//! Typed, versioned atomic calls. Arguments are already evaluated; no Expr, control flow,
//! retry, automatic trace or task spawning belongs in this layer.

use std::{borrow::Cow, collections::BTreeMap, sync::Arc};

use easycon_device::{Button, Gamepad, Hat, Report, Stick, StickId};
use tokio_util::sync::CancellationToken;

use crate::{
    SampleId,
    audio::AudioWindow,
    meta::{self, LogLevel, LogValue, MetaContext},
    video::VideoFrame,
    vision::{
        Image, MatchConfig, OcrBackend, OcrBackendKind, OcrResult, Rect, TemplateMatch,
        TemplateMatcher,
        measure::{self, ImageDifference, RegionStatistics},
    },
};

mod catalog;
mod error;
mod ports;
#[cfg(test)]
mod tests;
mod validation;

pub use catalog::{
    Cancellation, Capability, Completion, Kind, OutputOwnership, OutputType, Parameter,
    PrimitiveContract, PrimitiveId, ProviderOwnership, Scheduling,
};
pub use error::{ErrorKind, PrimitiveError, Recovery, Result};
pub use ports::{
    AudioInput, CaptureFuture, InputDelivery, InputProgress, InputReceipt, TextInput,
    TextInputError, VideoInput,
};

/// Explicit pixels only: OCR never captures a source or advances a subscription.
#[derive(Clone, Copy, Debug)]
pub enum ImageInput<'a> {
    Frame(&'a VideoFrame),
    Image(&'a Image),
}

impl<'a> ImageInput<'a> {
    fn resolve(self) -> Result<Cow<'a, Image>> {
        Ok(match self {
            Self::Frame(frame) => Cow::Owned(Image::from_frame(frame)?),
            Self::Image(image) => Cow::Borrowed(image),
        })
    }
}

/// Resolved arguments, not a serializable IR. Symbolic provider bindings are scoped to the host.
#[derive(Debug)]
pub enum PrimitiveCall<'a> {
    Capture {
        source: &'a str,
        after: Option<&'a SampleId>,
    },
    ReadAudio {
        source: &'a str,
        frames: i64,
    },
    RegionStatistics {
        image: &'a Image,
        region: Option<Rect>,
    },
    ImageDifference {
        left: &'a Image,
        right: &'a Image,
        left_region: Option<Rect>,
        right_region: Option<Rect>,
    },
    MatchTemplate {
        matcher: &'a str,
        image: &'a Image,
        template: &'a Image,
        config: MatchConfig,
    },
    Ocr {
        kind: OcrBackendKind,
        engine: &'a str,
        image: ImageInput<'a>,
        region: Option<Rect>,
    },
    AudioRms {
        window: &'a AudioWindow,
    },
    PadPress {
        target: &'a str,
        buttons: &'a [Button],
    },
    PadRelease {
        target: &'a str,
        buttons: &'a [Button],
    },
    PadSetHat {
        target: &'a str,
        hat: Hat,
    },
    PadSetStick {
        target: &'a str,
        stick: StickId,
        position: Stick,
    },
    PadCenterStick {
        target: &'a str,
        stick: StickId,
    },
    PadApplyReport {
        target: &'a str,
        report: Report,
    },
    PadReset {
        target: &'a str,
    },
    TypeText {
        target: &'a str,
        text: &'a str,
    },
    Sleep {
        duration_ms: i64,
    },
    ClockNow,
    Log {
        level: LogLevel,
        message: &'a str,
        fields: BTreeMap<String, LogValue>,
    },
}

impl PrimitiveCall<'_> {
    pub fn id(&self) -> PrimitiveId {
        match self {
            Self::Capture { .. } => PrimitiveId::Capture,
            Self::ReadAudio { .. } => PrimitiveId::ReadAudio,
            Self::RegionStatistics { .. } => PrimitiveId::RegionStatistics,
            Self::ImageDifference { .. } => PrimitiveId::ImageDifference,
            Self::MatchTemplate { .. } => PrimitiveId::MatchTemplate,
            Self::Ocr { .. } => PrimitiveId::Ocr,
            Self::AudioRms { .. } => PrimitiveId::AudioRms,
            Self::PadPress { .. } => PrimitiveId::PadPress,
            Self::PadRelease { .. } => PrimitiveId::PadRelease,
            Self::PadSetHat { .. } => PrimitiveId::PadSetHat,
            Self::PadSetStick { .. } => PrimitiveId::PadSetStick,
            Self::PadCenterStick { .. } => PrimitiveId::PadCenterStick,
            Self::PadApplyReport { .. } => PrimitiveId::PadApplyReport,
            Self::PadReset { .. } => PrimitiveId::PadReset,
            Self::TypeText { .. } => PrimitiveId::TypeText,
            Self::Sleep { .. } => PrimitiveId::Sleep,
            Self::ClockNow => PrimitiveId::ClockNow,
            Self::Log { .. } => PrimitiveId::Log,
        }
    }
}

