use std::{
    sync::{
        Mutex,
        atomic::{AtomicUsize, Ordering},
    },
    time::{Duration, Instant},
};

use super::*;
use crate::{
    CaptureClock, CaptureError, SampleId,
    meta::{LogLevel, LogRecord, LogSink},
    primitives::{CaptureFuture, InputDelivery},
    video::{VideoBuffer, VideoFormat, VideoFrame, VideoSession, VideoSource},
};
use easycon_device::{Button, Delivery, DeviceError, Report, SessionInfo, WriteReceipt};

#[derive(Default)]
struct PadRecords {
    writes: Mutex<Vec<Report>>,
    resets: AtomicUsize,
    disconnects: AtomicUsize,
}
struct Pad {
    records: Arc<PadRecords>,
    info: SessionInfo,
    state: Report,
    write_error: Option<DeviceError>,
    reset_error: Option<DeviceError>,
    cancel_on_write: bool,
    cancel_during_reset: Option<CancellationToken>,
}
impl Pad {
    fn new(session: u64) -> Self {
        Self {
            records: Arc::new(PadRecords::default()),
            info: SessionInfo {
                port: "fake".into(),
                backend: "fake",
                baud_rate: 115200,
                session,
            },
            state: Report::default(),
            write_error: None,
            reset_error: None,
            cancel_on_write: false,
            cancel_during_reset: None,
        }
    }
    fn write(&mut self, report: Report) -> WriteReceipt {
        self.state = report;
        let mut writes = self.records.writes.lock().unwrap();
        writes.push(report);
        WriteReceipt {
            session: self.info.session,
            sequence: writes.len() as u64,
            delivery: Delivery::Accepted,
            written_at: Instant::now(),
        }
    }
}
impl Gamepad for Pad {
    fn info(&self) -> &SessionInfo {
        &self.info
    }
    fn is_connected(&self) -> bool {
        true
    }
    fn state(&self) -> Option<Report> {
        Some(self.state)
    }
    fn apply_report(
        &mut self,
        report: Report,
        token: &CancellationToken,
    ) -> easycon_device::Result<WriteReceipt> {
        if token.is_cancelled() {
            return Err(DeviceError::Cancelled);
        }
        if let Some(error) = self.write_error.take() {
            return Err(error);
        }
        let receipt = self.write(report);
        if self.cancel_on_write {
            token.cancel();
        }
        Ok(receipt)
    }
    fn reset(&mut self, token: &CancellationToken) -> easycon_device::Result<WriteReceipt> {
        assert!(!token.is_cancelled(), "cleanup must use a live token");
        self.records.resets.fetch_add(1, Ordering::SeqCst);
        if let Some(parent) = &self.cancel_during_reset {
            parent.cancel();
        }
        if let Some(error) = self.reset_error.take() {
            token.cancel(); // Must not poison a later lease's cleanup token.
            return Err(error);
        }
        Ok(self.write(Report::default()))
    }
    fn disconnect(&mut self) -> easycon_device::Result<()> {
        self.records.disconnects.fetch_add(1, Ordering::SeqCst);
        Ok(())
    }
}
fn failure(message: &str) -> Arc<PrimitiveError> {
    Arc::new(PrimitiveError::InvalidArgument(message.into()))
}

