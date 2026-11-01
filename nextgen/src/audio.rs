use std::sync::{
    Arc, Mutex, TryLockError,
    atomic::{AtomicU64, Ordering},
};

use cpal::{
    FromSample, Sample, SampleFormat, SizedSample,
    traits::{DeviceTrait, HostTrait, StreamTrait},
};
use tokio::sync::Notify;
use tokio_util::sync::CancellationToken;

use crate::{CaptureClock, CaptureError, Result, SampleId, SampleTime, SessionId};

#[derive(Clone, Debug)]
pub struct AudioDevice {
    pub id: String,
    pub name: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AudioFormat {
    pub sample_rate: u32,
    pub channels: u16,
    pub sample_format: String,
}

#[derive(Clone, Debug)]
pub struct AudioFormatRange {
    pub min_sample_rate: u32,
    pub max_sample_rate: u32,
    pub channels: u16,
    pub sample_format: String,
}

#[derive(Clone, Debug)]
pub struct AudioConfig {
    /// None uses the selected device's advertised default input format.
    pub format: Option<AudioFormat>,
    /// Sample frames, independent of channel count. Defaults to two seconds at 48 kHz.
    pub history_frames: usize,
}

impl Default for AudioConfig {
    fn default() -> Self {
        Self {
            format: None,
            history_frames: 96_000,
        }
    }
}

#[derive(Debug)]
pub struct AudioWindow {
    pub id: SampleId,
    pub time: SampleTime,
    pub sample_rate: u32,
    pub channels: u16,
    /// Interleaved normalized samples; source format remains available on AudioSession.
    pub samples: Arc<[f32]>,
}

impl AudioWindow {
    pub fn frames(&self) -> usize {
        self.samples
            .len()
            .checked_div(usize::from(self.channels))
            .unwrap_or(0)
    }

    /// Pure measurement. No silence threshold or source reads are hidden here.
    pub fn rms(&self) -> Result<f64> {
        self.measure_rms(None)
    }

    /// The same measurement, checking cancellation every 4096 interleaved samples.
    pub fn rms_cancellable(&self, token: &CancellationToken) -> Result<f64> {
        self.measure_rms(Some(token))
    }

    fn measure_rms(&self, token: Option<&CancellationToken>) -> Result<f64> {
        let check = || {
            if token.is_some_and(CancellationToken::is_cancelled) {
                Err(CaptureError::Cancelled)
            } else {
                Ok(())
            }
        };
        check()?;
        if self.channels == 0
            || !self
                .samples
                .len()
                .is_multiple_of(usize::from(self.channels))
            || self.samples.is_empty()
        {
            return Err(CaptureError::InvalidData(
                "RMS requires nonempty finite samples".into(),
            ));
        }
        let mut sum = 0.0;
        for chunk in self.samples.chunks(4096) {
            check()?;
            for &value in chunk {
                if !value.is_finite() {
                    return Err(CaptureError::InvalidData(
                        "RMS requires nonempty finite samples".into(),
                    ));
                }
                sum += f64::from(value).powi(2);
            }
        }
        check()?;
        Ok((sum / self.samples.len() as f64).sqrt())
    }
}

pub fn devices() -> Result<Vec<AudioDevice>> {
    cpal::default_host()
        .input_devices()
        .map_err(|e| CaptureError::backend("cpal", "enumerate", e))?
        .map(|device| {
            Ok(AudioDevice {
                id: device
                    .id()
                    .map_err(|e| CaptureError::backend("cpal", "device ID", e))?
                    .to_string(),
                name: device
                    .description()
                    .map_err(|e| CaptureError::backend("cpal", "description", e))?
                    .name()
                    .to_owned(),
            })
        })
        .collect()
}

fn input_device(id: &str) -> Result<cpal::Device> {
    let id = id
        .parse::<cpal::DeviceId>()
        .map_err(|e| CaptureError::InvalidArgument(e.to_string()))?;
    let device = cpal::default_host().device_by_id(&id).ok_or_else(|| {
        CaptureError::backend("cpal", "find device", "input device no longer available")
    })?;
    if !device.supports_input() {
        return Err(CaptureError::InvalidArgument(
            "selected device is not an audio input".into(),
        ));
    }
    Ok(device)
}

pub fn formats(id: &str) -> Result<Vec<AudioFormatRange>> {
    let formats = input_device(id)?
        .supported_input_configs()
        .map_err(|e| CaptureError::backend("cpal", "query formats", e))?
        .map(|config| AudioFormatRange {
            min_sample_rate: config.min_sample_rate(),
            max_sample_rate: config.max_sample_rate(),
            channels: config.channels(),
            sample_format: config.sample_format().to_string(),
        })
        .collect();
    Ok(formats)
}

struct AudioRing {
    channels: usize,
    capacity: usize,
    samples: Vec<f32>,
    times: Vec<SampleTime>,
    oldest: u64,
    end: u64,
}

impl AudioRing {
    fn new(channels: u16, capacity: usize) -> Result<Self> {
        let count = capacity
            .checked_mul(usize::from(channels))
            .filter(|count| *count > 0 && *count <= 16_777_216)
            .ok_or_else(|| {
                CaptureError::InvalidArgument(
                    "audio history must contain 1..=16777216 samples".into(),
                )
            })?;
        Ok(Self {
            channels: usize::from(channels),
            capacity,
            samples: vec![0.0; count],
            times: vec![
                SampleTime {
                    received_ns: 0,
                    source_ns: None
                };
                capacity
            ],
            oldest: 0,
            end: 0,
        })
    }

