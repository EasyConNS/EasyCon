//! Closed, versioned contracts for the built-in atomic capabilities.

use super::ErrorKind;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind {
    Pure,
    Effectful,
    Meta,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Capability {
    Video,
    Audio,
    TemplateMatcher,
    Ocr,
    Gamepad,
    TextInput,
    Meta,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum OutputType {
    Unit,
    Frame,
    AudioWindow,
    RegionStatistics,
    ImageDifference,
    Match,
    OcrResult,
    Float,
    InputReceipt,
    Int,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Scheduling {
    /// Await on the caller's runtime; no detached tasks are created.
    Async,
    /// May block or perform CPU work. The host must supply a managed worker thread.
    CallerThread,
    /// Await the provider's async entry; inline adapters may still block the caller.
    Provider,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Cancellation {
    DuringWait,
    BetweenBatches,
    BeforeAndAfterNativeCall,
    /// Once delivery starts, preserve the actual outcome. Blocking bounds are provider-specific.
    BeforeCommit,
    BeforeCall,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ProviderOwnership {
    None,
    /// The host owns the provider; completing/dropping a call does not close it.
    Borrowed,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum OutputOwnership {
    Value,
    /// Immutable, reference-counted sample; no physical source ownership is transferred.
    SharedSample,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Completion {
    Computed,
    SampleAcquired,
    /// A successful read advances this subscription, even if its caller discards the result.
    SubscriptionAdvanced,
    /// See InputReceipt.delivery; success does not imply a game response.
    InputDelivered,
    WaitFinished,
    ClockRead,
    /// The sink returned success; persistence depends on the sink's own contract.
    LogSubmitted,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Parameter {
    pub name: &'static str,
    /// Contract vocabulary; actual Rust argument types are enforced by PrimitiveCall.
    pub ty: &'static str,
}

#[derive(Clone, Copy, Debug)]
pub struct PrimitiveContract {
    pub id: PrimitiveId,
    pub name: &'static str,
    pub version: u32,
    pub kind: Kind,
    pub inputs: &'static [Parameter],
    pub output: OutputType,
    pub capability: Option<Capability>,
    pub scheduling: Scheduling,
    pub cancellation: Cancellation,
    pub provider_ownership: ProviderOwnership,
    pub output_ownership: OutputOwnership,
    pub completion: Completion,
    pub errors: &'static [ErrorKind],
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PrimitiveId {
    Capture,
    ReadAudio,
    RegionStatistics,
    ImageDifference,
    MatchTemplate,
    Ocr,
    AudioRms,
    PadPress,
    PadRelease,
    PadSetHat,
    PadSetStick,
    PadCenterStick,
    PadApplyReport,
    PadReset,
    TypeText,
    Sleep,
    ClockNow,
    Log,
}

const MEDIA_ERRORS: &[ErrorKind] = &[
    ErrorKind::Cancelled,
    ErrorKind::CapabilityUnavailable,
    ErrorKind::SourceUnavailable,
    ErrorKind::SourceChanged,
    ErrorKind::AudioGap,
    ErrorKind::InvalidArgument,
    ErrorKind::InvalidData,
    ErrorKind::CleanupFailed,
    ErrorKind::InternalError,
];
const VISION_ERRORS: &[ErrorKind] = &[
    ErrorKind::Cancelled,
    ErrorKind::CapabilityUnavailable,
    ErrorKind::InvalidArgument,
    ErrorKind::AssetUnavailable,
    ErrorKind::RecognitionError,
    ErrorKind::InternalError,
];
const PAD_ERRORS: &[ErrorKind] = &[
    ErrorKind::Cancelled,
    ErrorKind::CapabilityUnavailable,
    ErrorKind::InvalidArgument,
    ErrorKind::SourceUnavailable,
    ErrorKind::TransportError,
    ErrorKind::CleanupFailed,
    ErrorKind::InternalError,
];

impl PrimitiveId {
    pub const ALL: [Self; 18] = [
        Self::Capture,
        Self::ReadAudio,
        Self::RegionStatistics,
        Self::ImageDifference,
        Self::MatchTemplate,
        Self::Ocr,
        Self::AudioRms,
        Self::PadPress,
        Self::PadRelease,
        Self::PadSetHat,
        Self::PadSetStick,
        Self::PadCenterStick,
        Self::PadApplyReport,
        Self::PadReset,
        Self::TypeText,
        Self::Sleep,
        Self::ClockNow,
        Self::Log,
    ];

    /// Exact name/version lookup. Unknown contracts never select a fallback implementation.
    pub fn lookup(name: &str, version: u32) -> Option<Self> {
        Self::ALL.into_iter().find(|id| {
            let contract = id.contract();
            contract.name == name && contract.version == version
        })
    }

    pub fn contract(self) -> PrimitiveContract {
        use Cancellation as C;
        use Capability as Cap;
        use Kind as K;
        use OutputType as O;
        use Scheduling as S;
        let (name, kind, output, capability, scheduling, cancellation) = match self {
            Self::Capture => (
                "capture",
                K::Effectful,
                O::Frame,
                Some(Cap::Video),
                S::Async,
                C::DuringWait,
            ),
            Self::ReadAudio => (
                "read_audio",
                K::Effectful,
                O::AudioWindow,
                Some(Cap::Audio),
                S::Async,
                C::DuringWait,
            ),
            Self::RegionStatistics => (
                "region_statistics",
                K::Pure,
                O::RegionStatistics,
                None,
                S::CallerThread,
                C::BetweenBatches,
            ),
            Self::ImageDifference => (
                "image_difference",
                K::Pure,
                O::ImageDifference,
                None,
                S::CallerThread,
                C::BetweenBatches,
            ),
            Self::MatchTemplate => (
                "match_template",
                K::Pure,
                O::Match,
                Some(Cap::TemplateMatcher),
                S::Provider,
                C::BeforeAndAfterNativeCall,
            ),
            Self::Ocr => (
                "ocr",
                K::Pure,
                O::OcrResult,
                Some(Cap::Ocr),
                S::Provider,
                C::BeforeAndAfterNativeCall,
            ),
            Self::AudioRms => (
                "audio_rms",
                K::Pure,
                O::Float,
                None,
                S::CallerThread,
                C::BetweenBatches,
            ),
            Self::PadPress => (
                "pad_press",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::Gamepad),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::PadRelease => (
                "pad_release",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::Gamepad),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::PadSetHat => (
                "pad_set_hat",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::Gamepad),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::PadSetStick => (
                "pad_set_stick",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::Gamepad),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::PadCenterStick => (
                "pad_center_stick",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::Gamepad),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::PadApplyReport => (
                "pad_apply_report",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::Gamepad),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::PadReset => (
                "pad_reset",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::Gamepad),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::TypeText => (
                "type_text",
                K::Effectful,
                O::InputReceipt,
                Some(Cap::TextInput),
                S::CallerThread,
                C::BeforeCommit,
            ),
            Self::Sleep => ("sleep", K::Meta, O::Unit, None, S::Async, C::DuringWait),
            Self::ClockNow => (
                "clock_now",
                K::Meta,
                O::Int,
                Some(Cap::Meta),
                S::CallerThread,
                C::BeforeCall,
            ),
            Self::Log => (
                "log",
                K::Meta,
                O::Unit,
                Some(Cap::Meta),
                S::CallerThread,
                C::BeforeCommit,
            ),
        };
        let inputs: &'static [Parameter] = match self {
            Self::Capture => &[
                Parameter {
                    name: "source",
                    ty: "VideoSourceRef",
                },
                Parameter {
                    name: "after",
                    ty: "Option<SampleId>",
                },
            ],
            Self::ReadAudio => &[
                Parameter {
                    name: "source",
                    ty: "AudioSourceRef",
                },
                Parameter {
                    name: "frames",
                    ty: "Int>0",
                },
            ],
            Self::RegionStatistics => &[
                Parameter {
                    name: "image",
                    ty: "ImageRef",
                },
                Parameter {
                    name: "region",
                    ty: "Option<Rect>",
                },
            ],
            Self::ImageDifference => &[
                Parameter {
                    name: "left",
                    ty: "ImageRef",
                },
                Parameter {
                    name: "right",
                    ty: "ImageRef",
                },
                Parameter {
                    name: "left_region",
                    ty: "Option<Rect>",
                },
                Parameter {
                    name: "right_region",
                    ty: "Option<Rect>",
                },
            ],
            Self::MatchTemplate => &[
                Parameter {
                    name: "matcher",
                    ty: "MatcherRef",
                },
                Parameter {
                    name: "image",
                    ty: "ImageRef",
                },
                Parameter {
                    name: "template",
                    ty: "ImageRef",
                },
                Parameter {
                    name: "config",
                    ty: "MatchConfig",
                },
            ],
            Self::Ocr => &[
                Parameter {
                    name: "kind",
                    ty: "OcrKind",
                },
                Parameter {
                    name: "engine",
                    ty: "OcrRef",
                },
                Parameter {
                    name: "image",
                    ty: "FrameOrImageRef",
                },
                Parameter {
                    name: "region",
                    ty: "Option<Rect>",
                },
            ],
            Self::AudioRms => &[Parameter {
                name: "window",
                ty: "AudioWindowRef",
            }],
            Self::PadPress | Self::PadRelease => &[
                Parameter {
                    name: "target",
                    ty: "GamepadRef",
                },
                Parameter {
                    name: "buttons",
                    ty: "NonEmptyList<Button>",
                },
            ],
            Self::PadSetHat => &[
                Parameter {
                    name: "target",
                    ty: "GamepadRef",
                },
                Parameter {
                    name: "hat",
                    ty: "Hat",
                },
            ],
            Self::PadSetStick => &[
                Parameter {
                    name: "target",
                    ty: "GamepadRef",
                },
                Parameter {
                    name: "stick",
                    ty: "StickId",
                },
                Parameter {
                    name: "position",
                    ty: "Stick",
                },
            ],
            Self::PadCenterStick => &[
                Parameter {
                    name: "target",
                    ty: "GamepadRef",
                },
                Parameter {
                    name: "stick",
                    ty: "StickId",
                },
            ],
            Self::PadApplyReport => &[
                Parameter {
                    name: "target",
                    ty: "GamepadRef",
                },
                Parameter {
                    name: "report",
                    ty: "Report",
                },
            ],
            Self::PadReset => &[Parameter {
                name: "target",
                ty: "GamepadRef",
            }],
            Self::TypeText => &[
                Parameter {
                    name: "target",
                    ty: "TextTargetRef",
                },
                Parameter {
                    name: "text",
                    ty: "Str",
                },
            ],
            Self::Sleep => &[Parameter {
                name: "duration_ms",
                ty: "Int>=0",
            }],
            Self::ClockNow => &[],
            Self::Log => &[
                Parameter {
                    name: "level",
                    ty: "LogLevel",
                },
                Parameter {
                    name: "message",
                    ty: "Str",
                },
                Parameter {
                    name: "fields",
                    ty: "LogFields",
                },
            ],
        };
        let errors: &'static [ErrorKind] = match self {
            Self::Capture | Self::ReadAudio => MEDIA_ERRORS,
            Self::RegionStatistics | Self::ImageDifference | Self::Sleep => {
                &[ErrorKind::Cancelled, ErrorKind::InvalidArgument]
            }
            Self::MatchTemplate | Self::Ocr => VISION_ERRORS,
            Self::AudioRms => &[
                ErrorKind::Cancelled,
                ErrorKind::InvalidArgument,
                ErrorKind::InvalidData,
            ],
            Self::PadPress
            | Self::PadRelease
            | Self::PadSetHat
            | Self::PadSetStick
            | Self::PadCenterStick
            | Self::PadApplyReport
            | Self::PadReset => PAD_ERRORS,
            Self::TypeText => &[
                ErrorKind::Cancelled,
                ErrorKind::CapabilityUnavailable,
                ErrorKind::InvalidArgument,
                ErrorKind::TransportError,
            ],
            Self::ClockNow => &[
                ErrorKind::Cancelled,
                ErrorKind::CapabilityUnavailable,
                ErrorKind::InternalError,
            ],
            Self::Log => &[
                ErrorKind::Cancelled,
                ErrorKind::CapabilityUnavailable,
                ErrorKind::InvalidArgument,
                ErrorKind::InternalError,
                ErrorKind::LogError,
            ],
        };
        PrimitiveContract {
            id: self,
            name,
            version: 1,
            kind,
            inputs,
            output,
            capability,
            scheduling,
            cancellation,
            provider_ownership: if capability.is_some() {
                ProviderOwnership::Borrowed
            } else {
                ProviderOwnership::None
            },
            output_ownership: match self {
                Self::Capture | Self::ReadAudio => OutputOwnership::SharedSample,
                _ => OutputOwnership::Value,
            },
            completion: match self {
                Self::Capture => Completion::SampleAcquired,
                Self::ReadAudio => Completion::SubscriptionAdvanced,
                Self::PadPress
                | Self::PadRelease
                | Self::PadSetHat
                | Self::PadSetStick
                | Self::PadCenterStick
                | Self::PadApplyReport
                | Self::PadReset
                | Self::TypeText => Completion::InputDelivered,
                Self::Sleep => Completion::WaitFinished,
                Self::ClockNow => Completion::ClockRead,
                Self::Log => Completion::LogSubmitted,
                _ => Completion::Computed,
            },
            errors,
        }
    }
}
