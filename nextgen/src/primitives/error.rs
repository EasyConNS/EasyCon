use thiserror::Error;

use crate::{CaptureError, meta::MetaError, vision::VisionError};
use easycon_device::DeviceError;

use super::{Capability, TextInputError};

pub type Result<T> = std::result::Result<T, PrimitiveError>;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ErrorKind {
    Cancelled,
    CapabilityUnavailable,
    SourceUnavailable,
    SourceChanged,
    AudioGap,
    InvalidArgument,
    InvalidData,
    AssetUnavailable,
    RecognitionError,
    TransportError,
    LogError,
    CleanupFailed,
    InternalError,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Recovery {
    Cancelled,
    Recoverable,
    RunFatal,
}

/// Keeps the original typed backend error. Node/run identity is attached by the future engine.
#[derive(Debug, Error)]
pub enum PrimitiveError {
    #[error("operation cancelled")]
    Cancelled,
    #[error("capability {capability:?} is unavailable: {binding}")]
    CapabilityUnavailable {
        capability: Capability,
        binding: String,
    },
    #[error("invalid argument: {0}")]
    InvalidArgument(String),
    #[error(transparent)]
    Capture(CaptureError),
    #[error(transparent)]
    Vision(VisionError),
    #[error(transparent)]
    Device(DeviceError),
    #[error(transparent)]
    Meta(MetaError),
    #[error(transparent)]
    Text(TextInputError),
}

impl PrimitiveError {
    pub fn kind(&self) -> ErrorKind {
        match self {
            Self::Cancelled => ErrorKind::Cancelled,
            Self::CapabilityUnavailable { .. } => ErrorKind::CapabilityUnavailable,
            Self::InvalidArgument(_) => ErrorKind::InvalidArgument,
            Self::Capture(error) => match error {
                CaptureError::Cancelled => ErrorKind::Cancelled,
                CaptureError::Stopped | CaptureError::Backend { .. } => {
                    ErrorKind::SourceUnavailable
                }
                CaptureError::SourceChanged => ErrorKind::SourceChanged,
                CaptureError::AudioGap { .. } => ErrorKind::AudioGap,
                CaptureError::InvalidArgument(_) | CaptureError::UnsupportedFormat(_) => {
                    ErrorKind::InvalidArgument
                }
                CaptureError::PermissionRequired | CaptureError::PermissionDenied => {
                    ErrorKind::CapabilityUnavailable
                }
                CaptureError::InvalidData(_) => ErrorKind::InvalidData,
                CaptureError::CleanupFailed { .. } => ErrorKind::CleanupFailed,
                CaptureError::Internal(_) | CaptureError::WorkerPanicked => {
                    ErrorKind::InternalError
                }
            },
            Self::Vision(error) => match error {
                VisionError::Cancelled => ErrorKind::Cancelled,
                VisionError::InvalidArgument(_) | VisionError::InvalidImage(_) => {
                    ErrorKind::InvalidArgument
                }
                VisionError::BackendUnavailable(_) | VisionError::BackendNotImplemented(_) => {
                    ErrorKind::CapabilityUnavailable
                }
                VisionError::Asset { .. } => ErrorKind::AssetUnavailable,
                VisionError::Worker(_) => ErrorKind::InternalError,
                VisionError::Backend { .. } | VisionError::InvalidOutput(_) => {
                    ErrorKind::RecognitionError
                }
            },
            Self::Device(error) => device_kind(error),
            Self::Meta(error) => match error {
                MetaError::Cancelled => ErrorKind::Cancelled,
                MetaError::InvalidArgument(_) => ErrorKind::InvalidArgument,
                MetaError::ClockOverflow => ErrorKind::InternalError,
                MetaError::Sink(_) => ErrorKind::LogError,
            },
            Self::Text(error) => match error {
                TextInputError::Cancelled => ErrorKind::Cancelled,
                TextInputError::InvalidArgument(_) => ErrorKind::InvalidArgument,
                TextInputError::Transport { .. } => ErrorKind::TransportError,
            },
        }
    }

    pub fn recovery(&self) -> Recovery {
        match self.kind() {
            ErrorKind::Cancelled => Recovery::Cancelled,
            ErrorKind::CleanupFailed | ErrorKind::InternalError => Recovery::RunFatal,
            _ => Recovery::Recoverable,
        }
    }
}

fn device_kind(error: &DeviceError) -> ErrorKind {
    match error {
        DeviceError::Cancelled => ErrorKind::Cancelled,
        DeviceError::InvalidArgument(_) => ErrorKind::InvalidArgument,
        DeviceError::Closed => ErrorKind::SourceUnavailable,
        DeviceError::CleanupFailed { .. } => ErrorKind::CleanupFailed,
        DeviceError::WorkerPanicked => ErrorKind::InternalError,
        DeviceError::ProtocolDetectionFailed { easycon, pokeldn } => {
            let kinds = [device_kind(easycon), device_kind(pokeldn)];
            if kinds.contains(&ErrorKind::CleanupFailed) {
                ErrorKind::CleanupFailed
            } else if kinds.contains(&ErrorKind::InternalError) {
                ErrorKind::InternalError
            } else {
                ErrorKind::TransportError
            }
        }
        DeviceError::ConnectionFailed { attempts } => {
            // Nested cleanup failures must not be downgraded to a recoverable transport error.
            if attempts
                .iter()
                .any(|attempt| device_kind(&attempt.error) == ErrorKind::CleanupFailed)
            {
                ErrorKind::CleanupFailed
            } else if attempts
                .iter()
                .any(|attempt| device_kind(&attempt.error) == ErrorKind::InternalError)
            {
                ErrorKind::InternalError
            } else {
                ErrorKind::TransportError
            }
        }
        _ => ErrorKind::TransportError,
    }
}

impl From<CaptureError> for PrimitiveError {
    fn from(error: CaptureError) -> Self {
        if error == CaptureError::Cancelled {
            Self::Cancelled
        } else {
            Self::Capture(error)
        }
    }
}
impl From<VisionError> for PrimitiveError {
    fn from(error: VisionError) -> Self {
        if matches!(error, VisionError::Cancelled) {
            Self::Cancelled
        } else {
            Self::Vision(error)
        }
    }
}
impl From<DeviceError> for PrimitiveError {
    fn from(error: DeviceError) -> Self {
        if error == DeviceError::Cancelled {
            Self::Cancelled
        } else {
            Self::Device(error)
        }
    }
}
impl From<MetaError> for PrimitiveError {
    fn from(error: MetaError) -> Self {
        if error == MetaError::Cancelled {
            Self::Cancelled
        } else {
            Self::Meta(error)
        }
    }
}
impl From<TextInputError> for PrimitiveError {
    fn from(error: TextInputError) -> Self {
        if matches!(error, TextInputError::Cancelled) {
            Self::Cancelled
        } else {
            Self::Text(error)
        }
    }
}