    fn push<T: Sample>(&mut self, first: u64, data: &[T], time: SampleTime, rate: u32) -> Result<()>
    where
        f32: FromSample<T>,
    {
        if !data.len().is_multiple_of(self.channels) {
            return Err(CaptureError::InvalidData(
                "incomplete interleaved audio frame".into(),
            ));
        }
        if first != self.end {
            self.oldest = first;
            self.end = first;
        }
        for (offset, frame) in data.chunks_exact(self.channels).enumerate() {
            let slot = (self.end % self.capacity as u64) as usize;
            for (channel, sample) in frame.iter().enumerate() {
                let value = f32::from_sample(*sample);
                if !value.is_finite() {
                    return Err(CaptureError::InvalidData("non-finite audio sample".into()));
                }
                self.samples[slot * self.channels + channel] = value;
            }
            self.times[slot] = SampleTime {
                received_ns: time.received_ns,
                source_ns: time.source_ns.and_then(|start| {
                    start.checked_add((offset as u128 * 1_000_000_000 / u128::from(rate)) as u64)
                }),
            };
            self.end = self
                .end
                .checked_add(1)
                .ok_or(CaptureError::Internal("audio sequence overflow"))?;
            self.oldest = self
                .oldest
                .max(self.end.saturating_sub(self.capacity as u64));
        }
        Ok(())
    }

    fn window(&self, start: u64, frames: usize) -> Option<(Vec<f32>, SampleTime)> {
        if start < self.oldest || self.end.checked_sub(start)? < frames as u64 {
            return None;
        }
        let mut samples = Vec::with_capacity(frames * self.channels);
        for index in start..start + frames as u64 {
            let slot = (index % self.capacity as u64) as usize;
            samples
                .extend_from_slice(&self.samples[slot * self.channels..(slot + 1) * self.channels]);
        }
        Some((samples, self.times[(start % self.capacity as u64) as usize]))
    }
}

struct AudioState {
    ring: AudioRing,
    terminal: Option<CaptureError>,
}

struct AudioShared {
    id: SampleId,
    format: AudioFormat,
    state: Mutex<AudioState>,
    changed: Notify,
    dropped_callbacks: AtomicU64,
    dropped_frames: AtomicU64,
}

impl AudioShared {
    fn new(source: String, format: AudioFormat, capacity: usize) -> Result<Arc<Self>> {
        if format.sample_rate == 0 || format.channels == 0 {
            return Err(CaptureError::InvalidArgument(
                "audio sample rate and channels must be positive".into(),
            ));
        }
        let ring = AudioRing::new(format.channels, capacity)?;
        Ok(Arc::new(Self {
            id: SampleId {
                source,
                session: SessionId::next(),
                sequence: 0,
            },
            format,
            state: Mutex::new(AudioState {
                ring,
                terminal: None,
            }),
            changed: Notify::new(),
            dropped_callbacks: AtomicU64::new(0),
            dropped_frames: AtomicU64::new(0),
        }))
    }

