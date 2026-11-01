use std::{future::Future, pin::Pin, sync::Arc, time::Instant};

use thiserror::Error;
use tokio_util::sync::CancellationToken;

use crate::{
    SampleId,
    audio::{AudioReader, AudioWindow},
    video::{VideoFrame, VideoReader},
};

/// Local future: native recognition providers need not be Send. The host owns scheduling.
pub type CaptureFuture<'a, T> = Pin<Box<dyn Future<Output = crate::Result<T>> + 'a>>;

/// One observation. With `after`, return a strictly newer frame of the same source/session.
/// A provider must report SourceChanged instead of silently crossing a reconnect boundary.
pub trait VideoInput {
    fn capture<'a>(
        &'a mut self,
        after: Option<&'a SampleId>,
        token: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<VideoFrame>>;
}

impl VideoInput for VideoReader {
    fn capture<'a>(
        &'a mut self,
        after: Option<&'a SampleId>,
        token: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<VideoFrame>> {
        Box::pin(VideoReader::capture(self, after, token))
    }
}

/// One contiguous window containing exactly `frames` complete interleaved sample frames.
/// A successful read advances this subscription; discarding/rejecting its output cannot undo it.
pub trait AudioInput {
    fn read_window<'a>(
        &'a mut self,
        frames: usize,
        token: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<AudioWindow>>;
}

impl AudioInput for AudioReader {
    fn read_window<'a>(
        &'a mut self,
        frames: usize,
        token: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<AudioWindow>> {
        Box::pin(AudioReader::read_window(self, frames, token))
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum InputDelivery {
    /// The OS/backend write completed; no game response is implied.
    Written,
    /// The input backend confirmed acceptance; no game response is implied.
    Accepted,
}

#[derive(Clone, Debug)]
pub struct InputReceipt {
    /// Process-local target session generation and monotonic report number.
    pub session: u64,
    pub sequence: u64,
    pub delivery: InputDelivery,
    pub written_at: Instant,
}

impl From<easycon_device::WriteReceipt> for InputReceipt {
    fn from(receipt: easycon_device::WriteReceipt) -> Self {
        Self {
            session: receipt.session,
            sequence: receipt.sequence,
            delivery: match receipt.delivery {
                easycon_device::Delivery::Written => InputDelivery::Written,
                easycon_device::Delivery::Accepted => InputDelivery::Accepted,
            },
            written_at: receipt.written_at,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum InputProgress {
    NotStarted,
    Unknown,
}

#[derive(Debug, Error)]
pub enum TextInputError {
    /// Only valid before delivery starts. Once started, return the actual bounded outcome.
    #[error("operation cancelled")]
    Cancelled,
    #[error("invalid text input: {0}")]
    InvalidArgument(String),
    #[error("text input transport: {message}; delivery: {progress:?}")]
    Transport {
        message: String,
        progress: InputProgress,
    },
}

/// Explicit, host-supplied text target; independent of the Switch gamepad capability.
/// Implementations must bound delivery time, check cancellation before sending, and must not
/// return Cancelled after committing delivery. No implicit character-to-gamepad conversion.
pub trait TextInput {
    fn type_text(
        &mut self,
        text: &str,
        token: &CancellationToken,
    ) -> std::result::Result<InputReceipt, TextInputError>;
}
