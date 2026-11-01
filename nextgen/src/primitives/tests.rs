use std::{
    collections::BTreeSet,
    sync::Mutex,
    time::{Duration, Instant},
};

use super::*;
use crate::{
    CaptureError, SampleTime, SessionId,
    meta::{LogRecord, LogSink, MetaError},
    video::VideoFormat,
    vision::{self, OcrBackendKind, TemplateBackendKind, VisionError},
};
use easycon_device::{Delivery, DeviceError, SessionInfo, WriteReceipt};

async fn invoke(
    host: &mut PrimitiveHost<'_>,
    call: PrimitiveCall<'_>,
    token: &CancellationToken,
) -> Result<PrimitiveOutput> {
    let contract = call.id().contract();
    let result = host.execute(call, token).await;
    match &result {
        Ok(output) => assert_eq!(
            output.output_type(),
            contract.output,
            "{} output",
            contract.name
        ),
        Err(error) => assert!(
            contract.errors.contains(&error.kind()),
            "{} undeclared error: {error:?}",
            contract.name
        ),
    }
    result
}

fn frame() -> Arc<VideoFrame> {
    Arc::new(VideoFrame {
        id: SampleId {
            source: "camera".into(),
            session: SessionId(7),
            sequence: 3,
        },
        time: SampleTime {
            received_ns: 123,
            source_ns: Some(99),
        },
        format: VideoFormat {
            width: 1,
            height: 1,
            frames_per_second: 30,
            encoding: "RGB3".into(),
        },
        rgb: vec![10, 20, 30].into(),
    })
}
fn window() -> Arc<AudioWindow> {
    Arc::new(AudioWindow {
        id: SampleId {
            source: "audio".into(),
            session: SessionId(8),
            sequence: 2,
        },
        time: SampleTime {
            received_ns: 124,
            source_ns: Some(100),
        },
        sample_rate: 48_000,
        channels: 2,
        samples: vec![0.25, -0.25, 0.75, -0.75].into(),
    })
}

struct VideoMock {
    frame: Arc<VideoFrame>,
    after: Vec<Option<SampleId>>,
}
impl VideoInput for VideoMock {
    fn capture<'a>(
        &'a mut self,
        after: Option<&'a SampleId>,
        _: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<VideoFrame>> {
        self.after.push(after.cloned());
        let frame = self.frame.clone();
        Box::pin(async move { Ok(frame) })
    }
}
struct AudioMock {
    window: Arc<AudioWindow>,
    frames: Vec<usize>,
}
impl AudioInput for AudioMock {
    fn read_window<'a>(
        &'a mut self,
        frames: usize,
        _: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<AudioWindow>> {
        self.frames.push(frames);
        let window = self.window.clone();
        Box::pin(async move { Ok(window) })
    }
}

#[test]
fn catalogue_is_complete_unique_versioned_and_has_explicit_effects() {
    let mut names = BTreeSet::new();
    for id in PrimitiveId::ALL {
        let contract = id.contract();
        assert_eq!(contract.id, id);
        assert!(names.insert(contract.name));
        assert_eq!(PrimitiveId::lookup(contract.name, 1), Some(id));
        assert_eq!(PrimitiveId::lookup(contract.name, 0), None);
        assert_eq!(PrimitiveId::lookup(contract.name, 2), None);
        assert!(contract.errors.contains(&ErrorKind::Cancelled));
    }
    assert_eq!(names.len(), 18);
    assert_eq!(PrimitiveId::lookup("ocr", 1), Some(PrimitiveId::Ocr));
    assert_eq!(PrimitiveId::lookup("tesseract_ocr", 1), None);
    assert_eq!(PrimitiveId::lookup("ppocr_ocr", 1), None);
    assert_eq!(PrimitiveId::lookup("unknown", 1), None);
    assert_eq!(PrimitiveId::lookup("Capture", 1), None);
    for id in [
        PrimitiveId::RegionStatistics,
        PrimitiveId::ImageDifference,
        PrimitiveId::MatchTemplate,
        PrimitiveId::Ocr,
        PrimitiveId::AudioRms,
    ] {
        assert_eq!(id.contract().kind, Kind::Pure);
        let scheduling = if matches!(id, PrimitiveId::MatchTemplate | PrimitiveId::Ocr) {
            Scheduling::Provider
        } else {
            Scheduling::CallerThread
        };
        assert_eq!(id.contract().scheduling, scheduling);
    }
    for id in [PrimitiveId::Sleep, PrimitiveId::ClockNow, PrimitiveId::Log] {
        assert_eq!(id.contract().kind, Kind::Meta);
    }
    assert_eq!(PrimitiveId::Capture.contract().kind, Kind::Effectful);
    assert_eq!(
        PrimitiveId::TypeText.contract().capability,
        Some(Capability::TextInput)
    );
}