    fn fail(&self, error: CaptureError) {
        if let Ok(mut state) = self.state.lock()
            && state.terminal.is_none()
        {
            state.terminal = Some(error);
        }
        self.changed.notify_waiters();
    }

    fn subscribe(self: &Arc<Self>) -> Result<AudioReader> {
        let state = self
            .state
            .lock()
            .map_err(|_| CaptureError::Internal("audio state poisoned"))?;
        if let Some(error) = &state.terminal {
            return Err(error.clone());
        }
        Ok(AudioReader {
            shared: self.clone(),
            cursor: state.ring.end,
            loss_generation: self.dropped_callbacks.load(Ordering::Acquire),
        })
    }
}

struct AudioIngress {
    shared: Arc<AudioShared>,
    next_frame: u64,
}

impl AudioIngress {
    fn push<T: Sample>(&mut self, data: &[T], time: SampleTime)
    where
        f32: FromSample<T>,
    {
        let frames = data.len() / usize::from(self.shared.format.channels);
        let first = self.next_frame;
        let Some(next) = first.checked_add(frames as u64) else {
            self.shared
                .fail(CaptureError::Internal("audio sequence overflow"));
            return;
        };
        self.next_frame = next;
        match self.shared.state.try_lock() {
            Ok(mut state) => {
                if state.terminal.is_none()
                    && let Err(error) =
                        state
                            .ring
                            .push(first, data, time, self.shared.format.sample_rate)
                {
                    state.terminal = Some(error);
                }
            }
            Err(TryLockError::WouldBlock) => {
                // shortcut: contention drops this callback; use an SPSC ingress if drop telemetry warrants it.
                self.shared
                    .dropped_frames
                    .fetch_add(frames as u64, Ordering::Relaxed);
                self.shared
                    .dropped_callbacks
                    .fetch_add(1, Ordering::Release);
            }
            Err(TryLockError::Poisoned(_)) => {}
        }
        self.shared.changed.notify_waiters();
    }
}

#[cfg(test)]
mod tests;

pub struct AudioSession {
    stream: Option<cpal::Stream>,
    shared: Arc<AudioShared>,
}

impl AudioSession {
    pub fn open(id: &str, config: AudioConfig, clock: CaptureClock) -> Result<Self> {
        let device = input_device(id)?;
        let selected = if let Some(request) = &config.format {
            if request.sample_rate == 0 || request.channels == 0 {
                return Err(CaptureError::InvalidArgument(
                    "audio sample rate and channels must be positive".into(),
                ));
            }
            device
                .supported_input_configs()
                .map_err(|e| CaptureError::backend("cpal", "query formats", e))?
                .find(|range| {
                    range.channels() == request.channels
                        && range.sample_format().to_string() == request.sample_format
                        && range.min_sample_rate() <= request.sample_rate
                        && request.sample_rate <= range.max_sample_rate()
                })
                .ok_or_else(|| CaptureError::UnsupportedFormat(format!("{request:?}")))?
                .with_sample_rate(request.sample_rate)
        } else {
            device
                .default_input_config()
                .map_err(|e| CaptureError::backend("cpal", "default input format", e))?
        };
        let format = AudioFormat {
            sample_rate: selected.sample_rate(),
            channels: selected.channels(),
            sample_format: selected.sample_format().to_string(),
        };
        let shared = AudioShared::new(format!("cpal:{id}"), format, config.history_frames)?;
        let native = selected.config();
        macro_rules! build {
            ($sample:ty) => {
                build_stream::<$sample>(&device, native, shared.clone(), clock)?
            };
        }
        let stream = match selected.sample_format() {
            SampleFormat::I8 => build!(i8),
            SampleFormat::I16 => build!(i16),
            SampleFormat::I24 => build!(cpal::I24),
            SampleFormat::I32 => build!(i32),
            SampleFormat::I64 => build!(i64),
            SampleFormat::U8 => build!(u8),
            SampleFormat::U16 => build!(u16),
            SampleFormat::U24 => build!(cpal::U24),
            SampleFormat::U32 => build!(u32),
            SampleFormat::U64 => build!(u64),
            SampleFormat::F32 => build!(f32),
            SampleFormat::F64 => build!(f64),
            format => return Err(CaptureError::UnsupportedFormat(format.to_string())),
        };
        stream
            .play()
            .map_err(|e| CaptureError::backend("cpal", "start", e))?;
        Ok(Self {
            stream: Some(stream),
            shared,
        })
    }