#[tokio::test]
async fn success_resets_once_preserves_receipt_and_keeps_external_connection() {
    let mut pad = Pad::new(7);
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    let mut run = RunScope::new(bindings, &CancellationToken::new());
    let result = run
        .execute(PrimitiveCall::PadPress {
            target: "switch",
            buttons: &[Button::A],
        })
        .await;
    let completion = run.finish(RunOutcome::from(result));
    assert!(matches!(
        completion.outcome,
        RunOutcome::Success(PrimitiveOutput::InputReceipt(_))
    ));
    assert!(completion.prior.is_empty());
    assert!(!completion.cleanup.has_failures());
    let entry = &completion.cleanup.entries[0];
    assert_eq!(entry.resource.session, Some(7));
    assert_eq!(entry.resource.binding, "switch");
    let CleanupState::Released {
        receipt: Some(receipt),
    } = &entry.state
    else {
        panic!()
    };
    assert_eq!(receipt.session, 7);
    assert_eq!(receipt.sequence, 2);
    assert_eq!(receipt.delivery, InputDelivery::Accepted);
    assert_eq!(records.resets.load(Ordering::SeqCst), 1);
    assert_eq!(records.disconnects.load(Ordering::SeqCst), 0);
    assert_eq!(
        records.writes.lock().unwrap().last(),
        Some(&Report::default())
    );
    assert_eq!(pad.state, Report::default());
}

#[test]
fn cleanup_failure_overrides_success_failure_and_cancellation_without_losing_candidates() {
    for candidate in [
        RunOutcome::Success(42),
        RunOutcome::Failure(RunFailure::Primitive(failure("body"))),
        RunOutcome::Cancelled,
    ] {
        let mut failed = Pad::new(1);
        failed.reset_error = Some(DeviceError::WriteTimeout { written: 2 });
        let failed_records = failed.records.clone();
        let mut good = Pad::new(2);
        let good_records = good.records.clone();
        let mut bindings = RunBindings::default();
        bindings.pads.insert("a-failed".into(), &mut failed);
        bindings.pads.insert("b-good".into(), &mut good);
        let parent = CancellationToken::new();
        let run = RunScope::new(bindings, &parent);
        if matches!(candidate, RunOutcome::Cancelled) {
            parent.cancel();
        }
        let completion = run.finish(candidate);
        assert!(matches!(
            completion.outcome,
            RunOutcome::Failure(RunFailure::CleanupFailed)
        ));
        assert_eq!(RunFailure::CleanupFailed.recovery(), Recovery::RunFatal);
        assert_eq!(completion.prior.len(), 1);
        match &completion.prior[0] {
            RunOutcome::Success(value) => assert_eq!(*value, 42),
            RunOutcome::Failure(RunFailure::Primitive(error)) => assert!(
                matches!(&**error, PrimitiveError::InvalidArgument(message) if message == "body")
            ),
            RunOutcome::Cancelled => {}
            _ => panic!(),
        }
        assert!(completion.cleanup.has_failures());
        let CleanupState::ReleaseUnconfirmed { error } = &completion.cleanup.entries[0].state
        else {
            panic!()
        };
        assert!(matches!(
            &**error,
            PrimitiveError::Device(DeviceError::WriteTimeout { written: 2 })
        ));
        assert!(matches!(
            &completion.cleanup.entries[1].state,
            CleanupState::Released { receipt: Some(_) }
        ));
        assert_eq!(failed_records.resets.load(Ordering::SeqCst), 1);
        assert_eq!(good_records.resets.load(Ordering::SeqCst), 1);
    }
}

#[tokio::test]
async fn fatal_call_latches_original_cause_and_blocks_all_further_calls() {
    let mut pad = Pad::new(1);
    pad.write_error = Some(DeviceError::WorkerPanicked);
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("pad".into(), &mut pad);
    let mut run = RunScope::new(bindings, &CancellationToken::new());
    let original = run
        .execute(PrimitiveCall::PadReset { target: "missing" })
        .await
        .unwrap_err();
    assert_eq!(original.recovery(), Recovery::Recoverable);
    let original = run
        .execute(PrimitiveCall::PadPress {
            target: "pad",
            buttons: &[Button::A],
        })
        .await
        .unwrap_err();
    assert_eq!(original.recovery(), Recovery::RunFatal);
    for call in [
        PrimitiveCall::PadPress {
            target: "pad",
            buttons: &[Button::B],
        },
        PrimitiveCall::Sleep { duration_ms: 0 },
        PrimitiveCall::ClockNow,
    ] {
        assert!(Arc::ptr_eq(
            &original,
            &run.execute(call).await.unwrap_err()
        ));
    }
    let completion = run.finish(RunOutcome::Success("candidate"));
    let RunOutcome::Failure(RunFailure::Primitive(error)) = completion.outcome else {
        panic!()
    };
    assert!(Arc::ptr_eq(&original, &error));
    assert!(matches!(
        &completion.prior[..],
        [RunOutcome::Success("candidate")]
    ));
    assert_eq!(records.resets.load(Ordering::SeqCst), 1);
    assert_eq!(records.writes.lock().unwrap().len(), 1); // Only cleanup.
}

