use std::{sync::Arc, time::Duration};

use axum::body::Bytes;
use easycon_device::Gamepad;
use easycon_sense::{
    CaptureClock, CaptureError,
    audio::{self, AudioConfig, AudioReader, AudioSession},
    native_work::NativeWorker,
    video::{self, VideoFrame, VideoReader, VideoSession},
};
use serde::{Deserialize, Serialize};
use tokio::{
    sync::{Mutex, RwLock, watch},
    task::JoinHandle,
};
use tokio_util::sync::CancellationToken;

#[derive(Clone, Copy, Debug, Deserialize, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum Kind {
    Gamepad,
    Video,
    Audio,
}

impl Kind {
    fn index(self) -> usize {
        match self {
            Self::Gamepad => 0,
            Self::Video => 1,
            Self::Audio => 2,
        }
    }
}

#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum Phase {
    #[default]
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Error,
}

#[derive(Clone, Debug, Default, Serialize)]
pub struct DeviceState {
    pub phase: Phase,
    pub id: Option<String>,
    pub detail: String,
    pub error: Option<String>,
}

#[derive(Clone, Debug, Default, Serialize)]
pub struct Snapshot {
    pub gamepad: DeviceState,
    pub video: DeviceState,
    pub audio: DeviceState,
    pub rms: f64,
    pub dbfs: Option<f64>,
}

impl Snapshot {
    fn device_mut(&mut self, kind: Kind) -> &mut DeviceState {
        match kind {
            Kind::Gamepad => &mut self.gamepad,
            Kind::Video => &mut self.video,
            Kind::Audio => &mut self.audio,
        }
    }
}

#[derive(Serialize)]
pub struct DeviceOption {
    pub id: String,
    pub name: String,
}

pub fn enumerate(kind: Kind) -> Result<Vec<DeviceOption>, String> {
    match kind {
        Kind::Gamepad => easycon_device::ports().map(|ports| {
            ports
                .into_iter()
                .map(|p| DeviceOption {
                    name: p.name.clone(),
                    id: p.name,
                })
                .collect()
        }),
        Kind::Video => {
            return video::devices()
                .map(|items| {
                    items
                        .into_iter()
                        .map(|v| DeviceOption {
                            id: v.id,
                            name: v.name,
                        })
                        .collect()
                })
                .map_err(|e| e.to_string());
        }
        Kind::Audio => {
            return audio::devices()
                .map(|items| {
                    items
                        .into_iter()
                        .map(|v| DeviceOption {
                            id: v.id,
                            name: v.name,
                        })
                        .collect()
                })
                .map_err(|e| e.to_string());
        }
    }
    .map_err(|e| e.to_string())
}

enum Resource {
    Gamepad(Box<dyn Gamepad>),
    Video(VideoSession),
    Audio(AudioSession),
}

enum Monitor {
    Video(VideoReader),
    Audio(AudioReader, usize),
}

impl Drop for Resource {
    fn drop(&mut self) {
        if let Err(error) = self.close() {
            eprintln!("native resource cleanup: {error}");
        }
    }
}

impl Resource {
    fn open(
        kind: Kind,
        id: &str,
        clock: CaptureClock,
        token: &CancellationToken,
    ) -> Result<Self, String> {
        match kind {
            Kind::Gamepad => easycon_device::connect_serial(id, token)
                .map(Self::Gamepad)
                .map_err(|e| e.to_string()),
            Kind::Video => VideoSession::open_default(id, clock)
                .map(Self::Video)
                .map_err(|e| e.to_string()),
            Kind::Audio => AudioSession::open(id, AudioConfig::default(), clock)
                .map(Self::Audio)
                .map_err(|e| e.to_string()),
        }
    }

