//! Explicit run-owned subscriptions and exclusive gamepad state leases.
//! This is a resource boundary, not an IR scheduler or a node executor.
//!
//! ```no_run
//! use easycon_device::{Button, Gamepad};
//! use easycon_sense::{
//!     primitives::{PrimitiveCall, PrimitiveOutput},
//!     run::{RunBindings, RunCompletion, RunOutcome, RunScope},
//! };
//! use tokio_util::sync::CancellationToken;
//!
//! async fn press_once(
//!     pad: &mut dyn Gamepad,
//!     parent: &CancellationToken,
//! ) -> RunCompletion<PrimitiveOutput> {
//!     let mut bindings = RunBindings::default();
//!     bindings.pads.insert("switch".into(), pad);
//!     let mut run = RunScope::new(bindings, parent);
//!     let result = run.execute(PrimitiveCall::PadPress {
//!         target: "switch", buttons: &[Button::A],
//!     }).await;
//!     run.finish(RunOutcome::from(result))
//! }
//! ```

use std::{collections::BTreeMap, sync::Arc};

use easycon_device::Gamepad;
use thiserror::Error;
use tokio_util::sync::CancellationToken;

use crate::{
    meta::MetaContext,
    primitives::{
        AudioInput, Capability, ErrorKind, InputReceipt, PrimitiveCall, PrimitiveError,
        PrimitiveHost, PrimitiveOutput, Recovery, TextInput, VideoInput,
    },
    vision::{OcrBackend, TemplateMatcher},
};

#[cfg(test)]
mod tests;

/// Owned subscriptions must be dedicated to this run. Physical capture sessions stay outside.
/// Enrolling a pad grants exclusive control of its entire state, including a neutral reset
/// at scope exit even if no action ran. Other providers are borrowed without teardown.
#[derive(Default)]
pub struct RunBindings<'a> {
    pub videos: BTreeMap<String, Box<dyn VideoInput + 'a>>,
    pub audio: BTreeMap<String, Box<dyn AudioInput + 'a>>,
    pub matchers: BTreeMap<String, &'a mut dyn TemplateMatcher>,
    pub ocr: BTreeMap<String, &'a mut dyn OcrBackend>,
    pub pads: BTreeMap<String, &'a mut dyn Gamepad>,
    pub text: BTreeMap<String, &'a mut dyn TextInput>,
    pub meta: Option<&'a MetaContext>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ResourceId {
    pub capability: Capability,
    pub binding: String,
    /// Present for gamepad leases; identifies the actual connection generation.
    pub session: Option<u64>,
}

#[derive(Clone, Debug)]
pub enum CleanupState {
    NotRequired,
    /// A subscription was dropped, or a neutral report was delivered at the recorded level.
    /// Neither means the physical source was closed or the game responded.
    Released {
        receipt: Option<InputReceipt>,
    },
    ReleaseUnconfirmed {
        error: Arc<PrimitiveError>,
    },
}

#[derive(Clone, Debug)]
pub struct CleanupEntry {
    pub resource: ResourceId,
    pub state: CleanupState,
}

#[derive(Clone, Debug, Default)]
pub struct CleanupReport {
    pub entries: Vec<CleanupEntry>,
}

impl CleanupReport {
    pub fn has_failures(&self) -> bool {
        self.entries
            .iter()
            .any(|entry| matches!(entry.state, CleanupState::ReleaseUnconfirmed { .. }))
    }
}

#[derive(Debug, Error)]
pub enum RunFailure {
    #[error(transparent)]
    Primitive(Arc<PrimitiveError>),
    /// Individual typed errors remain in RunCompletion.cleanup.
    #[error("run resource release is unconfirmed; see cleanup report")]
    CleanupFailed,
}

impl RunFailure {
    pub fn kind(&self) -> ErrorKind {
        match self {
            Self::Primitive(error) => error.kind(),
            Self::CleanupFailed => ErrorKind::CleanupFailed,
        }
    }

    pub fn recovery(&self) -> Recovery {
        match self {
            Self::Primitive(error) => error.recovery(),
            Self::CleanupFailed => Recovery::RunFatal,
        }
    }
}

#[derive(Debug)]
pub enum RunOutcome<T> {
    Success(T),
    Failure(RunFailure),
    /// The caller's cancellation request; detailed cancellation reasons belong to the engine.
    Cancelled,
}

