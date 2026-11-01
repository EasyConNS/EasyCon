//! Opt-in real-device regression of the production ccap video backend.
use std::{
    collections::BTreeMap,
    sync::Arc,
    time::{Duration, Instant},
};

use easycon_sense::{
    CaptureClock, CaptureError, SampleId,
    ir::{
        Definition, Document, Expr, Flow, FlowErrorKind, FlowOutcome, IR_VERSION, Literal,
        NamedExpr, Node, SourceKind, SourceRef, TraceKind, Type, Value,
    },
    primitives::{PrimitiveCall, PrimitiveHost, PrimitiveOutput},
    run::RunBindings,
    video::{VideoFrame, VideoReader, VideoSession},
    vision::{Image, MatchConfig, Rect, worker::NativeMatcher},
};
use tokio_util::sync::CancellationToken;

use super::*;

async fn capture(
    reader: &mut VideoReader,
    after: Option<&SampleId>,
    token: &CancellationToken,
) -> Result<Arc<VideoFrame>> {
    Ok(tokio::time::timeout(Duration::from_secs(5), reader.capture(after, token)).await??)
}

fn capture_workflow() -> Document {
    let source = Expr::Literal {
        value: Literal::Source(SourceRef {
            kind: SourceKind::Video,
            binding: "camera".into(),
        }),
    };
    let capture = Node::new(
        "capture",
        Flow::Primitive {
            name: "capture".into(),
            version: 1,
            args: vec![
                NamedExpr {
                    name: "source".into(),
                    expr: source,
                },
                NamedExpr {
                    name: "after".into(),
                    expr: Expr::var("after"),
                },
            ],
        },
    );
    let image = Node::new(
        "image",
        Flow::Eval {
            expr: Expr::Call {
                name: "image_from_frame".into(),
                version: 1,
                args: vec![Expr::var("$prev")],
            },
        },
    );
    Document {
        ir_version: IR_VERSION.into(),
        entry: "main".into(),
        definitions: BTreeMap::from([(
            "main".into(),
            Definition {
                params: BTreeMap::from([
                    ("after".into(), Type::Option(Box::new(Type::SampleId))),
                    ("timeout_ms".into(), Type::Int),
                ]),
                output: Type::Image,
                body: Node::new(
                    "timeout",
                    Flow::Timeout {
                        duration_ms: Expr::var("timeout_ms"),
                        body: Box::new(Node::new(
                            "pipe",
                            Flow::Pipe {
                                source: Box::new(capture),
                                sink: Box::new(image),
                            },
                        )),
                    },
                ),
            },
        )]),
    }
}

fn workflow_params(after: Option<SampleId>, timeout_ms: i64) -> BTreeMap<String, Value> {
    BTreeMap::from([
        (
            "after".into(),
            Value::Option {
                element: Type::SampleId,
                value: after.map(|id| Box::new(Value::SampleId(id))),
            },
        ),
        ("timeout_ms".into(), Value::Int(timeout_ms)),
    ])
}

#[test]
fn hardware_workflow_roundtrips_and_validates_offline() {
    let document = capture_workflow();
    let decoded = Document::from_json(&document.to_json().unwrap()).unwrap();
    assert_eq!(decoded, document);
    decoded.validate().unwrap();
}