#[tokio::test]
async fn handling_a_recoverable_error_does_not_close_the_scope() {
    let mut pad = Pad::new(1);
    pad.write_error = Some(DeviceError::InvalidArgument("bad request".into()));
    let mut bindings = RunBindings::default();
    bindings.pads.insert("pad".into(), &mut pad);
    let mut run = RunScope::new(bindings, &CancellationToken::new());
    let error = run
        .execute(PrimitiveCall::PadPress {
            target: "pad",
            buttons: &[Button::A],
        })
        .await
        .unwrap_err();
    assert_eq!(error.recovery(), Recovery::Recoverable);
    let result = run
        .execute(PrimitiveCall::PadPress {
            target: "pad",
            buttons: &[Button::B],
        })
        .await;
    let completion = run.finish(RunOutcome::from(result));
    assert!(matches!(completion.outcome, RunOutcome::Success(_)));
}

#[tokio::test]
async fn cleanup_failure_retains_latched_fatal_and_displaced_candidate() {
    let mut pad = Pad::new(1);
    pad.write_error = Some(DeviceError::WorkerPanicked);
    pad.reset_error = Some(DeviceError::Closed);
    let mut bindings = RunBindings::default();
    bindings.pads.insert("pad".into(), &mut pad);
    let mut run = RunScope::new(bindings, &CancellationToken::new());
    let original = run
        .execute(PrimitiveCall::PadPress {
            target: "pad",
            buttons: &[Button::A],
        })
        .await
        .unwrap_err();
    let completion = run.finish(RunOutcome::Success(42));
    assert!(matches!(
        completion.outcome,
        RunOutcome::Failure(RunFailure::CleanupFailed)
    ));
    let [
        RunOutcome::Success(42),
        RunOutcome::Failure(RunFailure::Primitive(retained)),
    ] = &completion.prior[..]
    else {
        panic!()
    };
    assert!(Arc::ptr_eq(&original, retained));
    assert!(matches!(&completion.cleanup.entries[0].state,
        CleanupState::ReleaseUnconfirmed { error }
            if matches!(&**error, PrimitiveError::Device(DeviceError::Closed))));
}

#[tokio::test]
async fn cancellation_after_action_commit_preserves_candidate_and_cleanup_still_runs() {
    let parent = CancellationToken::new();
    let mut pad = Pad::new(1);
    pad.cancel_on_write = true;
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("pad".into(), &mut pad);
    let mut run = RunScope::new(bindings, &parent);
    let result = run
        .execute(PrimitiveCall::PadPress {
            target: "pad",
            buttons: &[Button::A],
        })
        .await;
    assert!(result.is_ok());
    assert!(!parent.is_cancelled());
    let completion = run.finish(RunOutcome::from(result));
    assert!(matches!(completion.outcome, RunOutcome::Cancelled));
    assert!(matches!(
        &completion.prior[..],
        [RunOutcome::Success(PrimitiveOutput::InputReceipt(_))]
    ));
    assert_eq!(records.resets.load(Ordering::SeqCst), 1);
    assert_eq!(records.writes.lock().unwrap().len(), 2);
}