impl<T> From<std::result::Result<T, Arc<PrimitiveError>>> for RunOutcome<T> {
    fn from(result: std::result::Result<T, Arc<PrimitiveError>>) -> Self {
        match result {
            Ok(value) => Self::Success(value),
            Err(error) if error.recovery() == Recovery::Cancelled => Self::Cancelled,
            Err(error) => Self::Failure(RunFailure::Primitive(error)),
        }
    }
}

#[derive(Debug)]
#[must_use = "check outcome and cleanup before reporting that the run stopped"]
pub struct RunCompletion<T> {
    pub outcome: RunOutcome<T>,
    pub cleanup: CleanupReport,
    /// Candidates displaced by fatal failure, cancellation, then cleanup failure, in that order.
    /// Successful values and typed causes are retained without flattening them into strings.
    pub prior: Vec<RunOutcome<T>>,
}

#[must_use = "call finish to obtain a cleanup report; Drop is best effort only"]
/// Providers must return typed errors rather than panic, including during cleanup.
/// Cleanup is synchronous and cannot preempt a blocking provider or recover from process abort.
pub struct RunScope<'a> {
    bindings: RunBindings<'a>,
    token: CancellationToken,
    fatal: Option<Arc<PrimitiveError>>,
    cleaned: bool,
}

impl<'a> RunScope<'a> {
    pub fn new(bindings: RunBindings<'a>, parent: &CancellationToken) -> Self {
        Self {
            bindings,
            token: parent.child_token(),
            fatal: None,
            cleaned: false,
        }
    }

    pub fn cancel(&self) {
        self.token.cancel();
    }

    pub fn is_cancelled(&self) -> bool {
        self.token.is_cancelled()
    }

    /// Returned errors are shared so the original fatal cause can also latch this scope closed.
    /// Recoverable errors may be handled by the caller; cancellation and RunFatal cannot be
    /// cleared. No calls (including Meta) run after a fatal error.
    pub async fn execute(
        &mut self,
        call: PrimitiveCall<'_>,
    ) -> std::result::Result<PrimitiveOutput, Arc<PrimitiveError>> {
        let token = self.token.clone();
        self.execute_in(call, &token).await
    }

    /// Local cancellation closes the request scope without cancelling unrelated siblings.
    pub(crate) async fn execute_in(
        &mut self,
        call: PrimitiveCall<'_>,
        token: &CancellationToken,
    ) -> std::result::Result<PrimitiveOutput, Arc<PrimitiveError>> {
        if let Some(error) = &self.fatal {
            return Err(error.clone());
        }
        let result = self.host().execute(call, token).await.map_err(Arc::new);
        if let Err(error) = &result {
            match error.recovery() {
                Recovery::RunFatal => self.fatal = Some(error.clone()),
                Recovery::Cancelled => token.cancel(),
                Recovery::Recoverable => {}
            }
        }
        result
    }

    /// Only run this after the caller has joined any work it owns. Mutable borrowing prevents
    /// an execute future from remaining active here, but does not join external tasks.
    pub fn finish<T>(mut self, mut outcome: RunOutcome<T>) -> RunCompletion<T> {
        let mut prior = Vec::new();
        let candidate_fatal = matches!(&outcome,
            RunOutcome::Failure(error) if error.recovery() == Recovery::RunFatal);
        if let Some(error) = self.fatal.take() {
            if !matches!(&outcome, RunOutcome::Failure(RunFailure::Primitive(candidate)) if Arc::ptr_eq(candidate, &error))
            {
                prior.push(outcome);
            }
            outcome = RunOutcome::Failure(RunFailure::Primitive(error));
        } else if !candidate_fatal
            && self.token.is_cancelled()
            && !matches!(outcome, RunOutcome::Cancelled)
        {
            prior.push(outcome);
            outcome = RunOutcome::Cancelled;
        }
        let cleanup = self.cleanup();
        // Cleanup itself can trigger a parent cancellation before the terminal outcome is ready.
        if self.token.is_cancelled()
            && !matches!(&outcome, RunOutcome::Cancelled)
            && !matches!(&outcome, RunOutcome::Failure(error) if error.recovery() == Recovery::RunFatal)
        {
            prior.push(outcome);
            outcome = RunOutcome::Cancelled;
        }
        if cleanup.has_failures() {
            prior.push(outcome);
            outcome = RunOutcome::Failure(RunFailure::CleanupFailed);
        }
        RunCompletion {
            outcome,
            cleanup,
            prior,
        }
    }

