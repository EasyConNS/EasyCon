//! Host-owned cancellation with a first-writer-wins reason, shared by all control clones.
//!
//! ```no_run
//! use std::{collections::BTreeMap, future::Future};
//! use easycon_sense::{
//!     ir::{Execution, RunControl, RunStopReason, ValidatedDocument, ValidationError},
//!     run::RunBindings,
//! };
//!
//! async fn run_until_stopped(
//!     document: &ValidatedDocument,
//!     host_stop: impl Future<Output = RunStopReason>,
//! ) -> Result<Execution, ValidationError> {
//!     let control = RunControl::new();
//!     let execution = document.execute_controlled(
//!         RunBindings::default(), BTreeMap::new(), &control,
//!     );
//!     tokio::pin!(execution);
//!     tokio::select! {
//!         result = &mut execution => result,
//!         reason = host_stop => {
//!             control.request_stop(reason);
//!             execution.await // Preserve native work draining and cleanup.
//!         }
//!     }
//! }
//! ```

use std::sync::{
    Arc,
    atomic::{AtomicU8, Ordering},
};
use tokio_util::sync::CancellationToken;

/// External stop sources. Deadline, sibling and budget cancellation belong to the engine.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u8)]
pub enum RunStopReason {
    UserStop = 1,
    Shutdown = 2,
    Aborted = 3,
}

#[derive(Debug)]
struct Inner {
    token: CancellationToken,
    reason: AtomicU8,
}

/// One run's stop control. A clone shares the request; create a new control for a new run.
/// Requesting a stop never discards the execution future: await its cleanup and outcome.
#[derive(Clone, Debug)]
pub struct RunControl(Arc<Inner>);

impl Default for RunControl {
    fn default() -> Self {
        Self::new()
    }
}

impl RunControl {
    pub fn new() -> Self {
        Self(Arc::new(Inner {
            token: CancellationToken::new(),
            reason: AtomicU8::new(0),
        }))
    }

    /// Returns true only for the first request. The reason is published before cancellation.
    pub fn request_stop(&self, reason: RunStopReason) -> bool {
        let accepted = self
            .0
            .reason
            .compare_exchange(0, reason as u8, Ordering::AcqRel, Ordering::Acquire)
            .is_ok();
        self.0.token.cancel();
        accepted
    }

    pub fn reason(&self) -> Option<RunStopReason> {
        match self.0.reason.load(Ordering::Acquire) {
            1 => Some(RunStopReason::UserStop),
            2 => Some(RunStopReason::Shutdown),
            3 => Some(RunStopReason::Aborted),
            _ => None,
        }
    }

    pub fn is_cancelled(&self) -> bool {
        self.0.token.is_cancelled()
    }

    pub async fn cancelled(&self) {
        self.0.token.cancelled().await;
    }

    pub(crate) fn token(&self) -> &CancellationToken {
        &self.0.token
    }
}