#[test]
fn cancellation_observed_during_cleanup_precedes_success_but_not_fatal_failure() {
    for fatal in [false, true] {
        let parent = CancellationToken::new();
        let mut pad = Pad::new(1);
        pad.cancel_during_reset = Some(parent.clone());
        let mut bindings = RunBindings::default();
        bindings.pads.insert("pad".into(), &mut pad);
        let candidate = if fatal {
            RunOutcome::Failure(RunFailure::Primitive(Arc::new(PrimitiveError::Device(
                DeviceError::WorkerPanicked,
            ))))
        } else {
            RunOutcome::Success(42)
        };
        let completion = RunScope::new(bindings, &parent).finish(candidate);
        assert!(parent.is_cancelled());
        if fatal {
            assert!(matches!(completion.outcome, RunOutcome::Failure(_)));
            assert!(completion.prior.is_empty());
        } else {
            assert!(matches!(completion.outcome, RunOutcome::Cancelled));
            assert!(matches!(&completion.prior[..], [RunOutcome::Success(42)]));
        }
    }
}

#[tokio::test(start_paused = true)]
async fn local_cancellation_is_isolated_and_parent_cancellation_interrupts_wait() {
    let parent = CancellationToken::new();
    let mut first = RunScope::new(RunBindings::default(), &parent);
    let mut second = RunScope::new(RunBindings::default(), &parent);
    first.cancel();
    assert_eq!(
        first
            .execute(PrimitiveCall::Sleep { duration_ms: 0 })
            .await
            .unwrap_err()
            .recovery(),
        Recovery::Cancelled
    );
    assert!(
        second
            .execute(PrimitiveCall::Sleep { duration_ms: 0 })
            .await
            .is_ok()
    );
    let cancel = async {
        tokio::time::sleep(Duration::from_millis(500)).await;
        parent.cancel();
    };
    let (result, ()) = tokio::join!(
        second.execute(PrimitiveCall::Sleep {
            duration_ms: 10_000
        }),
        cancel
    );
    assert_eq!(result.unwrap_err().recovery(), Recovery::Cancelled);
    assert!(matches!(
        first.finish(RunOutcome::Success(())).outcome,
        RunOutcome::Cancelled
    ));
    assert!(matches!(
        second.finish(RunOutcome::Success(())).outcome,
        RunOutcome::Cancelled
    ));
}

#[test]
fn drop_is_best_effort_reset_once_without_disconnect() {
    let mut pad = Pad::new(1);
    pad.reset_error = Some(DeviceError::Closed);
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("pad".into(), &mut pad);
    drop(RunScope::new(bindings, &CancellationToken::new()));
    assert_eq!(records.resets.load(Ordering::SeqCst), 1);
    assert_eq!(records.disconnects.load(Ordering::SeqCst), 0);
}

struct SyntheticVideo(Arc<AtomicUsize>);
impl VideoSource for SyntheticVideo {
    fn format(&self) -> VideoFormat {
        VideoFormat {
            width: 1,
            height: 1,
            frames_per_second: 30,
            encoding: "RGB3".into(),
        }
    }
    fn next_frame(&mut self) -> crate::Result<VideoBuffer> {
        std::thread::sleep(Duration::from_millis(1));
        Ok(VideoBuffer {
            rgb: vec![10, 20, 30],
            source_ns: None,
        })
    }
    fn stop(&mut self) -> crate::Result<()> {
        self.0.fetch_add(1, Ordering::SeqCst);
        Ok(())
    }
}