    /// A child owns only pad state it touched; run-owned subscriptions remain enrolled.
    pub(crate) fn release_pads(
        &mut self,
        names: &std::collections::BTreeSet<String>,
    ) -> CleanupReport {
        let mut report = CleanupReport::default();
        for name in names {
            if let Some(pad) = self.bindings.pads.get_mut(name) {
                let resource = ResourceId {
                    capability: Capability::Gamepad,
                    binding: name.clone(),
                    session: Some(pad.info().session),
                };
                let state = match pad.reset(&CancellationToken::new()) {
                    Ok(receipt) => CleanupState::Released {
                        receipt: Some(receipt.into()),
                    },
                    Err(error) => CleanupState::ReleaseUnconfirmed {
                        error: Arc::new(error.into()),
                    },
                };
                report.entries.push(CleanupEntry { resource, state });
            }
        }
        report
    }

    fn host(&mut self) -> PrimitiveHost<'_> {
        PrimitiveHost {
            videos: self
                .bindings
                .videos
                .iter_mut()
                .map(|(name, input)| (name.clone(), &mut **input as &mut dyn VideoInput))
                .collect(),
            audio: self
                .bindings
                .audio
                .iter_mut()
                .map(|(name, input)| (name.clone(), &mut **input as &mut dyn AudioInput))
                .collect(),
            matchers: self
                .bindings
                .matchers
                .iter_mut()
                .map(|(name, provider)| (name.clone(), &mut **provider as &mut dyn TemplateMatcher))
                .collect(),
            ocr: self
                .bindings
                .ocr
                .iter_mut()
                .map(|(name, provider)| (name.clone(), &mut **provider as &mut dyn OcrBackend))
                .collect(),
            pads: self
                .bindings
                .pads
                .iter_mut()
                .map(|(name, provider)| (name.clone(), &mut **provider as &mut dyn Gamepad))
                .collect(),
            text: self
                .bindings
                .text
                .iter_mut()
                .map(|(name, provider)| (name.clone(), &mut **provider as &mut dyn TextInput))
                .collect(),
            meta: self.bindings.meta,
        }
    }

    fn cleanup(&mut self) -> CleanupReport {
        if self.cleaned {
            return CleanupReport::default();
        }
        self.cleaned = true;
        let mut report = CleanupReport::default();
        // A fresh token for every lease prevents cancellation of one cleanup from skipping others.
        for (binding, pad) in &mut self.bindings.pads {
            let resource = ResourceId {
                capability: Capability::Gamepad,
                binding: binding.clone(),
                session: Some(pad.info().session),
            };
            let state = match pad.reset(&CancellationToken::new()) {
                Ok(receipt) => CleanupState::Released {
                    receipt: Some(receipt.into()),
                },
                Err(error) => CleanupState::ReleaseUnconfirmed {
                    error: Arc::new(error.into()),
                },
            };
            report.entries.push(CleanupEntry { resource, state });
        }
        self.bindings.pads.clear();
        for (capability, names) in [
            (
                Capability::Video,
                self.bindings.videos.keys().cloned().collect::<Vec<_>>(),
            ),
            (
                Capability::Audio,
                self.bindings.audio.keys().cloned().collect(),
            ),
        ] {
            match capability {
                Capability::Video => self.bindings.videos.clear(),
                Capability::Audio => self.bindings.audio.clear(),
                _ => unreachable!(),
            }
            report
                .entries
                .extend(names.into_iter().map(|binding| CleanupEntry {
                    resource: ResourceId {
                        capability,
                        binding,
                        session: None,
                    },
                    state: CleanupState::Released { receipt: None },
                }));
        }
        for (capability, names) in [
            (
                Capability::TemplateMatcher,
                self.bindings.matchers.keys().cloned().collect::<Vec<_>>(),
            ),
            (Capability::Ocr, self.bindings.ocr.keys().cloned().collect()),
            (
                Capability::TextInput,
                self.bindings.text.keys().cloned().collect(),
            ),
            (
                Capability::Meta,
                if self.bindings.meta.is_some() {
                    vec!["run context".into()]
                } else {
                    vec![]
                },
            ),
        ] {
            report
                .entries
                .extend(names.into_iter().map(|binding| CleanupEntry {
                    resource: ResourceId {
                        capability,
                        binding,
                        session: None,
                    },
                    state: CleanupState::NotRequired,
                }));
        }
        self.bindings.matchers.clear();
        self.bindings.ocr.clear();
        self.bindings.text.clear();
        self.bindings.meta = None;
        report
    }
}

impl Drop for RunScope<'_> {
    fn drop(&mut self) {
        // Explicit finish is required to surface release errors. This only avoids stuck input
        // during accidental early return or unwinding; it never claims cleanup succeeded.
        let _ = self.cleanup();
    }
}