    fn subscribe(&self) -> Result<(String, Option<Monitor>), String> {
        match self {
            Self::Gamepad(pad) => Ok((
                format!("{} · {} baud", pad.info().backend, pad.info().baud_rate),
                None,
            )),
            Self::Video(session) => {
                let f = session.format();
                Ok((
                    if f.frames_per_second == 0 {
                        format!("{} × {} · 帧率未知", f.width, f.height)
                    } else {
                        format!("{} × {} · {} fps", f.width, f.height, f.frames_per_second)
                    },
                    Some(Monitor::Video(session.subscribe())),
                ))
            }
            Self::Audio(session) => {
                let f = session.format();
                let reader = session.subscribe().map_err(|e| e.to_string())?;
                Ok((
                    format!("{} Hz · {} 声道", f.sample_rate, f.channels),
                    Some(Monitor::Audio(reader, (f.sample_rate / 20).max(1) as usize)),
                ))
            }
        }
    }

    fn close(&mut self) -> Result<(), String> {
        match self {
            Self::Gamepad(pad) => pad.disconnect().map_err(|e| e.to_string()),
            Self::Video(session) => session.stop().map_err(|e| e.to_string()),
            Self::Audio(session) => session.stop().map_err(|e| e.to_string()),
        }
    }
}

struct Connection {
    worker: NativeWorker<Resource>,
    stop: CancellationToken,
    monitor: Option<JoinHandle<()>>,
}

pub struct Hardware {
    slots: [Mutex<Option<Connection>>; 3],
    pub state: RwLock<Snapshot>,
    pub(crate) preview: watch::Sender<Option<Preview>>,
    pub stop: CancellationToken,
    clock: CaptureClock,
}

#[derive(Clone)]
pub(crate) struct Preview {
    pub id: String,
    pub jpeg: Bytes,
}

const PREVIEW_INTERVAL: Duration = Duration::from_nanos(1_000_000_000 / 24);
const PREVIEW_WIDTH: u32 = 640;
const PREVIEW_HEIGHT: u32 = 360;

impl Hardware {
    pub fn new() -> Arc<Self> {
        Arc::new(Self {
            slots: std::array::from_fn(|_| Mutex::new(None)),
            state: RwLock::new(Snapshot::default()),
            preview: watch::channel(None).0,
            stop: CancellationToken::new(),
            clock: CaptureClock::default(),
        })
    }

    pub async fn connect(self: &Arc<Self>, kind: Kind, id: String) -> Result<(), String> {
        let mut slot = self.slots[kind.index()]
            .try_lock()
            .map_err(|_| "设备正在处理另一个连接操作".to_owned())?;
        if slot.is_some() {
            return Err("请先断开当前设备".into());
        }
        if id.trim().is_empty() || id.len() > 4096 {
            return Err("请选择有效的设备".into());
        }
        if self.stop.is_cancelled() {
            return Err("服务正在关闭".into());
        }
        *self.state.write().await.device_mut(kind) = DeviceState {
            phase: Phase::Connecting,
            ..Default::default()
        };
        let result = self.open(kind, id).await;
        match result {
            Ok(connection) => {
                *slot = Some(connection);
                Ok(())
            }
            Err(error) => {
                *self.state.write().await.device_mut(kind) = DeviceState {
                    phase: Phase::Error,
                    error: Some(error.clone()),
                    ..Default::default()
                };
                Err(error)
            }
        }
    }

    async fn open(self: &Arc<Self>, kind: Kind, id: String) -> Result<Connection, String> {
        let selected = id.clone();
        let clock = self.clock.clone();
        let worker = NativeWorker::start(
            move |token| Resource::open(kind, &selected, clock, token),
            &self.stop,
        )
        .await
        .map_err(|e| e.to_string())?;
        self.attach(kind, id, worker).await
    }

    async fn attach(
        self: &Arc<Self>,
        kind: Kind,
        id: String,
        mut worker: NativeWorker<Resource>,
    ) -> Result<Connection, String> {
        let ready = worker
            .call(&self.stop, |resource, _| resource.subscribe())
            .await;
        let (detail, input) = match ready {
            Ok(ready) => ready,
            Err(error) => {
                let _ = worker.shutdown().await;
                return Err(error.to_string());
            }
        };
        *self.state.write().await.device_mut(kind) = DeviceState {
            phase: Phase::Connected,
            id: Some(id),
            detail,
            error: None,
        };
        let stop = self.stop.child_token();
        let monitor = input.map(|input| {
            let (host, token) = (self.clone(), stop.clone());
            tokio::spawn(async move {
                let result = match input {
                    Monitor::Video(reader) => host.video_monitor(reader, &token).await,
                    Monitor::Audio(reader, frames) => {
                        host.audio_monitor(reader, frames, &token).await
                    }
                };
                if !token.is_cancelled()
                    && let Err(error) = result
                {
                    let mut state = host.state.write().await;
                    let device = state.device_mut(kind);
                    device.phase = Phase::Error;
                    device.error = Some(error);
                    if matches!(kind, Kind::Audio) {
                        state.rms = 0.0;
                        state.dbfs = None;
                    }
                    if matches!(kind, Kind::Video) {
                        host.preview.send_replace(None);
                    }
                }
            })
        });
        Ok(Connection {
            worker,
            stop,
            monitor,
        })
    }