    pub fn format(&self) -> &AudioFormat {
        &self.shared.format
    }

    /// Starts at the current end; readers never compete for one consumption cursor.
    pub fn subscribe(&self) -> Result<AudioReader> {
        self.shared.subscribe()
    }

    pub fn dropped_frames(&self) -> u64 {
        self.shared.dropped_frames.load(Ordering::Relaxed)
    }

    pub fn stop(&mut self) -> Result<()> {
        drop(self.stream.take());
        let mut state = self
            .shared
            .state
            .lock()
            .map_err(|_| CaptureError::Internal("audio state poisoned"))?;
        if state.terminal.is_none() {
            state.terminal = Some(CaptureError::Stopped);
        }
        drop(state);
        self.shared.changed.notify_waiters();
        Ok(())
    }
}

impl Drop for AudioSession {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

fn build_stream<T: SizedSample>(
    device: &cpal::Device,
    config: cpal::StreamConfig,
    shared: Arc<AudioShared>,
    clock: CaptureClock,
) -> Result<cpal::Stream>
where
    f32: FromSample<T>,
{
    let errors = shared.clone();
    let mut ingress = AudioIngress {
        shared,
        next_frame: 0,
    };
    let mut source_origin = None;
    device
        .build_input_stream::<T, _, _>(
            config,
            move |data, info| {
                let capture = info.timestamp().capture;
                let origin = *source_origin.get_or_insert(capture);
                let source_ns = capture
                    .checked_duration_since(origin)
                    .and_then(|elapsed| u64::try_from(elapsed.as_nanos()).ok());
                ingress.push(
                    data,
                    SampleTime {
                        received_ns: clock.now_ns(),
                        source_ns,
                    },
                );
            },
            move |error| errors.fail(CaptureError::backend("cpal", "stream", error)),
            None,
        )
        .map_err(|e| CaptureError::backend("cpal", "build input stream", e))
}

pub struct AudioReader {
    shared: Arc<AudioShared>,
    cursor: u64,
    loss_generation: u64,
}

impl AudioReader {
    /// A gap invalidates this reader; subscribe again explicitly to resume at the current end.
    pub async fn read_window(
        &mut self,
        frames: usize,
        token: &CancellationToken,
    ) -> Result<Arc<AudioWindow>> {
        loop {
            if token.is_cancelled() {
                return Err(CaptureError::Cancelled);
            }
            let notified = self.shared.changed.notified();
            tokio::pin!(notified);
            notified.as_mut().enable();
            {
                let state = self
                    .shared
                    .state
                    .lock()
                    .map_err(|_| CaptureError::Internal("audio state poisoned"))?;
                if frames == 0 || frames > state.ring.capacity {
                    return Err(CaptureError::InvalidArgument(
                        "audio window must fit within history".into(),
                    ));
                }
                if let Some(error) = &state.terminal {
                    return Err(error.clone());
                }
                if self.cursor < state.ring.oldest
                    || self.loss_generation != self.shared.dropped_callbacks.load(Ordering::Acquire)
                {
                    return Err(CaptureError::AudioGap {
                        expected: self.cursor,
                        oldest: state.ring.oldest,
                    });
                }
                if let Some((samples, time)) = state.ring.window(self.cursor, frames) {
                    if token.is_cancelled() {
                        return Err(CaptureError::Cancelled);
                    }
                    let window = AudioWindow {
                        id: SampleId {
                            sequence: self.cursor,
                            ..self.shared.id.clone()
                        },
                        time,
                        sample_rate: self.shared.format.sample_rate,
                        channels: self.shared.format.channels,
                        samples: samples.into(),
                    };
                    self.cursor += frames as u64;
                    return Ok(Arc::new(window));
                }
            }
            tokio::select! {
                biased;
                _ = token.cancelled() => return Err(CaptureError::Cancelled),
                _ = &mut notified => {}
            }
        }
    }
}