#[tokio::test]
async fn capture_audio_and_rms_keep_explicit_samples_and_forward_cursors_once() {
    let frame = frame();
    let after = SampleId {
        sequence: frame.id.sequence - 1,
        ..frame.id.clone()
    };
    let window = window();
    let mut video = VideoMock {
        frame: frame.clone(),
        after: vec![],
    };
    let mut audio = AudioMock {
        window: window.clone(),
        frames: vec![],
    };
    let token = CancellationToken::new();
    {
        let mut host = PrimitiveHost::default();
        host.videos.insert("video".into(), &mut video);
        host.audio.insert("sound".into(), &mut audio);
        let PrimitiveOutput::Frame(actual) = invoke(
            &mut host,
            PrimitiveCall::Capture {
                source: "video",
                after: Some(&after),
            },
            &token,
        )
        .await
        .unwrap() else {
            panic!()
        };
        assert!(Arc::ptr_eq(&actual, &frame));
        let PrimitiveOutput::AudioWindow(actual) = invoke(
            &mut host,
            PrimitiveCall::ReadAudio {
                source: "sound",
                frames: 2,
            },
            &token,
        )
        .await
        .unwrap() else {
            panic!()
        };
        assert!(Arc::ptr_eq(&actual, &window));
        let PrimitiveOutput::Float(rms) = invoke(
            &mut host,
            PrimitiveCall::AudioRms { window: &actual },
            &token,
        )
        .await
        .unwrap() else {
            panic!()
        };
        assert_eq!(rms, (0.3125f64).sqrt());
        for frames in [0, -1, i64::MIN] {
            assert_eq!(
                invoke(
                    &mut host,
                    PrimitiveCall::ReadAudio {
                        source: "sound",
                        frames
                    },
                    &token
                )
                .await
                .unwrap_err()
                .kind(),
                ErrorKind::InvalidArgument
            );
        }
    }
    assert_eq!(video.after, [Some(after)]);
    assert_eq!(audio.frames, [2]);
}

struct MatcherMock(usize);
impl TemplateMatcher for MatcherMock {
    fn kind(&self) -> TemplateBackendKind {
        TemplateBackendKind::OpenCv5
    }
    fn match_template(
        &mut self,
        image: &Image,
        _: &Image,
        config: MatchConfig,
        _: &CancellationToken,
    ) -> vision::Result<TemplateMatch> {
        self.0 += 1;
        Ok(TemplateMatch {
            source: image.source().cloned(),
            search: image.bounds(),
            bounds: image.bounds(),
            mode: config.mode,
            raw_score: 0.25,
            confidence: Some(0.75),
            implementation: "mock-fixed/v1".into(),
        })
    }
}
struct OcrMock(usize);
impl OcrBackend for OcrMock {
    fn kind(&self) -> OcrBackendKind {
        OcrBackendKind::Tesseract
    }
    fn recognize(
        &mut self,
        image: &Image,
        _: Option<Rect>,
        _: &CancellationToken,
    ) -> vision::Result<OcrResult> {
        self.0 += 1;
        Ok(OcrResult {
            source: image.source().cloned(),
            region: image.bounds(),
            kind: self.kind(),
            text: String::new(),
            boxes: vec![],
            profile: "mock-fixed/v1".into(),
        })
    }
}

