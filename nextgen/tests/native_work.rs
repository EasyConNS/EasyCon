use easycon_sense::{
    ir::{
        Definition, Document, Expr, Flow, FlowErrorKind, FlowOutcome, IR_VERSION, Literal,
        NamedExpr, Node, ParMode, SourceKind, SourceRef, Symbol, SymbolKind, TraceKind, Type,
        Value,
    },
    native_work::WorkerError,
    primitives::{ErrorKind, PrimitiveCall, PrimitiveHost, PrimitiveOutput, Recovery},
    run::RunBindings,
    vision::{
        Image, MatchConfig, OcrBackend, OcrBackendKind, OcrBox, OcrResult, Point, Rect,
        TemplateBackendKind, TemplateMatch, TemplateMatcher, VisionError,
        worker::{NativeMatcher, NativeOcr},
    },
};
use std::{
    cell::Cell,
    collections::BTreeMap,
    rc::Rc,
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, ThreadId},
    time::{Duration, Instant},
};
use tokio_util::sync::CancellationToken;

struct Ocr {
    calls: Rc<Cell<u32>>,
    owner: ThreadId,
    saw_cancel: Arc<AtomicBool>,
    finished: Arc<AtomicBool>,
    dropped: Arc<Mutex<Option<ThreadId>>>,
    panic: bool,
}
impl Drop for Ocr {
    fn drop(&mut self) {
        *self.dropped.lock().unwrap() = Some(thread::current().id());
    }
}
impl OcrBackend for Ocr {
    fn kind(&self) -> OcrBackendKind {
        OcrBackendKind::Tesseract
    }
    fn recognize(
        &mut self,
        image: &Image,
        _: Option<Rect>,
        token: &CancellationToken,
    ) -> Result<OcrResult, VisionError> {
        assert_eq!(self.owner, thread::current().id());
        assert_ne!(self.calls.get(), u32::MAX);
        if self.panic {
            panic!("provider panicked");
        }
        if self.calls.replace(self.calls.get() + 1) == 0 {
            let deadline = Instant::now() + Duration::from_secs(2);
            while !token.is_cancelled() && Instant::now() < deadline {
                thread::sleep(Duration::from_millis(1));
            }
            self.saw_cancel
                .store(token.is_cancelled(), Ordering::SeqCst);
        }
        self.finished.store(true, Ordering::SeqCst);
        let region = image.bounds();
        Ok(OcrResult {
            source: image.source().cloned(),
            region,
            kind: self.kind(),
            text: "ok".into(),
            boxes: vec![OcrBox {
                text: "ok".into(),
                confidence: Some(1.0),
                polygon: [
                    Point { x: 0.0, y: 0.0 },
                    Point {
                        x: region.width as f32,
                        y: 0.0,
                    },
                    Point {
                        x: region.width as f32,
                        y: region.height as f32,
                    },
                    Point {
                        x: 0.0,
                        y: region.height as f32,
                    },
                ],
            }],
            profile: "thread-local test".into(),
        })
    }
}

fn named(name: &str, expr: Expr) -> NamedExpr {
    NamedExpr {
        name: name.into(),
        expr,
    }
}
fn text(text: &str) -> Expr {
    Expr::Literal {
        value: Literal::Str(text.into()),
    }
}
fn call(id: &str, name: &str, args: Vec<NamedExpr>) -> Node {
    Node::new(
        id,
        Flow::Primitive {
            name: name.into(),
            version: 1,
            args,
        },
    )
}
fn recognition() -> Node {
    let region = Type::Record(
        ["x", "y", "width", "height"]
            .into_iter()
            .map(|name| (name.into(), Type::Int))
            .collect(),
    );
    Node::new(
        "recognition_text",
        Flow::Pipe {
            source: Box::new(call(
                "recognition",
                "ocr",
                vec![
                    named(
                        "kind",
                        Expr::Literal {
                            value: Literal::Symbol(
                                Symbol::new(SymbolKind::OcrKind, "Tesseract").unwrap(),
                            ),
                        },
                    ),
                    named(
                        "engine",
                        Expr::Literal {
                            value: Literal::Source(SourceRef {
                                kind: SourceKind::Ocr,
                                binding: "ocr".into(),
                            }),
                        },
                    ),
                    named("image", Expr::var("image")),
                    named("region", Expr::None { element: region }),
                ],
            )),
            sink: Box::new(Node::new(
                "text",
                Flow::Eval {
                    expr: Expr::Field {
                        base: Box::new(Expr::var("$prev")),
                        name: "text".into(),
                    },
                },
            )),
        },
    )
}
fn document(body: Node) -> Document {
    Document {
        ir_version: IR_VERSION.into(),
        entry: "main".into(),
        definitions: BTreeMap::from([(
            "main".into(),
            Definition {
                params: BTreeMap::from([("image".into(), Type::Image)]),
                output: Type::Str,
                body,
            },
        )]),
    }
}

