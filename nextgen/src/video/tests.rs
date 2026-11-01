use super::*;
use std::{
    sync::atomic::{AtomicUsize, Ordering},
    time::Duration,
};

enum Command {
    Frame(Vec<u8>),
    Fail,
    Panic,
}

struct FakeSource {
    commands: mpsc::Receiver<Command>,
    stopped: Arc<AtomicUsize>,
    cleanup_error: bool,
    format: VideoFormat,
}

impl VideoSource for FakeSource {
    fn format(&self) -> VideoFormat {
        self.format.clone()
    }
    fn next_frame(&mut self) -> Result<VideoBuffer> {
        match self.commands.recv_timeout(Duration::from_secs(2)) {
            Ok(Command::Frame(rgb)) => Ok(VideoBuffer {
                rgb,
                source_ns: Some(42),
            }),
            Ok(Command::Panic) => panic!("synthetic source panic"),
            _ => Err(CaptureError::InvalidData("synthetic disconnect".into())),
        }
    }
    fn stop(&mut self) -> Result<()> {
        self.stopped.fetch_add(1, Ordering::SeqCst);
        if self.cleanup_error {
            Err(CaptureError::Internal("synthetic cleanup failure"))
        } else {
            Ok(())
        }
    }
}

fn format() -> VideoFormat {
    VideoFormat {
        width: 1,
        height: 1,
        frames_per_second: 30,
        encoding: "RAWRGB".into(),
    }
}

struct Fixture {
    session: VideoSession,
    commands: mpsc::Sender<Command>,
    stopped: Arc<AtomicUsize>,
}