#[tokio::test]
async fn recognition_and_measurements_preserve_structured_results_without_recapture() {
    let frame = frame();
    let image = Image::from_frame(&frame).unwrap();
    let mut matcher = MatcherMock(0);
    let mut ocr = OcrMock(0);
    let mut host = PrimitiveHost::default();
    host.matchers.insert("fixed-template".into(), &mut matcher);
    host.ocr.insert("fixed-ocr".into(), &mut ocr);
    let token = CancellationToken::new();
    let PrimitiveOutput::Match(result) = invoke(
        &mut host,
        PrimitiveCall::MatchTemplate {
            matcher: "fixed-template",
            image: &image,
            template: &image,
            config: MatchConfig::default(),
        },
        &token,
    )
    .await
    .unwrap() else {
        panic!()
    };
    assert_eq!(result.source.as_ref(), image.source());
    assert_eq!(result.confidence, Some(0.75));
    assert_eq!(result.raw_score, 0.25);
    let PrimitiveOutput::Ocr(result) = invoke(
        &mut host,
        PrimitiveCall::Ocr {
            kind: OcrBackendKind::Tesseract,
            engine: "fixed-ocr",
            image: ImageInput::Image(&image),
            region: None,
        },
        &token,
    )
    .await
    .unwrap() else {
        panic!()
    };
    assert_eq!(result.source.as_ref(), image.source());
    assert!(result.text.is_empty());
    let PrimitiveOutput::RegionStatistics(result) = invoke(
        &mut host,
        PrimitiveCall::RegionStatistics {
            image: &image,
            region: None,
        },
        &token,
    )
    .await
    .unwrap() else {
        panic!()
    };
    assert_eq!(result.rgb_mean, [10.0, 20.0, 30.0]);
    let PrimitiveOutput::ImageDifference(result) = invoke(
        &mut host,
        PrimitiveCall::ImageDifference {
            left: &image,
            right: &image,
            left_region: None,
            right_region: None,
        },
        &token,
    )
    .await
    .unwrap() else {
        panic!()
    };
    assert_eq!(result.changed_pixels, 0);
    assert_eq!(result.left_source, result.right_source);
    assert_eq!(matcher.0, 1);
    assert_eq!(ocr.0, 1);
}

struct PadMock {
    info: SessionInfo,
    report: Report,
    reports: Vec<Report>,
    disconnected: bool,
    cancel_after_commit: bool,
    written_at: Instant,
}
impl PadMock {
    fn new() -> Self {
        Self {
            info: SessionInfo {
                port: "fake".into(),
                backend: "fake",
                baud_rate: 115200,
                session: 9,
            },
            report: Report::default(),
            reports: vec![],
            disconnected: false,
            cancel_after_commit: false,
            written_at: Instant::now(),
        }
    }
}
impl Gamepad for PadMock {
    fn info(&self) -> &SessionInfo {
        &self.info
    }
    fn is_connected(&self) -> bool {
        !self.disconnected
    }
    fn state(&self) -> Option<Report> {
        Some(self.report)
    }
    fn apply_report(
        &mut self,
        report: Report,
        token: &CancellationToken,
    ) -> easycon_device::Result<WriteReceipt> {
        self.report = report;
        self.reports.push(report);
        if self.cancel_after_commit {
            token.cancel();
        }
        Ok(WriteReceipt {
            session: self.info.session,
            sequence: self.reports.len() as u64,
            delivery: Delivery::Accepted,
            written_at: self.written_at,
        })
    }
    fn disconnect(&mut self) -> easycon_device::Result<()> {
        self.disconnected = true;
        Ok(())
    }
}

