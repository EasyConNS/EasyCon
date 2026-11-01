use super::*;
use crate::{
    CaptureError,
    primitives::{CaptureFuture, VideoInput},
    video::{VideoFormat, VideoFrame},
    vision::Rect,
};
use std::{
    sync::{
        Barrier,
        atomic::{AtomicUsize, Ordering},
    },
    time::Duration,
};

#[test]
fn stop_control_clones_share_the_first_reason_across_threads() {
    let control = RunControl::new();
    assert!(!control.is_cancelled());
    assert_eq!(control.reason(), None);
    let barrier = Arc::new(Barrier::new(3));
    let threads = [RunStopReason::Shutdown, RunStopReason::Aborted].map(|reason| {
        let (control, barrier) = (control.clone(), barrier.clone());
        std::thread::spawn(move || {
            barrier.wait();
            (reason, control.request_stop(reason))
        })
    });
    barrier.wait();
    let results = threads.map(|t| t.join().unwrap());
    assert_eq!(results.iter().filter(|(_, accepted)| *accepted).count(), 1);
    let winner = results
        .into_iter()
        .find(|(_, accepted)| *accepted)
        .unwrap()
        .0;
    assert!(control.is_cancelled());
    assert_eq!(control.reason(), Some(winner));
    assert!(!control.request_stop(RunStopReason::UserStop));
    assert_eq!(control.reason(), Some(winner));
}

fn cancellation(execution: &Execution) -> &FlowError {
    let FlowOutcome::Cancelled(error) = &execution.outcome else {
        panic!("expected cancellation: {:?}", execution.outcome)
    };
    error
}
fn requested(execution: &Execution, expected: CancelReason) {
    let reasons: Vec<_> = execution
        .trace
        .iter()
        .filter_map(|event| match event.kind {
            TraceKind::CancellationRequested { reason } => Some(reason),
            _ => None,
        })
        .collect();
    assert_eq!(reasons, vec![expected]);
}

#[tokio::test]
async fn prestart_stop_keeps_reason_without_starting_nodes_and_raw_tokens_stay_compatible() {
    let checked = document(eval("unused", Expr::int(42)), Type::Int)
        .validate()
        .unwrap();
    for reason in [
        RunStopReason::UserStop,
        RunStopReason::Shutdown,
        RunStopReason::Aborted,
    ] {
        let control = RunControl::new();
        control.request_stop(reason);
        control.cancelled().await;
        let execution = checked
            .execute_controlled(RunBindings::default(), BTreeMap::new(), &control)
            .await
            .unwrap();
        assert_eq!(cancellation(&execution).cancel_reason, Some(reason.into()));
        requested(&execution, reason.into());
        assert!(
            !execution
                .trace
                .iter()
                .any(|e| matches!(e.kind, TraceKind::NodeStarted))
        );
        assert!(
            execution
                .trace
                .iter()
                .any(|e| matches!(e.kind, TraceKind::CleanupCompleted { .. }))
        );
    }
    let token = CancellationToken::new();
    token.cancel();
    let execution = checked
        .execute(RunBindings::default(), BTreeMap::new(), &token)
        .await
        .unwrap();
    assert_eq!(
        cancellation(&execution).cancel_reason,
        Some(CancelReason::UserStop)
    );
}

struct WaitingVideo {
    started: Arc<tokio::sync::Notify>,
    calls: Arc<AtomicUsize>,
}
impl VideoInput for WaitingVideo {
    fn capture<'a>(
        &'a mut self,
        _: Option<&'a SampleId>,
        token: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<VideoFrame>> {
        Box::pin(async move {
            self.calls.fetch_add(1, Ordering::SeqCst);
            self.started.notify_one();
            token.cancelled().await;
            Err(CaptureError::Cancelled)
        })
    }
}
fn waiting_probe() -> Node {
    Node::new(
        "probe",
        Flow::Pipe {
            source: Box::new(call(
                "capture",
                "capture",
                vec![
                    named("source", source(SourceKind::Video, "camera")),
                    named(
                        "after",
                        Expr::None {
                            element: Type::SampleId,
                        },
                    ),
                ],
            )),
            sink: Box::new(eval("discard", literal(Literal::Unit))),
        },
    )
}

