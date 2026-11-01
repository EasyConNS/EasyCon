use thiserror::Error;

pub type Result<T> = std::result::Result<T, CaptureError>;

#[derive(Clone, Debug, Error, PartialEq, Eq)]
pub enum CaptureError {
    #[error("operation cancelled")]
    Cancelled,
    #[error("capture session stopped")]
    Stopped,
    #[error("source or session changed")]
    SourceChanged,
    #[error("audio discontinuity: expected frame {expected}, oldest available {oldest}")]
    AudioGap { expected: u64, oldest: u64 },
    #[error("invalid argument: {0}")]
    InvalidArgument(String),
    #[error("unsupported sample format: {0}")]
    UnsupportedFormat(String),
    #[error("video permission has not been granted; request it explicitly first")]
    PermissionRequired,
    #[error("video permission denied")]
    PermissionDenied,
    #[error("{backend} {operation}: {message}")]
    Backend {
        backend: &'static str,
        operation: &'static str,
        message: String,
    },
    #[error("invalid sample data: {0}")]
    InvalidData(String),
    #[error("capture worker panicked")]
    WorkerPanicked,
    #[error("resource cleanup failed: {message}")]
    CleanupFailed {
        message: String,
        previous: Option<Box<CaptureError>>,
    },
    #[error("internal error: {0}")]
    Internal(&'static str),
}

impl CaptureError {
    pub(crate) fn backend(
        backend: &'static str,
        operation: &'static str,
        error: impl std::fmt::Display,
    ) -> Self {
        Self::Backend {
            backend,
            operation,
            message: error.to_string(),
        }
    }
}