    pub async fn disconnect(&self, kind: Kind) -> Result<(), String> {
        let mut slot = self.slots[kind.index()]
            .try_lock()
            .map_err(|_| "设备正在处理另一个连接操作".to_owned())?;
        self.close_slot(kind, &mut slot).await
    }

    async fn close_slot(&self, kind: Kind, slot: &mut Option<Connection>) -> Result<(), String> {
        let mut error = None;
        if let Some(mut connection) = slot.take() {
            self.state.write().await.device_mut(kind).phase = Phase::Disconnecting;
            connection.stop.cancel();
            if let Some(monitor) = connection.monitor.take()
                && let Err(e) = monitor.await
            {
                error = Some(e.to_string());
            }
            if !self.stop.is_cancelled()
                && let Err(e) = connection
                    .worker
                    .call(&CancellationToken::new(), |r, _| r.close())
                    .await
            {
                error = Some(e.to_string());
            }
            if let Err(e) = connection.worker.shutdown().await {
                error = Some(e.to_string());
            }
        }
        if matches!(kind, Kind::Video) {
            self.preview.send_replace(None);
        }
        let mut state = self.state.write().await;
        if matches!(kind, Kind::Audio) {
            state.rms = 0.0;
            state.dbfs = None;
        }
        *state.device_mut(kind) = DeviceState {
            phase: if error.is_some() {
                Phase::Error
            } else {
                Phase::Disconnected
            },
            error: error.clone(),
            ..Default::default()
        };
        error.map_or(Ok(()), Err)
    }

    pub async fn shutdown(&self) {
        self.stop.cancel();
        for kind in [Kind::Gamepad, Kind::Video, Kind::Audio] {
            let mut slot = self.slots[kind.index()].lock().await;
            if let Err(error) = self.close_slot(kind, &mut slot).await {
                eprintln!("{kind:?} cleanup: {error}");
            }
        }
    }

    async fn video_monitor(
        &self,
        mut reader: VideoReader,
        token: &CancellationToken,
    ) -> Result<(), String> {
        let mut previous = None;
        let mut interval = tokio::time::interval(PREVIEW_INTERVAL);
        interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        loop {
            tokio::select! {
                biased;
                _ = token.cancelled() => return Ok(()),
                _ = interval.tick() => {}
            }
            let frame = reader
                .capture(previous.as_ref(), token)
                .await
                .map_err(|e| e.to_string())?;
            previous = Some(frame.id.clone());
            // Keep checking source health; all viewers share one encode of the latest frame.
            if self.preview.receiver_count() == 0 {
                continue;
            }
            let id = format!("{}:{}", frame.id.session.0, frame.id.sequence);
            let jpeg = tokio::task::spawn_blocking(move || encode_preview(&frame))
                .await
                .map_err(|e| e.to_string())??;
            if token.is_cancelled() {
                return Ok(());
            }
            self.preview.send_replace(Some(Preview {
                id,
                jpeg: jpeg.into(),
            }));
        }
    }

    async fn audio_monitor(
        &self,
        mut reader: AudioReader,
        frames: usize,
        token: &CancellationToken,
    ) -> Result<(), String> {
        loop {
            let window = reader
                .read_window(frames, token)
                .await
                .map_err(|e| e.to_string())?;
            let rms = window.rms_cancellable(token).map_err(|e| e.to_string())?;
            let mut state = self.state.write().await;
            state.rms = rms;
            state.dbfs = dbfs(rms);
        }
    }
}