#[tokio::test]
async fn all_pad_actions_deliver_once_preserve_state_receipts_and_external_ownership() {
    let mut pad = PadMock::new();
    let initial = Report {
        buttons: Button::B.mask(),
        hat: Hat::Down,
        left: Stick { x: 12, y: 34 },
        right: Stick { x: 56, y: 78 },
    };
    let written_at = pad.written_at;
    {
        let mut host = PrimitiveHost::default();
        host.pads.insert("switch".into(), &mut pad);
        let token = CancellationToken::new();
        let calls = [
            PrimitiveCall::PadApplyReport {
                target: "switch",
                report: initial,
            },
            PrimitiveCall::PadPress {
                target: "switch",
                buttons: &[Button::A, Button::X],
            },
            PrimitiveCall::PadRelease {
                target: "switch",
                buttons: &[Button::A],
            },
            PrimitiveCall::PadSetHat {
                target: "switch",
                hat: Hat::UpRight,
            },
            PrimitiveCall::PadSetStick {
                target: "switch",
                stick: StickId::Left,
                position: Stick { x: 0, y: 255 },
            },
            PrimitiveCall::PadCenterStick {
                target: "switch",
                stick: StickId::Right,
            },
            PrimitiveCall::PadReset { target: "switch" },
        ];
        for (index, call) in calls.into_iter().enumerate() {
            let PrimitiveOutput::InputReceipt(receipt) =
                invoke(&mut host, call, &token).await.unwrap()
            else {
                panic!()
            };
            assert_eq!(receipt.session, 9);
            assert_eq!(receipt.sequence, index as u64 + 1);
            assert_eq!(receipt.delivery, InputDelivery::Accepted);
            assert_eq!(receipt.written_at, written_at);
        }
        let invalid = Report {
            buttons: 0x8000,
            ..Report::default()
        };
        assert_eq!(
            invoke(
                &mut host,
                PrimitiveCall::PadApplyReport {
                    target: "switch",
                    report: invalid
                },
                &token
            )
            .await
            .unwrap_err()
            .kind(),
            ErrorKind::InvalidArgument
        );
    }
    assert_eq!(pad.reports.len(), 7);
    assert_eq!(
        pad.reports[1],
        Report {
            buttons: Button::B.mask() | Button::A.mask() | Button::X.mask(),
            ..initial
        }
    );
    assert_eq!(pad.reports[2].buttons, Button::B.mask() | Button::X.mask());
    assert_eq!(pad.reports[4].left, Stick { x: 0, y: 255 });
    assert_eq!(pad.reports[4].right, initial.right);
    assert_eq!(pad.reports[5].right, Stick::default());
    assert_eq!(pad.reports[6], Report::default());
    assert!(!pad.disconnected);
}

struct TextMock {
    texts: Vec<String>,
    cancel_after_commit: bool,
}
impl TextInput for TextMock {
    fn type_text(
        &mut self,
        text: &str,
        token: &CancellationToken,
    ) -> std::result::Result<InputReceipt, TextInputError> {
        self.texts.push(text.into());
        if self.cancel_after_commit {
            token.cancel();
        }
        Ok(InputReceipt {
            session: 17,
            sequence: self.texts.len() as u64,
            delivery: InputDelivery::Written,
            written_at: Instant::now(),
        })
    }
}

#[tokio::test]
async fn text_input_requires_its_own_target_and_preserves_unicode_without_pad_emulation() {
    let mut pad = PadMock::new();
    let mut text = TextMock {
        texts: vec![],
        cancel_after_commit: false,
    };
    {
        let mut host = PrimitiveHost::default();
        host.pads.insert("switch".into(), &mut pad);
        let token = CancellationToken::new();
        let error = invoke(
            &mut host,
            PrimitiveCall::TypeText {
                target: "switch",
                text: "名字🙂",
            },
            &token,
        )
        .await
        .unwrap_err();
        assert!(matches!(
            error,
            PrimitiveError::CapabilityUnavailable {
                capability: Capability::TextInput,
                ..
            }
        ));
        host.text.insert("editor".into(), &mut text);
        let PrimitiveOutput::InputReceipt(receipt) = invoke(
            &mut host,
            PrimitiveCall::TypeText {
                target: "editor",
                text: "名字🙂\ne\u{301}",
            },
            &token,
        )
        .await
        .unwrap() else {
            panic!()
        };
        assert_eq!(receipt.session, 17);
        assert_eq!(receipt.delivery, InputDelivery::Written);
    }
    assert_eq!(text.texts, ["名字🙂\ne\u{301}"]);
    assert!(pad.reports.is_empty());
}

#[derive(Default)]
struct Logs(Mutex<Vec<LogRecord>>);
impl LogSink for Logs {
    fn emit(&self, record: LogRecord) -> std::result::Result<(), String> {
        self.0.lock().unwrap().push(record);
        Ok(())
    }
}

