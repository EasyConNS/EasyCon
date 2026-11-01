use std::{
    sync::{Arc, mpsc},
    thread::{self, JoinHandle},
};

use tokio::sync::watch;
use tokio_util::sync::CancellationToken;

use crate::{CaptureClock, CaptureError, Result, SampleId, SampleTime, SessionId};

#[cfg(test)]
mod tests;

#[derive(Clone, Debug)]
pub struct VideoDevice {
    /// Name IDs are scoped to the current device list, not stable hardware identities.
    pub id: String,
    pub name: String,
    pub description: String,
}

#[derive(Clone, Debug, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct VideoFormat {
    pub width: u32,
    pub height: u32,
    /// Backend-reported whole-number FPS; 0 means unknown (also for fractional rates).
    pub frames_per_second: u32,
    pub encoding: String,
}

#[derive(Debug)]
pub struct VideoFrame {
    pub id: SampleId,
    pub time: SampleTime,
    pub format: VideoFormat,
    /// Tightly packed RGB8. `format.encoding` records ccap output before RGB8 normalization.
    pub rgb: Arc<[u8]>,
}

/// A single backend sample, with decoding performed by the backend.
pub struct VideoBuffer {
    pub rgb: Vec<u8>,
    pub source_ns: Option<u64>,
}

/// Constructed and owned on the capture worker; a backend need not be Send.
pub trait VideoSource: 'static {
    fn format(&self) -> VideoFormat;
    fn next_frame(&mut self) -> Result<VideoBuffer>;
    fn stop(&mut self) -> Result<()>;
}

#[derive(Clone, Debug)]
enum VideoState {
    Running(Option<Arc<VideoFrame>>),
    Stopped,
    Failed(CaptureError),
}

mod ccap;
use ccap::CcapSource;
pub use ccap::devices;
pub(crate) use ccap::validate_device_id;

/// Probe the actual default stream, rather than advertise unverified mode combinations.
pub fn formats(device_id: &str) -> Result<Vec<VideoFormat>> {
    let mut session = VideoSession::open_default(device_id, CaptureClock::default())?;
    let format = session.format().clone();
    session.stop()?;
    Ok(vec![format])
}

/// Owns a physical source and its worker. `open`, `stop` and Drop may block in native code.
pub struct VideoSession {
    id: SampleId,
    format: VideoFormat,
    receiver: watch::Receiver<VideoState>,
    stop: CancellationToken,
    worker: Option<JoinHandle<Result<()>>>,
    stop_result: Option<Result<()>>,
}

impl VideoSession {
    /// Open the device default stream and derive dimensions from the first actual frame.
    pub fn open_default(device_id: &str, clock: CaptureClock) -> Result<Self> {
        let id = device_id.to_owned();
        let source = format!("ccap:{device_id}");
        Self::from_source(source, clock, move || {
            Ok(Box::new(CcapSource::open(&id, None)?))
        })
    }

    /// Require the default stream to match these constraints; 0 FPS leaves the rate unconstrained.
    /// The safe ccap API cannot reliably negotiate arbitrary modes on every platform.
    pub fn open(device_id: &str, format: VideoFormat, clock: CaptureClock) -> Result<Self> {
        if format.width == 0 || format.height == 0 || format.encoding.is_empty() {
            return Err(CaptureError::InvalidArgument(
                "video dimensions and encoding are required".into(),
            ));
        }
        let id = device_id.to_owned();
        let source = format!("ccap:{device_id}");
        Self::from_source(source, clock, move || {
            Ok(Box::new(CcapSource::open(&id, Some(&format))?))
        })
    }