#[tokio::test(flavor = "current_thread")]
#[ignore = "opens OBS Virtual Camera; run explicitly with --ignored --nocapture"]
async fn obs_capture_card_contract() -> Result<()> {
    let device = std::env::var("CCAP_TEST_DEVICE").unwrap_or_else(|_| "OBS Virtual Camera".into());
    let token = CancellationToken::new();
    let clock = CaptureClock::default();
    let document = capture_workflow();
    let checked = Document::from_json(&document.to_json()?)?.validate()?;
    let mut old_cursor: Option<SampleId> = None;
    for cycle in 1..=3 {
        let mut session = VideoSession::open_default(&format!("id:{device}"), clock.clone())?;
        println!(
            "{}",
            json!({"event":"contract_open", "device":device, "format":session.format()})
        );
        let mut reader = session.subscribe();
        let mut second = session.subscribe();
        if let Some(cursor) = &old_cursor {
            assert!(matches!(
                reader.capture(Some(cursor), &token).await,
                Err(CaptureError::SourceChanged)
            ));
        }
        let first = capture(&mut reader, None, &token).await?;
        assert_eq!(first.format, *session.format());
        assert_eq!(
            first.rgb.len(),
            first.format.width as usize * first.format.height as usize * 3
        );
        let retained = first.rgb.to_vec();
        let mut last = first.clone();
        for _ in 1..30 {
            let next = capture(&mut reader, Some(&last.id), &token).await?;
            assert!(next.id.sequence > last.id.sequence);
            assert_eq!(next.id.session, first.id.session);
            assert!(next.time.received_ns > last.time.received_ns);
            assert!(next.time.source_ns > last.time.source_ns);
            assert_eq!(next.format, first.format);
            assert_eq!(next.rgb.len(), retained.len());
            last = next;
        }
        let peer = capture(&mut second, Some(&last.id), &token).await?;
        assert!(peer.id.sequence > last.id.sequence);
        assert_eq!(peer.id.session, first.id.session);

        // Force a pending read, so cancellation cannot pass by returning a cached frame.
        let future_cursor = SampleId {
            sequence: u64::MAX,
            ..last.id.clone()
        };
        let cancelled = CancellationToken::new();
        let start = Instant::now();
        let (result, ()) = tokio::time::timeout(Duration::from_secs(2), async {
            tokio::join!(second.capture(Some(&future_cursor), &cancelled), async {
                tokio::task::yield_now().await;
                cancelled.cancel();
            })
        })
        .await?;
        assert!(matches!(result, Err(CaptureError::Cancelled)));
        assert!(start.elapsed() < Duration::from_secs(1));
        let cancel_ms = start.elapsed().as_millis();

        let mut host = PrimitiveHost::default();
        host.videos.insert("camera".into(), &mut reader);
        let output = host
            .execute(
                PrimitiveCall::Capture {
                    source: "camera",
                    after: Some(&peer.id),
                },
                &token,
            )
            .await?;
        let PrimitiveOutput::Frame(frame) = output else {
            panic!("Capture returned wrong type")
        };
        assert!(frame.id.sequence > peer.id.sequence);
        let image = Image::from_frame(&frame)?;
        let output = host
            .execute(
                PrimitiveCall::RegionStatistics {
                    image: &image,
                    region: None,
                },
                &token,
            )
            .await?;
        let PrimitiveOutput::RegionStatistics(stats) = output else {
            panic!("wrong statistics output")
        };
        assert_eq!(
            stats.pixel_count,
            u64::from(image.width()) * u64::from(image.height())
        );
        assert!(
            stats
                .rgb_mean
                .iter()
                .all(|mean| mean.is_finite() && (0.0..=255.0).contains(mean))
        );
        assert!(
            stats
                .rgb_max
                .iter()
                .zip(stats.rgb_min)
                .any(|(max, min)| *max > min)
        );

        let mut bindings = RunBindings::default();
        bindings
            .videos
            .insert("camera".into(), Box::new(session.subscribe()));
        let execution = checked
            .execute(bindings, workflow_params(None, 5000), &token)
            .await?;
        let FlowOutcome::Success(Value::Image(ir_image)) = &execution.outcome else {
            panic!("IR failed: {:?}", execution.outcome)
        };
        assert_eq!(
            (ir_image.width(), ir_image.height()),
            (image.width(), image.height())
        );
        assert!(!execution.cleanup.has_failures());
        assert_eq!(
            execution
                .trace
                .iter()
                .filter(|event| matches!(event.kind, TraceKind::NodeSucceeded { .. }))
                .count(),
            4
        );
        let mut bindings = RunBindings::default();
        bindings
            .videos
            .insert("camera".into(), Box::new(session.subscribe()));
        let timed_out = checked
            .execute(bindings, workflow_params(Some(future_cursor), 20), &token)
            .await?;
        assert!(
            matches!(&timed_out.outcome, FlowOutcome::Failure(error) if error.kind == FlowErrorKind::Timeout)
        );
        assert!(!timed_out.cleanup.has_failures());
        assert!(
            timed_out
                .trace
                .iter()
                .any(|event| event.node_id.as_deref() == Some("capture")
                    && matches!(event.kind, TraceKind::NodeCancelled { .. }))
        );
        // Run-scope cleanup only closes its subscription, not the physical source.
        let after_ir = capture(&mut reader, Some(&frame.id), &token).await?;
        assert!(after_ir.id.sequence > frame.id.sequence);

        let stopped = Instant::now();
        if cycle < 3 {
            session.stop()?;
            session.stop()?;
        }
        drop(session);
        assert!(stopped.elapsed() < Duration::from_secs(3));
        assert!(matches!(
            reader.capture(None, &token).await,
            Err(CaptureError::Stopped)
        ));
        assert!(matches!(
            second.capture(None, &token).await,
            Err(CaptureError::Stopped)
        ));
        assert_eq!(&*first.rgb, retained);
        old_cursor = Some(last.id.clone());
        println!(
            "{}",
            json!({"event":"contract_cycle_pass", "cycle":cycle, "fresh_frames":30, "session":first.id.session.0,
            "last_sequence":last.id.sequence, "reader_cancel_ms":cancel_ms, "stop_join_ms":stopped.elapsed().as_millis(),
            "rgb_mean":stats.rgb_mean, "ir_nodes_succeeded":4, "ir_timeout_cancelled_capture":true})
        );
    }
    println!(
        "{}",
        json!({"event":"contract_pass", "device":device, "cycles":3, "fresh_frames":90})
    );
    Ok(())
}