#[tokio::test]
async fn host_stop_bypasses_recovery_and_preserves_reason_through_nested_combinators() {
    for reason in [RunStopReason::Shutdown, RunStopReason::Aborted] {
        let body = Node::new(
            "catch",
            Flow::Catch {
                body: Box::new(Node::new(
                    "fallback",
                    Flow::Fallback {
                        primary: Box::new(Node::new(
                            "retry",
                            Flow::Retry {
                                body: Box::new(Node::new(
                                    "timeout",
                                    Flow::Timeout {
                                        duration_ms: Expr::int(60_000),
                                        body: Box::new(waiting_probe()),
                                    },
                                )),
                                max: Expr::int(3),
                                retry_on: Expr::List {
                                    element: Type::Str,
                                    items: vec![string("SourceUnavailable")],
                                },
                                backoff: Backoff::None,
                            },
                        )),
                        secondary: Box::new(eval("secondary", literal(Literal::Unit))),
                    },
                )),
                bind_error: Some("error".into()),
                handler: Box::new(eval("handler", literal(Literal::Unit))),
            },
        );
        let checked = document(body, Type::Unit).validate().unwrap();
        let control = RunControl::new();
        let started = Arc::new(tokio::sync::Notify::new());
        let calls = Arc::new(AtomicUsize::new(0));
        let mut bindings = RunBindings::default();
        bindings.videos.insert(
            "camera".into(),
            Box::new(WaitingVideo {
                started: started.clone(),
                calls: calls.clone(),
            }),
        );
        let run = checked.execute_controlled(bindings, BTreeMap::new(), &control);
        let stop = async {
            started.notified().await;
            assert!(control.request_stop(reason));
        };
        let (execution, ()) =
            tokio::time::timeout(Duration::from_secs(2), async { tokio::join!(run, stop) })
                .await
                .unwrap();
        let execution = execution.unwrap();
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        let error = cancellation(&execution);
        assert_eq!(error.cancel_reason, Some(reason.into()));
        assert!(
            matches!(error.field("cancel_reason"), Some(Value::Option { value: Some(v), .. }) if matches!(&*v, Value::Str(s) if s == &format!("{reason:?}")))
        );
        requested(&execution, reason.into());
        assert!(
            !execution
                .trace
                .iter()
                .any(|e| matches!(e.node_id.as_deref(), Some("handler" | "secondary")))
        );
        assert!(
            execution
                .trace
                .iter()
                .filter_map(|e| match &e.kind {
                    TraceKind::NodeCancelled { error } => Some(error),
                    _ => None,
                })
                .all(|e| e.cancel_reason == Some(reason.into()))
        );
        assert!(!execution.cleanup.has_failures());
    }
}

struct StopOnCleanup {
    pad: Pad,
    control: RunControl,
}
impl Gamepad for StopOnCleanup {
    fn info(&self) -> &SessionInfo {
        self.pad.info()
    }
    fn is_connected(&self) -> bool {
        self.pad.is_connected()
    }
    fn state(&self) -> Option<Report> {
        self.pad.state()
    }
    fn apply_report(
        &mut self,
        report: Report,
        token: &CancellationToken,
    ) -> easycon_device::Result<WriteReceipt> {
        self.pad.apply_report(report, token)
    }
    fn reset(&mut self, token: &CancellationToken) -> easycon_device::Result<WriteReceipt> {
        self.control.request_stop(RunStopReason::Shutdown);
        self.pad.reset(token)
    }
    fn disconnect(&mut self) -> easycon_device::Result<()> {
        self.pad.disconnect()
    }
}

#[tokio::test]
async fn cancellation_during_cleanup_keeps_shutdown_reason_and_fatal_cleanup_still_wins() {
    let checked = document(eval("value", Expr::int(42)), Type::Int)
        .validate()
        .unwrap();
    for fail_reset in [false, true] {
        let control = RunControl::new();
        let mut pad = StopOnCleanup {
            pad: Pad {
                fail_reset,
                ..Pad::new()
            },
            control: control.clone(),
        };
        let mut bindings = RunBindings::default();
        bindings.pads.insert("switch".into(), &mut pad);
        let execution = checked
            .execute_controlled(bindings, BTreeMap::new(), &control)
            .await
            .unwrap();
        requested(&execution, CancelReason::Shutdown);
        assert!(
            execution
                .prior
                .iter()
                .any(|p| matches!(p, FlowOutcome::Success(Value::Int(42))))
        );
        if fail_reset {
            assert!(
                matches!(&execution.outcome, FlowOutcome::Failure(e) if e.kind == FlowErrorKind::CleanupFailed && e.recovery() == Recovery::RunFatal)
            );
            assert!(execution.prior.iter().any(|p| matches!(p, FlowOutcome::Cancelled(e) if e.cancel_reason == Some(CancelReason::Shutdown))));
        } else {
            assert_eq!(
                cancellation(&execution).cancel_reason,
                Some(CancelReason::Shutdown)
            );
        }
    }
}