#[derive(Debug)]
pub enum PrimitiveOutput {
    Unit,
    Frame(Arc<VideoFrame>),
    AudioWindow(Arc<AudioWindow>),
    RegionStatistics(RegionStatistics),
    ImageDifference(ImageDifference),
    Match(TemplateMatch),
    Ocr(OcrResult),
    Float(f64),
    InputReceipt(InputReceipt),
    Int(i64),
}

impl PrimitiveOutput {
    pub fn output_type(&self) -> OutputType {
        match self {
            Self::Unit => OutputType::Unit,
            Self::Frame(_) => OutputType::Frame,
            Self::AudioWindow(_) => OutputType::AudioWindow,
            Self::RegionStatistics(_) => OutputType::RegionStatistics,
            Self::ImageDifference(_) => OutputType::ImageDifference,
            Self::Match(_) => OutputType::Match,
            Self::Ocr(_) => OutputType::OcrResult,
            Self::Float(_) => OutputType::Float,
            Self::InputReceipt(_) => OutputType::InputReceipt,
            Self::Int(_) => OutputType::Int,
        }
    }
}

/// Borrows providers and subscriptions; the caller retains ownership and must clean them up.
/// Recognition providers must honor the fixed-asset, explicit-input Pure contract. These are
/// trusted host bindings, never metadata supplied by an untrusted IR document.
#[derive(Default)]
pub struct PrimitiveHost<'a> {
    pub videos: BTreeMap<String, &'a mut dyn VideoInput>,
    pub audio: BTreeMap<String, &'a mut dyn AudioInput>,
    pub matchers: BTreeMap<String, &'a mut dyn TemplateMatcher>,
    pub ocr: BTreeMap<String, &'a mut dyn OcrBackend>,
    pub pads: BTreeMap<String, &'a mut dyn Gamepad>,
    pub text: BTreeMap<String, &'a mut dyn TextInput>,
    pub meta: Option<&'a MetaContext>,
}

fn unavailable(capability: Capability, binding: &str) -> PrimitiveError {
    PrimitiveError::CapabilityUnavailable {
        capability,
        binding: binding.into(),
    }
}