#[tokio::test(flavor = "current_thread")]
#[ignore = "opens OBS and native OpenCV >=5; requires EASYCON_OPENCV_DIR"]
async fn obs_opencv_capture_card_match() -> Result<()> {
    let device = std::env::var("CCAP_TEST_DEVICE").unwrap_or_else(|_| "OBS Virtual Camera".into());
    let mut session = VideoSession::open_default(&format!("id:{device}"), CaptureClock::default())?;
    let token = CancellationToken::new();
    let mut reader = session.subscribe();
    let frame = capture(&mut reader, None, &token).await?;
    let image = Image::from_frame(&frame)?;
    let target = Rect {
        x: image.width() / 2 - 96,
        y: image.height() / 2 - 96,
        width: 192,
        height: 192,
    };
    let template = image.crop(target)?;
    let mut matcher = NativeMatcher::open_opencv(None, &token).await?;
    let result: Result<()> = async {
        let mut host = PrimitiveHost::default();
        host.matchers.insert("opencv".into(), &mut matcher);
        let PrimitiveOutput::Match(found) = host.execute(PrimitiveCall::MatchTemplate {
            matcher: "opencv", image: &image, template: &template, config: MatchConfig::default(),
        }, &token).await? else { panic!("wrong matching output") };
        assert!(found.confidence.is_some_and(|confidence| confidence >= 0.999));
        assert_eq!(found.bounds, target);
        assert_eq!(found.source.as_ref().unwrap().id, frame.id);
        println!("{}", json!({"event":"opencv_match_pass", "implementation":found.implementation,
            "confidence":found.confidence, "bounds":[found.bounds.x, found.bounds.y, found.bounds.width, found.bounds.height]}));
        Ok(())
    }.await;
    let shutdown = matcher.shutdown().await;
    session.stop()?;
    shutdown?;
    result
}