async fn run_native(body: Node, panic: bool) {
    let lifetime = CancellationToken::new();
    let saw_cancel = Arc::new(AtomicBool::new(false));
    let finished = Arc::new(AtomicBool::new(false));
    let dropped = Arc::new(Mutex::new(None));
    let (cancel, done, disposed) = (saw_cancel.clone(), finished.clone(), dropped.clone());
    let mut ocr = NativeOcr::spawn(
        move || {
            Ok(Box::new(Ocr {
                calls: Rc::new(Cell::new(0)),
                owner: thread::current().id(),
                saw_cancel: cancel,
                finished: done,
                dropped: disposed,
                panic,
            }))
        },
        &lifetime,
    )
    .await
    .unwrap();
    let mut bindings = RunBindings::default();
    bindings.ocr.insert("ocr".into(), &mut ocr);
    let image = Image::new(2, 2, vec![0; 12]).unwrap();
    let result = document(body)
        .validate()
        .unwrap()
        .execute(
            bindings,
            BTreeMap::from([("image".into(), Value::Image(image.clone()))]),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    if panic {
        let FlowOutcome::Failure(error) = &result.outcome else {
            panic!("{:?}", result.outcome)
        };
        assert_eq!(error.recovery(), Recovery::RunFatal);
        assert_eq!(
            error.primitive.as_ref().unwrap().kind(),
            ErrorKind::InternalError
        );
        assert!(
            !result
                .trace
                .iter()
                .any(|event| event.node_id.as_deref() == Some("handler"))
        );
        assert!(matches!(
            ocr.shutdown().await,
            Err(VisionError::Worker(WorkerError::Panicked))
        ));
    } else {
        assert!(
            saw_cancel.load(Ordering::SeqCst),
            "deadline/race was not polled during native work"
        );
        assert!(
            finished.load(Ordering::SeqCst),
            "run returned before native work drained"
        );
        match result.outcome {
            FlowOutcome::Failure(error) => assert_eq!(error.kind, FlowErrorKind::Timeout),
            FlowOutcome::Success(Value::Str(value)) => assert_eq!(value, "timer"),
            other => panic!("{other:?}"),
        }
        assert!(
            result
                .trace
                .iter()
                .any(|event| event.node_id.as_deref() == Some("recognition")
                    && matches!(event.kind, TraceKind::NodeCancelled { .. }))
        );
        assert_eq!(
            ocr.recognize_async(&image, None, &CancellationToken::new())
                .await
                .unwrap()
                .text,
            "ok"
        );
        ocr.shutdown().await.unwrap();
    }
    assert!(
        dropped
            .lock()
            .unwrap()
            .is_some_and(|id| id != thread::current().id())
    );
}

#[tokio::test]
async fn ir_timeout_cancels_native_work_without_blocking_its_scheduler_and_drains_it() {
    run_native(
        Node::new(
            "timeout",
            Flow::Timeout {
                duration_ms: Expr::int(30),
                body: Box::new(recognition()),
            },
        ),
        false,
    )
    .await;
}

#[tokio::test]
async fn ir_race_accepts_the_timer_then_cancels_and_drains_native_recognition() {
    run_native(
        Node::new(
            "race",
            Flow::Par {
                mode: ParMode::Race,
                branches: vec![
                    recognition(),
                    Node::new(
                        "timer_branch",
                        Flow::Seq {
                            steps: vec![
                                call("timer", "sleep", vec![named("duration_ms", Expr::int(30))]),
                                Node::new(
                                    "timer_result",
                                    Flow::Eval {
                                        expr: text("timer"),
                                    },
                                ),
                            ],
                        },
                    ),
                ],
            },
        ),
        false,
    )
    .await;
}

#[tokio::test]
async fn worker_panic_is_fatal_and_cannot_be_caught_as_a_recognition_error() {
    run_native(
        Node::new(
            "catch",
            Flow::Catch {
                body: Box::new(recognition()),
                bind_error: Some("error".into()),
                handler: Box::new(Node::new(
                    "handler",
                    Flow::Eval {
                        expr: text("recovered"),
                    },
                )),
            },
        ),
        true,
    )
    .await;
}

#[tokio::test]
async fn managed_matcher_keeps_primitive_validation_and_sync_api_compatibility() {
    struct Matcher(Rc<Cell<usize>>);
    impl TemplateMatcher for Matcher {
        fn kind(&self) -> TemplateBackendKind {
            TemplateBackendKind::OpenCv5
        }
        fn match_template(
            &mut self,
            image: &Image,
            template: &Image,
            config: MatchConfig,
            _: &CancellationToken,
        ) -> Result<TemplateMatch, VisionError> {
            self.0.set(self.0.get() + 1);
            Ok(TemplateMatch {
                source: image.source().cloned(),
                search: image.bounds(),
                bounds: template.bounds(),
                mode: config.mode,
                raw_score: 0.0,
                confidence: config.mode.confidence(0.0)?,
                implementation: "local test".into(),
            })
        }
    }
    let parent = CancellationToken::new();
    let mut matcher =
        NativeMatcher::spawn(|| Ok(Box::new(Matcher(Rc::new(Cell::new(0))))), &parent)
            .await
            .unwrap();
    let image = Image::new(2, 2, vec![0; 12]).unwrap();
    let mut host = PrimitiveHost::default();
    host.matchers.insert("matcher".into(), &mut matcher);
    let result = host
        .execute(
            PrimitiveCall::MatchTemplate {
                matcher: "matcher",
                image: &image,
                template: &image,
                config: MatchConfig::default(),
            },
            &parent,
        )
        .await
        .unwrap();
    assert!(matches!(result, PrimitiveOutput::Match(value) if value.confidence == Some(1.0)));
    drop(host);
    assert_eq!(
        matcher
            .match_template(&image, &image, MatchConfig::default(), &parent)
            .unwrap()
            .confidence,
        Some(1.0)
    );
    matcher.shutdown().await.unwrap();
}

#[cfg(all(feature = "ocr-tesseract", feature = "ocr-ppocr"))]
#[tokio::test]
#[ignore = "requires native OpenCV DNN, Tesseract and workflow-supplied OCR models"]
async fn real_native_workers_load_and_reuse_opencv_and_workflow_supplied_ocr_models() {
    use easycon_sense::primitives::ImageInput;
    use easycon_sense::vision::{OcrConfig, PpOcrConfig, TesseractConfig};
    let env = |name| std::env::var_os(name).expect(name).into();
    let configs = [
        OcrConfig::Tesseract(TesseractConfig {
            runtime_library: env("EASYCON_TESSERACT_LIBRARY"),
            data_path: env("EASYCON_TESSDATA"),
            language: "eng+chi_sim".into(),
            page_segmentation: 6,
        }),
        OcrConfig::PpOcr(PpOcrConfig::new(
            env("EASYCON_PPOCR_REC"),
            env("EASYCON_PPOCR_DICT"),
        )),
    ];
    let pixels = image::load_from_memory(include_bytes!("fixtures/ocr-mixed.png"))
        .unwrap()
        .to_rgb8();
    let frame = easycon_sense::video::VideoFrame {
        id: easycon_sense::SampleId {
            source: "ocr-fixture".into(),
            session: easycon_sense::SessionId(1),
            sequence: 7,
        },
        time: easycon_sense::SampleTime {
            received_ns: 30,
            source_ns: Some(20),
        },
        format: easycon_sense::video::VideoFormat {
            width: pixels.width(),
            height: pixels.height(),
            frames_per_second: 30,
            encoding: "RGB3".into(),
        },
        rgb: pixels.into_raw().into(),
    };
    let image = Image::from_frame(&frame).unwrap();
    let parent = CancellationToken::new();
    let mut matcher = NativeMatcher::open_opencv(None, &parent).await.unwrap();
    for config in configs {
        let mut ocr = NativeOcr::open(config, &parent).await.unwrap();
        let kind = ocr.kind();
        let mut host = PrimitiveHost::default();
        host.ocr.insert("ocr".into(), &mut ocr);
        for _ in 0..2 {
            let (matched, recognized) = tokio::join!(
                matcher.match_template_async(&image, &image, MatchConfig::default(), &parent),
                host.execute(
                    PrimitiveCall::Ocr {
                        kind,
                        engine: "ocr",
                        image: ImageInput::Frame(&frame),
                        region: None,
                    },
                    &parent
                ),
            );
            assert!(matched.unwrap().confidence.unwrap() > 0.999);
            let PrimitiveOutput::Ocr(result) = recognized.unwrap() else {
                panic!()
            };
            assert_eq!(
                result.text.split_whitespace().collect::<String>(),
                "自动化测试EasyCon123"
            );
            assert_eq!(result.kind, kind);
            assert_eq!(result.source.as_ref().unwrap().id, frame.id);
            assert!(!result.boxes.is_empty());
            assert!(
                result
                    .confidence()
                    .is_some_and(|score| (0.0..=1.0).contains(&score))
            );
        }
        drop(host);
        ocr.shutdown().await.unwrap();
    }
    matcher.shutdown().await.unwrap();
}