#[tokio::test]
async fn local_deadlines_and_budget_exhaustion_do_not_cancel_host_control() {
    let control = RunControl::new();
    let checked = document(
        Node::new(
            "deadline",
            Flow::Timeout {
                duration_ms: Expr::int(1),
                body: Box::new(call(
                    "sleep",
                    "sleep",
                    vec![named("duration_ms", Expr::int(60_000))],
                )),
            },
        ),
        Type::Unit,
    )
    .validate()
    .unwrap();
    let execution = checked
        .execute_controlled(RunBindings::default(), BTreeMap::new(), &control)
        .await
        .unwrap();
    assert!(
        matches!(&execution.outcome, FlowOutcome::Failure(e) if e.kind == FlowErrorKind::Timeout)
    );
    assert!(execution.trace.iter().any(|e| matches!(
        e.kind,
        TraceKind::ScopeCancellation {
            reason: CancelReason::DeadlineExceeded
        }
    )));
    assert!(!control.is_cancelled());
    let checked = document(
        Node::new(
            "loop",
            Flow::LoopUntil {
                body: Box::new(eval("body", literal(Literal::Unit))),
                cond: literal(Literal::Bool(false)),
                max_iters: None,
                state_init: None,
                state_next: None,
            },
        ),
        Type::Unit,
    )
    .validate()
    .unwrap();
    let execution = checked
        .execute_controlled_with_limits(
            RunBindings::default(),
            BTreeMap::new(),
            &control,
            ExecutionLimits {
                iterations: 0,
                call_depth: 128,
            },
        )
        .await
        .unwrap();
    assert_eq!(
        cancellation(&execution).cancel_reason,
        Some(CancelReason::BudgetExceeded)
    );
    requested(&execution, CancelReason::BudgetExceeded);
    assert_eq!(control.reason(), None);
}

struct CancelledVideo;
impl VideoInput for CancelledVideo {
    fn capture<'a>(
        &'a mut self,
        _: Option<&'a SampleId>,
        _: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<VideoFrame>> {
        Box::pin(async { Err(CaptureError::Cancelled) })
    }
}

#[tokio::test]
async fn provider_cancellation_is_not_misreported_as_a_user_stop() {
    let control = RunControl::new();
    let mut bindings = RunBindings::default();
    bindings
        .videos
        .insert("camera".into(), Box::new(CancelledVideo));
    let execution = document(waiting_probe(), Type::Unit)
        .validate()
        .unwrap()
        .execute_controlled(bindings, BTreeMap::new(), &control)
        .await
        .unwrap();
    assert_eq!(
        cancellation(&execution).cancel_reason,
        Some(CancelReason::ProviderCancelled)
    );
    requested(&execution, CancelReason::ProviderCancelled);
    assert!(!control.is_cancelled());
}

#[tokio::test]
async fn shutdown_during_cleanup_preserves_an_earlier_provider_cancellation_as_cause() {
    let control = RunControl::new();
    let mut pad = StopOnCleanup {
        pad: Pad::new(),
        control: control.clone(),
    };
    let mut bindings = RunBindings::default();
    bindings
        .videos
        .insert("camera".into(), Box::new(CancelledVideo));
    bindings.pads.insert("switch".into(), &mut pad);
    let execution = document(waiting_probe(), Type::Unit)
        .validate()
        .unwrap()
        .execute_controlled(bindings, BTreeMap::new(), &control)
        .await
        .unwrap();
    let error = cancellation(&execution);
    assert_eq!(error.cancel_reason, Some(CancelReason::Shutdown));
    assert_eq!(
        error.cause.as_ref().unwrap().cancel_reason,
        Some(CancelReason::ProviderCancelled)
    );
    let reasons: Vec<_> = execution
        .trace
        .iter()
        .filter_map(|event| match event.kind {
            TraceKind::CancellationRequested { reason } => Some(reason),
            _ => None,
        })
        .collect();
    assert_eq!(
        reasons,
        vec![CancelReason::ProviderCancelled, CancelReason::Shutdown]
    );
}

