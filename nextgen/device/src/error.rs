use std::io;

use thiserror::Error;

pub type Result<T> = std::result::Result<T, DeviceError>;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AttemptFailure {
    pub baud_rate: u32,
    pub error: DeviceError,
}

#[derive(Clone, Debug, Error, PartialEq, Eq)]
pub enum DeviceError {
    #[error("invalid argument: {0}")]
    InvalidArgument(String),
    #[error("operation cancelled")]
    Cancelled,
    #[error("device session closed")]
    Closed,
    #[error("serial {operation}: {message}")]
    Io {
        operation: &'static str,
        kind: io::ErrorKind,
        message: String,
    },
    #[error("firmware handshake deadline exceeded")]
    HandshakeTimeout,
    #[error("firmware report acknowledgement deadline exceeded")]
    AcknowledgementTimeout,
    #[error("firmware rejected the report (code {code})")]
    ReportRejected { code: i8 },
    #[error("malformed firmware response: {0}")]
    MalformedResponse(String),
    #[error("serial write deadline exceeded after {written} bytes; delivery is uncertain")]
    WriteTimeout { written: usize },
    #[error("MCU connection failed: {attempts:?}")]
    ConnectionFailed { attempts: Vec<AttemptFailure> },
    #[error("serial protocol detection failed (EasyCon: {easycon}; Pokeldn: {pokeldn})")]
    ProtocolDetectionFailed {
        easycon: Box<DeviceError>,
        pokeldn: Box<DeviceError>,
    },
    #[error(
        "neutral report cleanup failed: {error}; controller release is unconfirmed (prior error: {previous:?})"
    )]
    CleanupFailed {
        #[source]
        error: Box<DeviceError>,
        previous: Option<Box<DeviceError>>,
    },
    #[error("diagnostic worker panicked")]
    WorkerPanicked,
}

impl DeviceError {
    pub(crate) fn io(operation: &'static str, error: io::Error) -> Self {
        Self::Io {
            operation,
            kind: error.kind(),
            message: error.to_string(),
        }
    }
}