#[tokio::test(start_paused = true)]
async fn meta_calls_share_run_clock_and_deliver_one_structured_user_log() {
    let logs = Arc::new(Logs::default());
    let meta = MetaContext::new(logs.clone());
    let mut host = PrimitiveHost {
        meta: Some(&meta),
        ..PrimitiveHost::default()
    };
    let token = CancellationToken::new();
    invoke(&mut host, PrimitiveCall::Sleep { duration_ms: 125 }, &token)
        .await
        .unwrap();
    let PrimitiveOutput::Int(now) = invoke(&mut host, PrimitiveCall::ClockNow, &token)
        .await
        .unwrap()
    else {
        panic!()
    };
    assert_eq!(now, 125);
    invoke(
        &mut host,
        PrimitiveCall::Log {
            level: LogLevel::Info,
            message: "once",
            fields: BTreeMap::from([("score".into(), LogValue::Float(0.75))]),
        },
        &token,
    )
    .await
    .unwrap();
    let records = logs.0.lock().unwrap();
    assert_eq!(records.len(), 1);
    assert_eq!(records[0].timestamp_ms, 125);
    assert_eq!(records[0].fields["score"], LogValue::Float(0.75));
}

#[tokio::test(start_paused = true)]
async fn sleep_and_media_wait_cancel_without_detached_work_or_retry() {
    struct WaitingVideo;
    impl VideoInput for WaitingVideo {
        fn capture<'a>(
            &'a mut self,
            _: Option<&'a SampleId>,
            token: &'a CancellationToken,
        ) -> CaptureFuture<'a, Arc<VideoFrame>> {
            Box::pin(async {
                token.cancelled().await;
                Err(CaptureError::Cancelled)
            })
        }
    }
    let mut video = WaitingVideo;
    let mut host = PrimitiveHost::default();
    host.videos.insert("waiting".into(), &mut video);
    for call in [
        PrimitiveCall::Capture {
            source: "waiting",
            after: None,
        },
        PrimitiveCall::Sleep {
            duration_ms: 10_000,
        },
    ] {
        let token = CancellationToken::new();
        let start = tokio::time::Instant::now();
        let (result, ()) = tokio::join!(invoke(&mut host, call, &token), async {
            tokio::time::sleep(Duration::from_millis(500)).await;
            token.cancel();
        });
        assert!(matches!(result, Err(PrimitiveError::Cancelled)));
        assert_eq!(start.elapsed(), Duration::from_millis(500));
    }
}

#[tokio::test]
async fn cancellation_before_call_has_no_delivery_but_after_commit_preserves_receipts() {
    let mut pad = PadMock::new();
    pad.cancel_after_commit = true;
    let mut text = TextMock {
        texts: vec![],
        cancel_after_commit: true,
    };
    {
        let mut host = PrimitiveHost::default();
        host.pads.insert("switch".into(), &mut pad);
        host.text.insert("editor".into(), &mut text);
        let token = CancellationToken::new();
        token.cancel();
        assert!(matches!(
            invoke(
                &mut host,
                PrimitiveCall::PadPress {
                    target: "switch",
                    buttons: &[Button::A]
                },
                &token
            )
            .await,
            Err(PrimitiveError::Cancelled)
        ));
        let token = CancellationToken::new();
        invoke(
            &mut host,
            PrimitiveCall::PadPress {
                target: "switch",
                buttons: &[Button::B],
            },
            &token,
        )
        .await
        .unwrap();
        assert!(token.is_cancelled());
        let token = CancellationToken::new();
        invoke(
            &mut host,
            PrimitiveCall::TypeText {
                target: "editor",
                text: "delivered",
            },
            &token,
        )
        .await
        .unwrap();
        assert!(token.is_cancelled());
    }
    assert_eq!(pad.reports.len(), 1);
    assert_eq!(pad.report.buttons, Button::B.mask());
    assert_eq!(text.texts, ["delivered"]);
}