impl PrimitiveHost<'_> {
    /// Execute exactly one call. CallerThread contracts execute synchronously on this thread;
    /// async hosts must arrange a managed worker with a runtime for the async contracts.
    /// Dropping this future does not create or detach any work. Do not discard successful
    /// input/log delivery if cancellation arrives after commit; the engine arbitrates afterward.
    pub async fn execute(
        &mut self,
        call: PrimitiveCall<'_>,
        token: &CancellationToken,
    ) -> Result<PrimitiveOutput> {
        if token.is_cancelled() {
            return Err(PrimitiveError::Cancelled);
        }
        let id = call.id();
        let pure = id.contract().kind == Kind::Pure;
        let output = match call {
            PrimitiveCall::Capture { source, after } => {
                let reader = self
                    .videos
                    .get_mut(source)
                    .ok_or_else(|| unavailable(Capability::Video, source))?;
                let frame = reader.capture(after, token).await?;
                validation::frame(&frame, after)?;
                PrimitiveOutput::Frame(frame)
            }
            PrimitiveCall::ReadAudio { source, frames } => {
                let frames = usize::try_from(frames)
                    .ok()
                    .filter(|n| *n > 0)
                    .ok_or_else(|| {
                        PrimitiveError::InvalidArgument(
                            "audio frames must be positive and fit usize".into(),
                        )
                    })?;
                let reader = self
                    .audio
                    .get_mut(source)
                    .ok_or_else(|| unavailable(Capability::Audio, source))?;
                let window = reader.read_window(frames, token).await?;
                validation::audio(&window, frames)?;
                PrimitiveOutput::AudioWindow(window)
            }
            PrimitiveCall::RegionStatistics { image, region } => {
                PrimitiveOutput::RegionStatistics(measure::region_statistics(image, region, token)?)
            }
            PrimitiveCall::ImageDifference {
                left,
                right,
                left_region,
                right_region,
            } => PrimitiveOutput::ImageDifference(measure::image_difference(
                left,
                right,
                left_region,
                right_region,
                token,
            )?),
            PrimitiveCall::MatchTemplate {
                matcher,
                image,
                template,
                config,
            } => {
                let search = validation::match_input(image, template, config)?;
                let backend = self
                    .matchers
                    .get_mut(matcher)
                    .ok_or_else(|| unavailable(Capability::TemplateMatcher, matcher))?;
                let result = backend
                    .match_template_async(image, template, config, token)
                    .await?;
                validation::template_match(&result, image, template, config, search)?;
                PrimitiveOutput::Match(result)
            }
            PrimitiveCall::Ocr {
                kind,
                engine,
                image,
                region,
            } => {
                let image = image.resolve()?;
                let selected = validation::region(&image, region)?;
                let backend = self
                    .ocr
                    .get_mut(engine)
                    .ok_or_else(|| unavailable(Capability::Ocr, engine))?;
                if backend.kind() != kind {
                    return Err(PrimitiveError::InvalidArgument(format!(
                        "ocr kind {kind:?} does not match provider {engine} ({:?})",
                        backend.kind()
                    )));
                }
                let result = backend.recognize_async(&image, region, token).await?;
                validation::ocr(&result, &image, selected, kind)?;
                PrimitiveOutput::Ocr(result)
            }
            PrimitiveCall::AudioRms { window } => {
                PrimitiveOutput::Float(window.rms_cancellable(token)?)
            }
            PrimitiveCall::PadPress { target, buttons } => {
                if buttons.is_empty() {
                    return Err(PrimitiveError::InvalidArgument(
                        "buttons must be nonempty".into(),
                    ));
                }
                let pad = self
                    .pads
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::Gamepad, target))?;
                PrimitiveOutput::InputReceipt(pad.press_buttons(buttons, token)?.into())
            }
            PrimitiveCall::PadRelease { target, buttons } => {
                if buttons.is_empty() {
                    return Err(PrimitiveError::InvalidArgument(
                        "buttons must be nonempty".into(),
                    ));
                }
                let pad = self
                    .pads
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::Gamepad, target))?;
                PrimitiveOutput::InputReceipt(pad.release_buttons(buttons, token)?.into())
            }
            PrimitiveCall::PadSetHat { target, hat } => {
                let pad = self
                    .pads
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::Gamepad, target))?;
                PrimitiveOutput::InputReceipt(pad.set_hat(hat, token)?.into())
            }
            PrimitiveCall::PadSetStick {
                target,
                stick,
                position,
            } => {
                let pad = self
                    .pads
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::Gamepad, target))?;
                PrimitiveOutput::InputReceipt(pad.set_stick(stick, position, token)?.into())
            }
            PrimitiveCall::PadCenterStick { target, stick } => {
                let pad = self
                    .pads
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::Gamepad, target))?;
                PrimitiveOutput::InputReceipt(pad.center_stick(stick, token)?.into())
            }
            PrimitiveCall::PadApplyReport { target, report } => {
                report.validate()?;
                let pad = self
                    .pads
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::Gamepad, target))?;
                PrimitiveOutput::InputReceipt(pad.apply_report(report, token)?.into())
            }
            PrimitiveCall::PadReset { target } => {
                let pad = self
                    .pads
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::Gamepad, target))?;
                PrimitiveOutput::InputReceipt(pad.reset(token)?.into())
            }
            PrimitiveCall::TypeText { target, text } => {
                let input = self
                    .text
                    .get_mut(target)
                    .ok_or_else(|| unavailable(Capability::TextInput, target))?;
                PrimitiveOutput::InputReceipt(input.type_text(text, token)?)
            }
            PrimitiveCall::Sleep { duration_ms } => {
                meta::sleep(duration_ms, token).await?;
                PrimitiveOutput::Unit
            }
            PrimitiveCall::ClockNow => {
                let meta = self
                    .meta
                    .ok_or_else(|| unavailable(Capability::Meta, "run context"))?;
                PrimitiveOutput::Int(meta.clock_now(token)?)
            }
            PrimitiveCall::Log {
                level,
                message,
                fields,
            } => {
                let meta = self
                    .meta
                    .ok_or_else(|| unavailable(Capability::Meta, "run context"))?;
                meta.log(level, message, fields, token)?;
                PrimitiveOutput::Unit
            }
        };
        if pure && token.is_cancelled() {
            return Err(PrimitiveError::Cancelled);
        }
        Ok(output)
    }
}