pub fn dbfs(rms: f64) -> Option<f64> {
    (rms.is_finite() && rms > 0.0).then(|| 20.0 * rms.log10())
}

fn encode_preview(frame: &VideoFrame) -> Result<Vec<u8>, String> {
    let image = image::ImageBuffer::<image::Rgb<u8>, _>::from_raw(
        frame.format.width,
        frame.format.height,
        frame.rgb.as_ref(),
    )
    .filter(|_| frame.format.width > 0 && frame.format.height > 0)
    .ok_or_else(|| CaptureError::InvalidData("invalid RGB8 preview".into()).to_string())?;
    let scale = (f64::from(PREVIEW_WIDTH) / f64::from(image.width()))
        .min(f64::from(PREVIEW_HEIGHT) / f64::from(image.height()))
        .min(1.0);
    let width = (f64::from(image.width()) * scale).round().max(1.0) as u32;
    let height = (f64::from(image.height()) * scale).round().max(1.0) as u32;
    let mut bytes = Vec::new();
    let mut encoder = image::codecs::jpeg::JpegEncoder::new_with_quality(&mut bytes, 75);
    if (width, height) == image.dimensions() {
        encoder.encode_image(&image)
    } else {
        encoder.encode_image(&image::imageops::thumbnail(&image, width, height))
    }
    .map_err(|e| e.to_string())?;
    Ok(bytes)
}