#[tokio::test]
async fn missing_providers_are_runtime_errors_without_backend_fallback() {
    let image = Image::from_frame(&frame()).unwrap();
    let mut host = PrimitiveHost::default();
    let token = CancellationToken::new();
    for call in [
        PrimitiveCall::Capture {
            source: "missing",
            after: None,
        },
        PrimitiveCall::ReadAudio {
            source: "missing",
            frames: 1,
        },
        PrimitiveCall::MatchTemplate {
            matcher: "missing",
            image: &image,
            template: &image,
            config: MatchConfig::default(),
        },
        PrimitiveCall::Ocr {
            kind: OcrBackendKind::Tesseract,
            engine: "missing",
            image: ImageInput::Image(&image),
            region: None,
        },
        PrimitiveCall::PadReset { target: "missing" },
        PrimitiveCall::TypeText {
            target: "missing",
            text: "text",
        },
        PrimitiveCall::ClockNow,
        PrimitiveCall::Log {
            level: LogLevel::Info,
            message: "missing",
            fields: BTreeMap::new(),
        },
    ] {
        assert_eq!(
            invoke(&mut host, call, &token).await.unwrap_err().kind(),
            ErrorKind::CapabilityUnavailable
        );
    }
}

#[test]
fn errors_keep_backend_detail_cancellation_and_fatal_cleanup_classification() {
    for error in [
        PrimitiveError::from(CaptureError::Cancelled),
        PrimitiveError::from(VisionError::Cancelled),
        PrimitiveError::from(DeviceError::Cancelled),
        PrimitiveError::from(MetaError::Cancelled),
        PrimitiveError::from(TextInputError::Cancelled),
    ] {
        assert!(matches!(error, PrimitiveError::Cancelled));
        assert_eq!(error.recovery(), Recovery::Cancelled);
    }
    let error = PrimitiveError::from(CaptureError::AudioGap {
        expected: 5,
        oldest: 9,
    });
    assert_eq!(error.kind(), ErrorKind::AudioGap);
    assert_eq!(error.recovery(), Recovery::Recoverable);
    assert!(matches!(
        error,
        PrimitiveError::Capture(CaptureError::AudioGap {
            expected: 5,
            oldest: 9
        })
    ));
    let error = PrimitiveError::from(DeviceError::CleanupFailed {
        error: Box::new(DeviceError::Closed),
        previous: Some(Box::new(DeviceError::Cancelled)),
    });
    assert_eq!(error.kind(), ErrorKind::CleanupFailed);
    assert_eq!(error.recovery(), Recovery::RunFatal);
    let nested = PrimitiveError::from(DeviceError::ConnectionFailed {
        attempts: vec![easycon_device::AttemptFailure {
            baud_rate: 115200,
            error: DeviceError::WorkerPanicked,
        }],
    });
    assert_eq!(nested.recovery(), Recovery::RunFatal);
    let error = PrimitiveError::from(TextInputError::Transport {
        message: "partial delivery".into(),
        progress: InputProgress::Unknown,
    });
    assert_eq!(error.kind(), ErrorKind::TransportError);
    assert!(matches!(
        error,
        PrimitiveError::Text(TextInputError::Transport {
            progress: InputProgress::Unknown,
            ..
        })
    ));
}

#[test]
fn rms_cancellation_and_invalid_samples_are_checked_at_the_atomic_boundary() {
    let window = window();
    let token = CancellationToken::new();
    assert_eq!(
        window.rms_cancellable(&token).unwrap(),
        window.rms().unwrap()
    );
    token.cancel();
    assert_eq!(window.rms_cancellable(&token), Err(CaptureError::Cancelled));
    let invalid = AudioWindow {
        samples: vec![f32::NAN, 0.0].into(),
        ..Arc::try_unwrap(window).unwrap()
    };
    assert_eq!(
        PrimitiveError::from(
            invalid
                .rms_cancellable(&CancellationToken::new())
                .unwrap_err()
        )
        .kind(),
        ErrorKind::InvalidData
    );
}