impl Fixture {
    fn new(cleanup_error: bool) -> Self {
        let (commands, receiver) = mpsc::channel();
        let stopped = Arc::new(AtomicUsize::new(0));
        let source = FakeSource {
            commands: receiver,
            stopped: stopped.clone(),
            cleanup_error,
            format: format(),
        };
        let session =
            VideoSession::from_source("fake-video".into(), CaptureClock::default(), move || {
                Ok(Box::new(source))
            })
            .unwrap();
        Self {
            session,
            commands,
            stopped,
        }
    }
    fn frame(&self, value: u8) {
        self.commands.send(Command::Frame(vec![value; 3])).unwrap();
    }
    fn stop(&mut self) -> Result<()> {
        self.session.stop.cancel();
        let _ = self.commands.send(Command::Frame(vec![0; 3]));
        self.session.stop()
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

async fn capture(reader: &mut VideoReader, after: Option<&SampleId>) -> Result<Arc<VideoFrame>> {
    tokio::time::timeout(
        Duration::from_secs(1),
        reader.capture(after, &CancellationToken::new()),
    )
    .await
    .expect("video reader did not wake")
}

async fn terminal(reader: &mut VideoReader) {
    tokio::time::timeout(Duration::from_secs(1), async {
        loop {
            if !matches!(*reader.receiver.borrow_and_update(), VideoState::Running(_)) {
                return;
            }
            reader.receiver.changed().await.unwrap();
        }
    })
    .await
    .expect("worker did not finish");
}

#[tokio::test]
async fn frames_are_immutable_and_readers_receive_latest_snapshot() {
    let fixture = Fixture::new(false);
    let mut reader = fixture.session.subscribe();
    fixture.frame(1);
    let first = capture(&mut reader, None).await.unwrap();
    fixture.frame(2);
    let second = capture(&mut reader, Some(&first.id)).await.unwrap();
    fixture.frame(3);
    let third = capture(&mut reader, Some(&second.id)).await.unwrap();
    let mut late = fixture.session.subscribe();
    let latest = capture(&mut late, None).await.unwrap();
    assert!(Arc::ptr_eq(&third, &latest));
    assert_eq!(&*first.rgb, &[1; 3]);
    assert_eq!(third.id.sequence, 3);
    assert_eq!(first.id.session, third.id.session);
    assert_eq!(third.time.source_ns, Some(42));
    assert!(third.time.received_ns >= first.time.received_ns);
}

#[tokio::test]
async fn fresh_capture_waits_and_cancellation_does_not_stop_source() {
    let fixture = Fixture::new(false);
    let mut reader = fixture.session.subscribe();
    fixture.frame(1);
    let first = capture(&mut reader, None).await.unwrap();
    let token = CancellationToken::new();
    let (result, ()) = tokio::join!(reader.capture(Some(&first.id), &token), async {
        tokio::task::yield_now().await;
        token.cancel();
    });
    assert_eq!(result.unwrap_err(), CaptureError::Cancelled);
    let mut other = fixture.session.subscribe();
    fixture.frame(2);
    assert_eq!(
        capture(&mut other, Some(&first.id))
            .await
            .unwrap()
            .id
            .sequence,
        2
    );
    assert_eq!(fixture.stopped.load(Ordering::SeqCst), 0);
}

#[tokio::test]
async fn different_source_or_session_cursor_is_rejected() {
    let fixture = Fixture::new(false);
    let mut reader = fixture.session.subscribe();
    let mut id = fixture.session.id.clone();
    id.session = SessionId::next();
    assert_eq!(
        capture(&mut reader, Some(&id)).await.unwrap_err(),
        CaptureError::SourceChanged
    );
    id = fixture.session.id.clone();
    id.source = "another-source".into();
    assert_eq!(
        capture(&mut reader, Some(&id)).await.unwrap_err(),
        CaptureError::SourceChanged
    );
}

#[tokio::test]
async fn failure_wakes_waiters_and_never_returns_stale_frame() {
    let mut fixture = Fixture::new(false);
    let mut reader = fixture.session.subscribe();
    fixture.frame(1);
    let frame = capture(&mut reader, None).await.unwrap();
    fixture.commands.send(Command::Fail).unwrap();
    assert!(matches!(
        capture(&mut reader, Some(&frame.id)).await,
        Err(CaptureError::InvalidData(_))
    ));
    assert!(matches!(
        capture(&mut reader, None).await,
        Err(CaptureError::InvalidData(_))
    ));
    assert!(fixture.stop().is_err());
    assert_eq!(fixture.stopped.load(Ordering::SeqCst), 1);
}

#[tokio::test]
async fn stop_joins_cleanup_once_and_invalidates_readers() {
    let mut fixture = Fixture::new(false);
    let mut reader = fixture.session.subscribe();
    fixture.stop().unwrap();
    fixture.stop().unwrap();
    assert_eq!(fixture.stopped.load(Ordering::SeqCst), 1);
    assert_eq!(
        capture(&mut reader, None).await.unwrap_err(),
        CaptureError::Stopped
    );
}

#[tokio::test]
async fn malformed_frame_and_read_panic_both_clean_up() {
    for command in [Command::Frame(vec![0; 2]), Command::Panic] {
        let mut fixture = Fixture::new(false);
        let mut reader = fixture.session.subscribe();
        fixture.commands.send(command).unwrap();
        terminal(&mut reader).await;
        let error = capture(&mut reader, None).await.unwrap_err();
        assert!(matches!(
            error,
            CaptureError::InvalidData(_) | CaptureError::WorkerPanicked
        ));
        assert!(fixture.stop().is_err());
        assert_eq!(fixture.stopped.load(Ordering::SeqCst), 1);
    }
}

#[tokio::test]
async fn cleanup_failure_preserves_capture_failure() {
    let mut fixture = Fixture::new(true);
    let mut reader = fixture.session.subscribe();
    fixture.commands.send(Command::Fail).unwrap();
    terminal(&mut reader).await;
    let error = fixture.stop().unwrap_err();
    assert!(
        matches!(error, CaptureError::CleanupFailed { previous: Some(ref previous), .. }
        if matches!(**previous, CaptureError::InvalidData(_)))
    );
    assert_eq!(capture(&mut reader, None).await.unwrap_err(), error);
}

#[test]
fn initialization_failure_returns_error_and_invalid_format_releases_source() {
    assert!(matches!(
        VideoSession::from_source("fake".into(), CaptureClock::default(), || Err(
            CaptureError::UnsupportedFormat("synthetic".into())
        )),
        Err(CaptureError::UnsupportedFormat(_))
    ));
    let (_, receiver) = mpsc::channel();
    let stopped = Arc::new(AtomicUsize::new(0));
    let source = FakeSource {
        commands: receiver,
        stopped: stopped.clone(),
        cleanup_error: false,
        format: VideoFormat {
            width: 0,
            ..format()
        },
    };
    assert!(matches!(
        VideoSession::from_source("fake".into(), CaptureClock::default(), move || Ok(
            Box::new(source)
        )),
        Err(CaptureError::InvalidData(_))
    ));
    assert_eq!(stopped.load(Ordering::SeqCst), 1);
}

#[test]
fn ccap_device_ids_and_default_format_constraints_are_checked() {
    for id in ["index:0", "index:2", "id:OBS Virtual Camera", "id:device:7"] {
        validate_device_id(id).unwrap();
    }
    for id in [
        "index:-1",
        "index:4294967296",
        "id:",
        "id: ",
        "id:bad\0name",
        "2",
    ] {
        assert!(validate_device_id(id).is_err(), "{id:?}");
    }
    let actual = VideoFormat {
        width: 1920,
        height: 1080,
        frames_per_second: 0,
        encoding: "BGRA32".into(),
    };
    ccap::verify_format(&actual, &actual).unwrap();
    for requested in [
        VideoFormat {
            width: 640,
            ..actual.clone()
        },
        VideoFormat {
            encoding: "RGB24".into(),
            ..actual.clone()
        },
        VideoFormat {
            frames_per_second: 60,
            ..actual.clone()
        },
    ] {
        assert!(ccap::verify_format(&actual, &requested).is_err());
    }
    for fps in [0.0, f64::NAN, f64::INFINITY, -1.0, 59.94] {
        assert_eq!(ccap::reported_fps(fps), 0);
    }
    assert_eq!(ccap::reported_fps(60.0), 60);
}

fn packed_frame(
    data: &[u8],
    pixel_format: ::ccap::PixelFormat,
    stride: u32,
) -> ::ccap::VideoFrameInfo<'_> {
    ::ccap::VideoFrameInfo {
        width: 1,
        height: 2,
        pixel_format,
        size_in_bytes: data.len() as u32,
        timestamp: 100,
        frame_index: 0,
        orientation: ::ccap::FrameOrientation::BottomToTop,
        data_planes: [Some(data), None, None],
        strides: [stride, 0, 0],
    }
}

#[test]
fn ccap_rgb_conversion_handles_channels_stride_orientation_and_trailing_storage() {
    use ::ccap::PixelFormat;
    for (format, data) in [
        (
            PixelFormat::Bgra32,
            vec![3, 2, 1, 255, 99, 99, 99, 99, 6, 5, 4, 255, 77, 77],
        ),
        (
            PixelFormat::Rgba32,
            vec![1, 2, 3, 255, 99, 99, 99, 99, 4, 5, 6, 255, 77, 77],
        ),
        (
            PixelFormat::Bgr24,
            vec![3, 2, 1, 99, 99, 99, 99, 99, 6, 5, 4, 77, 77, 77],
        ),
        (
            PixelFormat::Rgb24,
            vec![1, 2, 3, 99, 99, 99, 99, 99, 4, 5, 6, 77, 77, 77],
        ),
    ] {
        let info = packed_frame(&data, format, 8);
        assert_eq!(ccap::packed_rgb(&info).unwrap(), [4, 5, 6, 1, 2, 3]);
        let info = ::ccap::VideoFrameInfo {
            orientation: ::ccap::FrameOrientation::TopToBottom,
            ..info
        };
        assert_eq!(ccap::packed_rgb(&info).unwrap(), [1, 2, 3, 4, 5, 6]);
    }
}

#[test]
fn ccap_rejects_truncated_planes_invalid_stride_and_unsupported_formats() {
    use ::ccap::PixelFormat;
    for info in [
        packed_frame(&[1, 2, 3], PixelFormat::Bgra32, 4),
        packed_frame(&[0; 16], PixelFormat::Bgra32, 3),
        packed_frame(&[0; 16], PixelFormat::Nv12, 8),
        ::ccap::VideoFrameInfo {
            width: 0,
            ..packed_frame(&[0; 16], PixelFormat::Bgra32, 8)
        },
        ::ccap::VideoFrameInfo {
            data_planes: [None, None, None],
            ..packed_frame(&[], PixelFormat::Bgra32, 8)
        },
    ] {
        assert!(ccap::packed_rgb(&info).is_err());
    }
}

#[test]
fn ccap_decoder_preserves_relative_time_and_rejects_regression_and_format_changes() {
    let mut info = packed_frame(
        &[3, 2, 1, 255, 6, 5, 4, 255],
        ::ccap::PixelFormat::Bgra32,
        4,
    );
    let format = VideoFormat {
        width: 1,
        height: 2,
        frames_per_second: 0,
        encoding: "BGRA32".into(),
    };
    let mut origin = None;
    let mut previous = None;
    info.timestamp = 0;
    let first = ccap::decode_frame(&info, &format, &mut origin, &mut previous).unwrap();
    assert_eq!(first.source_ns, None);
    assert_eq!(first.rgb, [4, 5, 6, 1, 2, 3]);
    info.frame_index = 1;
    info.timestamp = 10_000;
    assert_eq!(
        ccap::decode_frame(&info, &format, &mut origin, &mut previous)
            .unwrap()
            .source_ns,
        Some(0)
    );
    info.frame_index = 3;
    info.timestamp = 20_000;
    assert_eq!(
        ccap::decode_frame(&info, &format, &mut origin, &mut previous)
            .unwrap()
            .source_ns,
        Some(10_000)
    );
    assert!(ccap::decode_frame(&info, &format, &mut origin, &mut previous).is_err());
    info.frame_index = 4;
    info.timestamp = 15_000;
    assert!(ccap::decode_frame(&info, &format, &mut origin, &mut previous).is_err());
    info.timestamp = 30_000;
    info.width = 2;
    assert!(ccap::decode_frame(&info, &format, &mut origin, &mut previous).is_err());
    info.width = 1;
    info.pixel_format = ::ccap::PixelFormat::Rgba32;
    assert!(ccap::decode_frame(&info, &format, &mut origin, &mut previous).is_err());
    assert_eq!(previous, Some((3, 20_000)));
}