#[cfg(test)]
mod tests {
    use super::*;
    use easycon_sense::{SampleId, SampleTime, SessionId};
    use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};

    struct TestVideo {
        fail: Arc<AtomicBool>,
        stopped: Arc<AtomicUsize>,
    }

    impl video::VideoSource for TestVideo {
        fn format(&self) -> video::VideoFormat {
            video::VideoFormat {
                width: 32,
                height: 18,
                frames_per_second: 30,
                encoding: "RGB".into(),
            }
        }
        fn next_frame(&mut self) -> easycon_sense::Result<video::VideoBuffer> {
            std::thread::sleep(Duration::from_millis(5));
            if self.fail.load(Ordering::Acquire) {
                return Err(CaptureError::InvalidData("test source unplugged".into()));
            }
            Ok(video::VideoBuffer {
                rgb: vec![120; 32 * 18 * 3],
                source_ns: None,
            })
        }
        fn stop(&mut self) -> easycon_sense::Result<()> {
            self.stopped.fetch_add(1, Ordering::AcqRel);
            Ok(())
        }
    }

    async fn live_test_video() -> (Arc<Hardware>, Arc<AtomicBool>, Arc<AtomicUsize>) {
        let host = Hardware::new();
        let fail = Arc::new(AtomicBool::new(false));
        let stopped = Arc::new(AtomicUsize::new(0));
        let source = TestVideo {
            fail: fail.clone(),
            stopped: stopped.clone(),
        };
        let worker = NativeWorker::start(
            move |_| {
                VideoSession::from_source("test".into(), CaptureClock::default(), move || {
                    Ok(Box::new(source))
                })
                .map(Resource::Video)
                .map_err(|e| e.to_string())
            },
            &host.stop,
        )
        .await
        .unwrap();
        *host.slots[Kind::Video.index()].lock().await = Some(
            host.attach(Kind::Video, "test".into(), worker)
                .await
                .unwrap(),
        );
        let preview = host.preview.subscribe();
        tokio::time::timeout(Duration::from_secs(3), async {
            while preview.borrow().is_none() {
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .unwrap();
        (host, fail, stopped)
    }

    #[tokio::test]
    async fn disconnect_cancels_preview_and_joins_source_exactly_once() {
        let (host, _, stopped) = live_test_video().await;
        host.disconnect(Kind::Video).await.unwrap();
        assert_eq!(stopped.load(Ordering::Acquire), 1);
        assert!(host.preview.borrow().is_none());
        assert!(host.slots[1].lock().await.is_none());
        assert!(matches!(
            host.state.read().await.video.phase,
            Phase::Disconnected
        ));
        host.shutdown().await;
        assert_eq!(stopped.load(Ordering::Acquire), 1);
    }

    #[tokio::test]
    async fn no_viewers_stops_encoding_but_capture_health_is_still_monitored() {
        let (host, fail, stopped) = live_test_video().await;
        // An encode already in flight may finish after the last viewer leaves.
        tokio::time::sleep(PREVIEW_INTERVAL * 3).await;
        let id = host.preview.borrow().as_ref().unwrap().id.clone();
        tokio::time::sleep(PREVIEW_INTERVAL * 3).await;
        assert_eq!(host.preview.borrow().as_ref().unwrap().id, id);
        let mut viewer = host.preview.subscribe();
        viewer.borrow_and_update();
        tokio::time::timeout(Duration::from_secs(1), viewer.changed())
            .await
            .unwrap()
            .unwrap();
        assert_ne!(viewer.borrow().as_ref().unwrap().id, id);
        drop(viewer);
        fail.store(true, Ordering::Release);
        tokio::time::timeout(Duration::from_secs(1), async {
            while !matches!(host.state.read().await.video.phase, Phase::Error) {
                tokio::time::sleep(Duration::from_millis(5)).await;
            }
        })
        .await
        .unwrap();
        host.shutdown().await;
        assert_eq!(stopped.load(Ordering::Acquire), 1);
    }

    #[tokio::test]
    async fn source_failure_clears_stale_preview_and_still_releases_native_state() {
        let (host, fail, stopped) = live_test_video().await;
        fail.store(true, Ordering::Release);
        tokio::time::timeout(Duration::from_secs(3), async {
            while !matches!(host.state.read().await.video.phase, Phase::Error) {
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .unwrap();
        assert!(host.preview.borrow().is_none());
        assert!(host.disconnect(Kind::Video).await.is_err());
        assert_eq!(stopped.load(Ordering::Acquire), 1);
        assert!(host.slots[1].lock().await.is_none());
        assert!(host.state.read().await.video.id.is_none());
        host.shutdown().await;
    }

    #[test]
    fn silence_and_known_levels() {
        assert_eq!(dbfs(0.0), None);
        assert_eq!(dbfs(f64::NAN), None);
        assert_eq!(dbfs(1.0), Some(0.0));
        assert!((dbfs(0.1).unwrap() + 20.0).abs() < 0.0001);
    }

    #[test]
    fn preview_is_real_jpeg_with_bounded_dimensions() {
        let frame = VideoFrame {
            id: SampleId {
                source: "test".into(),
                session: SessionId(1),
                sequence: 1,
            },
            time: SampleTime {
                received_ns: 0,
                source_ns: None,
            },
            format: video::VideoFormat {
                width: 1920,
                height: 1080,
                frames_per_second: 30,
                encoding: "RGB".into(),
            },
            rgb: vec![120; 1920 * 1080 * 3].into(),
        };
        let bytes = encode_preview(&frame).unwrap();
        let decoded = image::load_from_memory(&bytes).unwrap();
        assert_eq!((decoded.width(), decoded.height()), (640, 360));
        for (width, height, expected) in [
            (18, 32, (18, 32)),
            (1080, 1920, (203, 360)),
            (1000, 1000, (360, 360)),
        ] {
            let frame = VideoFrame {
                format: video::VideoFormat {
                    width,
                    height,
                    ..frame.format.clone()
                },
                rgb: vec![120; width as usize * height as usize * 3].into(),
                id: frame.id.clone(),
                time: frame.time,
            };
            let decoded = image::load_from_memory(&encode_preview(&frame).unwrap()).unwrap();
            assert_eq!((decoded.width(), decoded.height()), expected);
        }
    }

    #[tokio::test]
    async fn independent_slots_disconnect_and_reject_empty_ids() {
        let host = Hardware::new();
        let _guard = host.slots[0].lock().await;
        assert!(host.connect(Kind::Gamepad, "x".into()).await.is_err());
        host.disconnect(Kind::Audio).await.unwrap();
        assert!(host.connect(Kind::Audio, " ".into()).await.is_err());
        assert!(matches!(
            host.state.read().await.audio.phase,
            Phase::Disconnected
        ));
    }
}