#[tokio::test]
async fn log_commit_and_transport_failures_are_not_rewritten_by_late_cancellation() {
    struct CommitLog {
        token: CancellationToken,
        fail: bool,
        calls: Mutex<usize>,
    }
    impl LogSink for CommitLog {
        fn emit(&self, _: LogRecord) -> std::result::Result<(), String> {
            *self.calls.lock().unwrap() += 1;
            self.token.cancel();
            if self.fail {
                Err("sink failed".into())
            } else {
                Ok(())
            }
        }
    }
    for fail in [false, true] {
        let token = CancellationToken::new();
        let sink = Arc::new(CommitLog {
            token: token.clone(),
            fail,
            calls: Mutex::new(0),
        });
        let meta = MetaContext::new(sink.clone());
        let mut host = PrimitiveHost {
            meta: Some(&meta),
            ..PrimitiveHost::default()
        };
        let result = invoke(
            &mut host,
            PrimitiveCall::Log {
                level: LogLevel::Info,
                message: "once",
                fields: BTreeMap::new(),
            },
            &token,
        )
        .await;
        if fail {
            assert!(
                matches!(result, Err(PrimitiveError::Meta(MetaError::Sink(message))) if message == "sink failed")
            );
        } else {
            assert!(matches!(result, Ok(PrimitiveOutput::Unit)));
        }
        assert_eq!(*sink.calls.lock().unwrap(), 1);
        assert!(token.is_cancelled());
    }
    struct FailingText(usize);
    impl TextInput for FailingText {
        fn type_text(
            &mut self,
            _: &str,
            token: &CancellationToken,
        ) -> std::result::Result<InputReceipt, TextInputError> {
            self.0 += 1;
            token.cancel();
            Err(TextInputError::Transport {
                message: "delivery unconfirmed".into(),
                progress: InputProgress::Unknown,
            })
        }
    }
    let mut text = FailingText(0);
    let mut host = PrimitiveHost::default();
    host.text.insert("text".into(), &mut text);
    let result = invoke(
        &mut host,
        PrimitiveCall::TypeText {
            target: "text",
            text: "once",
        },
        &CancellationToken::new(),
    )
    .await;
    assert!(matches!(
        result,
        Err(PrimitiveError::Text(TextInputError::Transport {
            progress: InputProgress::Unknown,
            ..
        }))
    ));
    assert_eq!(text.0, 1);
}

#[tokio::test]
async fn pure_provider_completion_observes_cancellation_before_returning_a_value() {
    struct CancellingMatcher;
    impl TemplateMatcher for CancellingMatcher {
        fn kind(&self) -> TemplateBackendKind {
            TemplateBackendKind::OpenCv5
        }
        fn match_template(
            &mut self,
            image: &Image,
            template: &Image,
            config: MatchConfig,
            token: &CancellationToken,
        ) -> vision::Result<TemplateMatch> {
            let result = MatcherMock(0).match_template(image, template, config, token);
            token.cancel();
            result
        }
    }
    let image = Image::from_frame(&frame()).unwrap();
    let mut matcher = CancellingMatcher;
    let mut host = PrimitiveHost::default();
    host.matchers.insert("cancel".into(), &mut matcher);
    let result = invoke(
        &mut host,
        PrimitiveCall::MatchTemplate {
            matcher: "cancel",
            image: &image,
            template: &image,
            config: MatchConfig::default(),
        },
        &CancellationToken::new(),
    )
    .await;
    assert!(matches!(result, Err(PrimitiveError::Cancelled)));
}

mod semantics;