fn frame(time: SampleTime) -> Arc<VideoFrame> {
    Arc::new(VideoFrame {
        id: SampleId {
            source: "camera".into(),
            session: SessionId(u64::MAX),
            sequence: u64::MAX,
        },
        time,
        format: VideoFormat {
            width: 4,
            height: 3,
            frames_per_second: 60,
            encoding: "MJPEG".into(),
        },
        rgb: vec![0; 36].into(),
    })
}
async fn project(value: Value, expr: Expr, output: Type) -> Value {
    let mut doc = document(eval("projection", expr), output);
    doc.definitions
        .get_mut("main")
        .unwrap()
        .params
        .insert("sample".into(), value.ty());
    let execution = doc
        .validate()
        .unwrap()
        .execute(
            RunBindings::default(),
            BTreeMap::from([("sample".into(), value)]),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    let FlowOutcome::Success(value) = execution.outcome else {
        panic!("{:?}", execution.outcome)
    };
    value
}

#[tokio::test]
async fn frame_time_and_format_drive_expressions_without_truncating_nanoseconds() {
    let sample = frame(SampleTime {
        received_ns: u64::MAX,
        source_ns: Some(u64::MAX - 1),
    });
    let expr = Expr::Tuple {
        items: vec![
            field(field(Expr::var("sample"), "time"), "received_ns"),
            function(
                "unwrap",
                vec![field(field(Expr::var("sample"), "time"), "source_ns")],
            ),
            field(field(Expr::var("sample"), "format"), "frames_per_second"),
            field(field(Expr::var("sample"), "format"), "encoding"),
        ],
    };
    let output = Type::Tuple(vec![Type::Str, Type::Str, Type::Int, Type::Str]);
    let actual = project(Value::Frame(sample), expr, output.clone()).await;
    let expected = Value::Tuple(vec![
        Value::Str(u64::MAX.to_string()),
        Value::Str((u64::MAX - 1).to_string()),
        Value::Int(60),
        Value::Str("MJPEG".into()),
    ]);
    assert_eq!(actual.ty(), output);
    assert!(actual.equal(&expected));
    assert!(Type::Frame.field("unknown").is_none());
}

#[tokio::test]
async fn audio_time_preserves_absent_source_clock_and_existing_format_fields() {
    let sample = Value::AudioWindow(Arc::new(AudioWindow {
        id: frame(SampleTime {
            received_ns: 0,
            source_ns: None,
        })
        .id
        .clone(),
        time: SampleTime {
            received_ns: u64::MAX,
            source_ns: None,
        },
        sample_rate: 48_000,
        channels: 2,
        samples: vec![0.0; 4].into(),
    }));
    let expr = Expr::Tuple {
        items: vec![
            field(Expr::var("sample"), "time"),
            field(Expr::var("sample"), "frames"),
            field(Expr::var("sample"), "channels"),
            field(Expr::var("sample"), "sample_rate"),
        ],
    };
    let output = Type::Tuple(vec![
        Type::AudioWindow.field("time").unwrap(),
        Type::Int,
        Type::Int,
        Type::Int,
    ]);
    let actual = project(sample, expr, output.clone()).await;
    assert_eq!(actual.ty(), output);
    let Value::Tuple(items) = actual else {
        panic!()
    };
    assert!(matches!(
        items[0].field("source_ns"),
        Some(Value::Option {
            element: Type::Str,
            value: None
        })
    ));
    assert!(
        matches!(&items[0].field("received_ns"), Some(Value::Str(s)) if s == &u64::MAX.to_string())
    );
    assert!(items[1].equal(&Value::Int(2)));
    assert!(items[2].equal(&Value::Int(2)));
    assert!(items[3].equal(&Value::Int(48_000)));
}

#[tokio::test]
async fn image_projection_preserves_original_source_crop_coordinates_and_none() {
    let sample = frame(SampleTime {
        received_ns: u64::MAX,
        source_ns: Some(0),
    });
    let image = Image::from_frame(&sample)
        .unwrap()
        .crop(Rect {
            x: 1,
            y: 1,
            width: 3,
            height: 2,
        })
        .unwrap()
        .crop(Rect {
            x: 1,
            y: 0,
            width: 1,
            height: 2,
        })
        .unwrap();
    let expr = Expr::Tuple {
        items: vec![
            field(Expr::var("sample"), "source"),
            field(Expr::var("sample"), "bounds"),
        ],
    };
    let output = Type::Tuple(vec![
        Type::Image.field("source").unwrap(),
        Type::Image.field("bounds").unwrap(),
    ]);
    let actual = project(Value::Image(image), expr, output.clone()).await;
    assert_eq!(actual.ty(), output);
    let Value::Tuple(items) = actual else {
        panic!()
    };
    let Value::Option {
        value: Some(source),
        ..
    } = &items[0]
    else {
        panic!()
    };
    assert!(
        source
            .field("id")
            .unwrap()
            .field("session")
            .unwrap()
            .equal(&Value::Str(u64::MAX.to_string()))
    );
    assert!(source.field("width").unwrap().equal(&Value::Int(4)));
    assert!(
        source
            .field("received_ns")
            .unwrap()
            .equal(&Value::Str(u64::MAX.to_string()))
    );
    assert!(items[1].field("x").unwrap().equal(&Value::Int(2)));
    assert!(items[1].field("y").unwrap().equal(&Value::Int(1)));
    assert!(items[1].field("width").unwrap().equal(&Value::Int(1)));
    let none = project(
        Value::Image(Image::new(1, 1, vec![0; 3]).unwrap()),
        field(Expr::var("sample"), "source"),
        Type::Image.field("source").unwrap(),
    )
    .await;
    assert!(matches!(none, Value::Option { value: None, .. }));
}