#[tokio::test]
async fn finishing_one_run_drops_its_subscription_without_stopping_shared_video() {
    let stopped = Arc::new(AtomicUsize::new(0));
    let counter = stopped.clone();
    let mut session =
        VideoSession::from_source("synthetic".into(), CaptureClock::default(), move || {
            Ok(Box::new(SyntheticVideo(counter)))
        })
        .unwrap();
    let parent = CancellationToken::new();
    let mut first = RunBindings::default();
    first
        .videos
        .insert("camera".into(), Box::new(session.subscribe()));
    let mut second = RunBindings::default();
    second
        .videos
        .insert("camera".into(), Box::new(session.subscribe()));
    let first = RunScope::new(first, &parent);
    let mut second = RunScope::new(second, &parent);
    first.cancel();
    let completion = first.finish(RunOutcome::Success(()));
    assert!(matches!(completion.outcome, RunOutcome::Cancelled));
    assert!(matches!(
        completion.cleanup.entries[0].state,
        CleanupState::Released { receipt: None }
    ));
    assert_eq!(stopped.load(Ordering::SeqCst), 0);
    let result = tokio::time::timeout(
        Duration::from_secs(1),
        second.execute(PrimitiveCall::Capture {
            source: "camera",
            after: None,
        }),
    )
    .await
    .unwrap()
    .unwrap();
    assert!(matches!(result, PrimitiveOutput::Frame(_)));
    let completion = second.finish(RunOutcome::Success(result));
    assert!(!completion.cleanup.has_failures());
    assert_eq!(stopped.load(Ordering::SeqCst), 0);
    session.stop().unwrap();
    assert_eq!(stopped.load(Ordering::SeqCst), 1);
}

struct WaitingVideo(Arc<AtomicUsize>);
impl VideoInput for WaitingVideo {
    fn capture<'a>(
        &'a mut self,
        _: Option<&'a SampleId>,
        token: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<VideoFrame>> {
        Box::pin(async move {
            token.cancelled().await;
            Err(CaptureError::Cancelled)
        })
    }
}
impl Drop for WaitingVideo {
    fn drop(&mut self) {
        self.0.fetch_add(1, Ordering::SeqCst);
    }
}
struct WaitingAudio(Arc<AtomicUsize>);
impl AudioInput for WaitingAudio {
    fn read_window<'a>(
        &'a mut self,
        _: usize,
        token: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<crate::audio::AudioWindow>> {
        Box::pin(async move {
            token.cancelled().await;
            Err(CaptureError::Cancelled)
        })
    }
}
impl Drop for WaitingAudio {
    fn drop(&mut self) {
        self.0.fetch_add(1, Ordering::SeqCst);
    }
}
struct Logs(Arc<AtomicUsize>);
impl LogSink for Logs {
    fn emit(&self, _: LogRecord) -> std::result::Result<(), String> {
        self.0.fetch_add(1, Ordering::SeqCst);
        Ok(())
    }
}

#[tokio::test]
async fn explicit_finish_drops_owned_media_and_keeps_borrowed_meta_available() {
    let drops = Arc::new(AtomicUsize::new(0));
    let logs = Arc::new(AtomicUsize::new(0));
    let meta = MetaContext::new(Arc::new(Logs(logs.clone())));
    let mut bindings = RunBindings {
        meta: Some(&meta),
        ..RunBindings::default()
    };
    bindings
        .videos
        .insert("video".into(), Box::new(WaitingVideo(drops.clone())));
    bindings
        .audio
        .insert("audio".into(), Box::new(WaitingAudio(drops.clone())));
    let parent = CancellationToken::new();
    let mut run = RunScope::new(bindings, &parent);
    assert!(
        run.execute(PrimitiveCall::Log {
            level: LogLevel::Info,
            message: "run",
            fields: BTreeMap::new()
        })
        .await
        .is_ok()
    );
    parent.cancel();
    let result = run
        .execute(PrimitiveCall::Capture {
            source: "video",
            after: None,
        })
        .await;
    let completion = run.finish(RunOutcome::from(result));
    assert!(matches!(completion.outcome, RunOutcome::Cancelled));
    assert_eq!(drops.load(Ordering::SeqCst), 2);
    assert_eq!(completion.cleanup.entries.len(), 3);
    assert_eq!(
        completion
            .cleanup
            .entries
            .iter()
            .filter(|entry| matches!(entry.state, CleanupState::NotRequired))
            .count(),
        1
    );
    meta.log(
        LogLevel::Info,
        "still available",
        BTreeMap::new(),
        &CancellationToken::new(),
    )
    .unwrap();
    assert_eq!(logs.load(Ordering::SeqCst), 2);
}