    /// Injection seam for synthetic sources and tests; the factory runs on the worker.
    pub fn from_source(
        source: String,
        clock: CaptureClock,
        factory: impl FnOnce() -> Result<Box<dyn VideoSource>> + Send + 'static,
    ) -> Result<Self> {
        let id = SampleId {
            source,
            session: SessionId::next(),
            sequence: 0,
        };
        let (sender, receiver) = watch::channel(VideoState::Running(None));
        let (ready_tx, ready_rx) = mpsc::sync_channel(1);
        let stop = CancellationToken::new();
        let worker_stop = stop.clone();
        let worker_id = id.clone();
        let worker = thread::Builder::new()
            .name("easycon-video".into())
            .spawn(move || {
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    let mut source = factory()?;
                    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                        let format = source.format();
                        let expected = (format.width as usize)
                            .checked_mul(format.height as usize)
                            .and_then(|pixels| pixels.checked_mul(3))
                            .filter(|size| *size > 0)
                            .ok_or_else(|| {
                                CaptureError::InvalidData("invalid RGB8 dimensions".into())
                            })?;
                        ready_tx
                            .send(format.clone())
                            .map_err(|_| CaptureError::Stopped)?;
                        let mut sequence = 0u64;
                        while !worker_stop.is_cancelled() {
                            let buffer = source.next_frame()?;
                            let received_ns = clock.now_ns();
                            if worker_stop.is_cancelled() {
                                break;
                            }
                            if buffer.rgb.len() != expected {
                                return Err(CaptureError::InvalidData(
                                    "RGB8 buffer length does not match dimensions".into(),
                                ));
                            }
                            sequence = sequence
                                .checked_add(1)
                                .ok_or(CaptureError::Internal("video sequence overflow"))?;
                            let frame = Arc::new(VideoFrame {
                                id: SampleId {
                                    sequence,
                                    ..worker_id.clone()
                                },
                                time: SampleTime {
                                    received_ns,
                                    source_ns: buffer.source_ns,
                                },
                                format: format.clone(),
                                rgb: buffer.rgb.into(),
                            });
                            sender.send_replace(VideoState::Running(Some(frame)));
                        }
                        Ok(())
                    }))
                    .unwrap_or(Err(CaptureError::WorkerPanicked));
                    if let Err(error) = source.stop() {
                        return Err(CaptureError::CleanupFailed {
                            message: error.to_string(),
                            previous: result.err().map(Box::new),
                        });
                    }
                    result
                }))
                .unwrap_or(Err(CaptureError::WorkerPanicked));
                sender.send_replace(match &result {
                    Ok(()) => VideoState::Stopped,
                    Err(error) => VideoState::Failed(error.clone()),
                });
                result
            })
            .map_err(|e| CaptureError::backend("video worker", "spawn", e))?;
        match ready_rx.recv() {
            Ok(format) => Ok(Self {
                id,
                format,
                receiver,
                stop,
                worker: Some(worker),
                stop_result: None,
            }),
            Err(_) => match worker.join() {
                Ok(Err(error)) => Err(error),
                _ => Err(CaptureError::WorkerPanicked),
            },
        }
    }

    pub fn format(&self) -> &VideoFormat {
        &self.format
    }

    pub fn subscribe(&self) -> VideoReader {
        VideoReader {
            id: self.id.clone(),
            receiver: self.receiver.clone(),
        }
    }

    /// Joins the worker; a blocked native frame read is not forcibly interrupted.
    pub fn stop(&mut self) -> Result<()> {
        if let Some(result) = &self.stop_result {
            return result.clone();
        }
        self.stop.cancel();
        let result = match self.worker.take() {
            Some(worker) => worker.join().unwrap_or(Err(CaptureError::WorkerPanicked)),
            None => Ok(()),
        };
        self.stop_result = Some(result.clone());
        result
    }
}

impl Drop for VideoSession {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

#[derive(Clone)]
pub struct VideoReader {
    id: SampleId,
    receiver: watch::Receiver<VideoState>,
}

impl VideoReader {
    pub async fn capture(
        &mut self,
        after: Option<&SampleId>,
        token: &CancellationToken,
    ) -> Result<Arc<VideoFrame>> {
        if token.is_cancelled() {
            return Err(CaptureError::Cancelled);
        }
        if after.is_some_and(|id| id.source != self.id.source || id.session != self.id.session) {
            return Err(CaptureError::SourceChanged);
        }
        loop {
            if token.is_cancelled() {
                return Err(CaptureError::Cancelled);
            }
            let state = self.receiver.borrow_and_update().clone();
            match state {
                VideoState::Failed(error) => return Err(error),
                VideoState::Stopped => return Err(CaptureError::Stopped),
                VideoState::Running(Some(frame))
                    if after.is_none_or(|id| frame.id.sequence > id.sequence) =>
                {
                    return Ok(frame);
                }
                _ => {}
            }
            tokio::select! {
                biased;
                _ = token.cancelled() => return Err(CaptureError::Cancelled),
                changed = self.receiver.changed() => changed.map_err(|_| CaptureError::Stopped)?,
            }
        }
    }
}