#[tokio::test]
async fn ocr_kinds_can_coexist_and_reject_mismatched_providers() {
    struct Engine {
        kind: OcrBackendKind,
        calls: usize,
    }
    impl OcrBackend for Engine {
        fn kind(&self) -> OcrBackendKind {
            self.kind
        }
        fn recognize(
            &mut self,
            image: &Image,
            _: Option<Rect>,
            _: &CancellationToken,
        ) -> vision::Result<OcrResult> {
            self.calls += 1;
            Ok(OcrResult {
                source: image.source().cloned(),
                region: image.bounds(),
                kind: self.kind,
                text: format!("{:?}:{}", self.kind, self.calls),
                boxes: vec![],
                profile: "independent".into(),
            })
        }
    }
    let image = Image::new(1, 1, vec![0; 3]).unwrap();
    let mut tesseract = Engine {
        kind: OcrBackendKind::Tesseract,
        calls: 0,
    };
    let mut ppocr = Engine {
        kind: OcrBackendKind::PpOcr,
        calls: 0,
    };
    let mut host = PrimitiveHost::default();
    host.ocr.insert("tess".into(), &mut tesseract);
    host.ocr.insert("paddle".into(), &mut ppocr);
    let token = CancellationToken::new();
    for (call, expected) in [
        (
            PrimitiveCall::Ocr {
                kind: OcrBackendKind::Tesseract,
                engine: "tess",
                image: ImageInput::Image(&image),
                region: None,
            },
            "Tesseract:1",
        ),
        (
            PrimitiveCall::Ocr {
                kind: OcrBackendKind::PpOcr,
                engine: "paddle",
                image: ImageInput::Image(&image),
                region: None,
            },
            "PpOcr:1",
        ),
        (
            PrimitiveCall::Ocr {
                kind: OcrBackendKind::Tesseract,
                engine: "tess",
                image: ImageInput::Image(&image),
                region: None,
            },
            "Tesseract:2",
        ),
    ] {
        let PrimitiveOutput::Ocr(result) = host.execute(call, &token).await.unwrap() else {
            panic!()
        };
        assert_eq!(result.text, expected);
    }
    for call in [
        PrimitiveCall::Ocr {
            kind: OcrBackendKind::Tesseract,
            engine: "paddle",
            image: ImageInput::Image(&image),
            region: None,
        },
        PrimitiveCall::Ocr {
            kind: OcrBackendKind::PpOcr,
            engine: "tess",
            image: ImageInput::Image(&image),
            region: None,
        },
    ] {
        assert!(matches!(
            host.execute(call, &token).await,
            Err(PrimitiveError::InvalidArgument(_))
        ));
    }
    drop(host);
    assert_eq!(tesseract.calls, 2);
    assert_eq!(ppocr.calls, 1);
}

#[tokio::test]
async fn ocr_frame_input_shares_pixels_and_rejects_malformed_frames_before_calling_provider() {
    struct FrameOcr {
        expected: Arc<VideoFrame>,
        calls: usize,
    }
    impl OcrBackend for FrameOcr {
        fn kind(&self) -> OcrBackendKind {
            OcrBackendKind::Tesseract
        }
        fn recognize(
            &mut self,
            image: &Image,
            _: Option<Rect>,
            _: &CancellationToken,
        ) -> vision::Result<OcrResult> {
            self.calls += 1;
            assert_eq!(image.rgb().as_ptr(), self.expected.rgb.as_ptr());
            Ok(OcrResult {
                source: image.source().cloned(),
                region: image.bounds(),
                kind: self.kind(),
                text: String::new(),
                boxes: vec![],
                profile: "frame-mock".into(),
            })
        }
    }
    let frame = frame();
    let malformed = VideoFrame {
        rgb: vec![0; 2].into(),
        id: frame.id.clone(),
        time: frame.time,
        format: frame.format.clone(),
    };
    let mut ocr = FrameOcr {
        expected: frame.clone(),
        calls: 0,
    };
    let mut host = PrimitiveHost::default();
    host.ocr.insert("ocr".into(), &mut ocr);
    let token = CancellationToken::new();
    let PrimitiveOutput::Ocr(result) = invoke(
        &mut host,
        PrimitiveCall::Ocr {
            kind: OcrBackendKind::Tesseract,
            engine: "ocr",
            image: ImageInput::Frame(&frame),
            region: None,
        },
        &token,
    )
    .await
    .unwrap() else {
        panic!()
    };
    assert_eq!(result.source.as_ref().unwrap().id, frame.id);
    assert!(result.boxes.is_empty());
    assert_eq!(result.confidence(), None);
    assert_eq!(
        invoke(
            &mut host,
            PrimitiveCall::Ocr {
                kind: OcrBackendKind::Tesseract,
                engine: "ocr",
                image: ImageInput::Frame(&malformed),
                region: None,
            },
            &token
        )
        .await
        .unwrap_err()
        .kind(),
        ErrorKind::InvalidArgument
    );
    drop(host);
    assert_eq!(ocr.calls, 1);
}
