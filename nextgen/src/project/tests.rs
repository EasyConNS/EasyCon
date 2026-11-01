use super::*;
use crate::ir::{FlowOutcome, Type};
use base64::{Engine, engine::general_purpose::STANDARD};
use std::sync::atomic::{AtomicU64, Ordering};

static NEXT: AtomicU64 = AtomicU64::new(0);
struct Project(PathBuf);
impl Project {
    fn new() -> Self {
        let dir = std::env::temp_dir().join(format!(
            "easycon-project-{}-{}",
            std::process::id(),
            NEXT.fetch_add(1, Ordering::Relaxed)
        ));
        std::fs::create_dir_all(dir.join("imglabel")).unwrap();
        Self(dir)
    }
    fn label(&self, json: &str) {
        let bytes =
            std::fs::read(Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/hello.png"))
                .unwrap();
        let mut label: serde_json::Value = serde_json::from_str(json).unwrap();
        label["ImgBase64"] = STANDARD.encode(bytes).into();
        std::fs::write(self.0.join("imglabel/target.il"), label.to_string()).unwrap();
    }
    fn offline(&self, config: &str) -> PathBuf {
        std::fs::write(
            self.0.join("workflow.json"),
            include_str!("../../examples/combinators.ir.json"),
        )
        .unwrap();
        std::fs::write(self.0.join("config.json"), config).unwrap();
        self.0.join("workflow.json")
    }
    fn vision(&self) -> PathBuf {
        let path = self.offline(r#"{"video":"index:0","gamepad":"COM3"}"#);
        std::fs::write(
            &path,
            include_str!("../../examples/capture-match/workflow.json"),
        )
        .unwrap();
        self.label(r#"{"searchMethod":1}"#);
        path
    }
}
impl Drop for Project {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

#[test]
fn legacy_embedded_label_keeps_full_image_roi_and_default_method() {
    let project = Project::new();
    let bytes =
        std::fs::read(Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/hello.png"))
            .unwrap();
    let expected = image::load_from_memory(&bytes).unwrap().to_rgb8();
    let encoded = STANDARD.encode(bytes);
    let json = serde_json::json!({
        "ImgBase64": format!(" {}\r\n", encoded),
        "RangeX": 12, "RangeY": 34, "RangeWidth": 4096, "RangeHeight": 4096,
        "TargetX": 200, "TargetY": 100, "TargetWidth": 1, "TargetHeight": 1,
        "UseGrayscale": true, "UseBinary": true, "UseGaussianBlur": true, "UseOther": true
    });
    std::fs::write(
        project.0.join("imglabel/target.IL"),
        format!("\u{feff}{}", json),
    )
    .unwrap();
    let Value::Record(label) = load_image_label(&project.0, "target").unwrap() else {
        panic!()
    };
    assert_eq!(label.len(), 2);
    assert!(
        matches!(&label["image"], Value::Image(image) if image.width() == expected.width() && image.height() == expected.height())
    );
    let Value::Record(config) = &label["config"] else {
        panic!()
    };
    assert!(matches!(&config["mode"], Value::Symbol(s) if s.name() == "CCoeffNormed"));
    let Value::Option {
        value: Some(region),
        ..
    } = &config["region"]
    else {
        panic!()
    };
    assert!(matches!(region.field("x"), Some(Value::Int(12))));
    assert!(matches!(region.field("y"), Some(Value::Int(34))));
    std::fs::rename(project.0.join("imglabel"), project.0.join("ImgLabel")).unwrap();
    assert!(load_image_label(&project.0, "target").is_ok());
}

#[test]
fn legacy_labels_support_all_six_opencv_modes() {
    let project = Project::new();
    for (method, mode) in [
        "SqDiff",
        "SqDiffNormed",
        "CCorr",
        "CCorrNormed",
        "CCoeff",
        "CCoeffNormed",
    ]
    .into_iter()
    .enumerate()
    {
        project.label(&format!(r#"{{"searchMethod":{method}}}"#));
        let Value::Record(label) = load_image_label(&project.0, "target").unwrap() else {
            panic!()
        };
        let Value::Record(config) = &label["config"] else {
            panic!()
        };
        assert!(matches!(&config["mode"], Value::Symbol(s) if s.name() == mode));
        assert!(matches!(
            &config["region"],
            Value::Option { value: None, .. }
        ));
    }
}

#[test]
fn used_labels_reject_unsupported_methods_bad_images_and_invalid_regions() {
    let project = Project::new();
    for config in [
        r#"{"searchMethod":14}"#,
        r#"{"searchMethod":107}"#,
        r#"{"RangeX":-1,"RangeWidth":4096,"RangeHeight":4096}"#,
        r#"{"RangeWidth":0,"RangeHeight":4096}"#,
        r#"{"RangeX":4294967295,"RangeWidth":4096,"RangeHeight":4096}"#,
        r#"{"RangeWidth":1,"RangeHeight":1}"#,
    ] {
        project.label(config);
        assert!(load_image_label(&project.0, "target").is_err(), "{config}");
    }
    std::fs::write(
        project.0.join("imglabel/target.il"),
        r#"{"ImgBase64":"invalid base64"}"#,
    )
    .unwrap();
    assert!(load_image_label(&project.0, "target").is_err());
    std::fs::write(
        project.0.join("imglabel/target.il"),
        r#"{"ImgBase64":"aGVsbG8="}"#,
    )
    .unwrap();
    assert!(load_image_label(&project.0, "target").is_err());
    project.label("{}");
    std::fs::remove_file(project.0.join("imglabel/target.il")).unwrap();
    assert!(load_image_label(&project.0, "target").is_err());
}

#[test]
fn labels_require_the_legacy_schema_without_external_reference_extensions() {
    let project = Project::new();
    for json in [
        r#"{"image":"target.png","config":"target.json"}"#,
        r#"{"searchMethod":"SqDiffNormed","ImgBase64":""}"#,
        r#"{"searchMethod":1,"RangeWidth":"100"}"#,
    ] {
        std::fs::write(project.0.join("imglabel/target.il"), json).unwrap();
        assert!(labels::scan(&project.0).is_err(), "{json}");
    }
}

#[test]
fn vision_workflows_scan_all_descriptors_but_only_resolve_used_assets() {
    let project = Project::new();
    let path = project.vision();
    std::fs::write(
        project.0.join("imglabel/unused.IL"),
        r#"{"searchMethod":107}"#,
    )
    .unwrap();
    std::fs::write(
        project.0.join("imglabel/unused-image.il"),
        r#"{"ImgBase64":"invalid but unused image"}"#,
    )
    .unwrap();
    assert!(PreparedProject::load(&path).is_ok());
    std::fs::write(project.0.join("imglabel/unused.IL"), "invalid JSON").unwrap();
    assert!(PreparedProject::load(&path).is_err());
    project.offline("{}");
    assert!(PreparedProject::load(&path).is_ok());
}

#[test]
fn duplicate_label_stems_are_rejected_on_case_sensitive_filesystems() {
    let project = Project::new();
    project.label("{}");
    std::fs::write(project.0.join("imglabel/target.IL"), "{}").unwrap();
    if std::fs::read_dir(project.0.join("imglabel"))
        .unwrap()
        .count()
        == 2
    {
        assert!(labels::scan(&project.0).is_err());
    }
}

#[test]
fn project_preflight_reads_adjacent_optional_config_and_rejects_bad_devices() {
    let project = Project::new();
    let path = project.offline("{}");
    let prepared = PreparedProject::load(&path).unwrap();
    assert_eq!(prepared.directory, project.0.canonicalize().unwrap());
    let report = prepared.run_blocking(&RunControl::new());
    assert!(report.cleanup.is_empty());
    assert!(matches!(
        report.execution.unwrap().outcome,
        FlowOutcome::Success(Value::Int(42))
    ));
    std::fs::remove_file(project.0.join("config.json")).unwrap();
    assert!(PreparedProject::load(&project.0).is_ok());
    for config in [
        r#"{"workflow":"workflow.json"}"#,
        r#"{"gamepad":" "}"#,
        r#"{"video":"index:bad"}"#,
        r#"{"video":{}}"#,
        r#"{"params":{}}"#,
    ] {
        assert!(
            PreparedProject::load(project.offline(config)).is_err(),
            "{config}"
        );
    }
    let path = project.vision();
    std::fs::write(project.0.join("config.json"), "{}").unwrap();
    assert!(PreparedProject::load(&path).is_err());
    std::fs::write(project.0.join("config.json"), r#"{"video":"index:0"}"#).unwrap();
    assert!(PreparedProject::load(&path).is_err());
}

#[test]
fn capture_example_preflights_without_devices_or_native_libraries() {
    let project = PreparedProject::load(
        Path::new(env!("CARGO_MANIFEST_DIR")).join("examples/capture-match/workflow.json"),
    )
    .unwrap();
    assert_eq!(
        project.params["target"].ty().field("image"),
        Some(Type::Image)
    );
    assert_eq!(project.params.len(), 4);
    assert!(project.uses_opencv);
}

use crate::{
    SampleId, SampleTime, SessionId,
    ir::{Finite, RunStopReason},
    primitives::{CaptureFuture, VideoInput},
    video::{VideoFormat, VideoFrame},
    vision::{Image, MatchConfig, TemplateBackendKind, TemplateMatch},
};
use easycon_device::{Button, Delivery, DeviceError, Report, SessionInfo, WriteReceipt};
use std::{collections::VecDeque, sync::Mutex, time::Instant};
use tokio::sync::Notify;
use tokio_util::sync::CancellationToken;

struct Frames(u64);
impl VideoInput for Frames {
    fn capture<'a>(
        &'a mut self,
        after: Option<&'a SampleId>,
        _: &'a CancellationToken,
    ) -> CaptureFuture<'a, Arc<VideoFrame>> {
        Box::pin(async move {
            assert_eq!(after.map(|id| id.sequence), (self.0 > 0).then_some(self.0));
            self.0 += 1;
            Ok(Arc::new(VideoFrame {
                id: SampleId {
                    source: "fake".into(),
                    session: SessionId(1),
                    sequence: self.0,
                },
                time: SampleTime {
                    received_ns: self.0,
                    source_ns: None,
                },
                format: VideoFormat {
                    width: 1,
                    height: 1,
                    frames_per_second: 30,
                    encoding: "RAW".into(),
                },
                rgb: vec![0, 0, 0].into(),
            }))
        })
    }
}

struct Scores(VecDeque<f64>);
impl TemplateMatcher for Scores {
    fn kind(&self) -> TemplateBackendKind {
        TemplateBackendKind::OpenCv5
    }
    fn match_template(
        &mut self,
        image: &Image,
        _: &Image,
        config: MatchConfig,
        _: &CancellationToken,
    ) -> Result<TemplateMatch, VisionError> {
        let score = self.0.pop_front().expect("unexpected extra match");
        Ok(TemplateMatch {
            source: image.source().cloned(),
            search: image.bounds(),
            bounds: image.bounds(),
            mode: config.mode,
            raw_score: 1.0 - score,
            confidence: Some(score),
            implementation: "fake".into(),
        })
    }
}

struct Pad {
    info: SessionInfo,
    state: Report,
    reports: Arc<Mutex<Vec<Report>>>,
    pressed: Arc<Notify>,
    disconnects: Arc<AtomicU64>,
    fail_disconnect: bool,
}
impl Pad {
    fn new() -> Self {
        Self {
            info: SessionInfo {
                port: "fake".into(),
                backend: "fake",
                baud_rate: 0,
                session: 1,
            },
            state: Report::default(),
            reports: Arc::default(),
            pressed: Arc::default(),
            disconnects: Arc::default(),
            fail_disconnect: false,
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
    ) -> Result<WriteReceipt, DeviceError> {
        if token.is_cancelled() {
            return Err(DeviceError::Cancelled);
        }
        self.state = report;
        let mut reports = self.reports.lock().unwrap();
        reports.push(report);
        if report.buttons != 0 {
            self.pressed.notify_one();
        }
        Ok(WriteReceipt {
            session: 1,
            sequence: reports.len() as u64,
            delivery: Delivery::Written,
            written_at: Instant::now(),
        })
    }
    fn disconnect(&mut self) -> Result<(), DeviceError> {
        self.disconnects.fetch_add(1, Ordering::Relaxed);
        if self.fail_disconnect {
            Err(DeviceError::CleanupFailed {
                error: Box::new(DeviceError::Closed),
                previous: None,
            })
        } else {
            Ok(())
        }
    }
}

async fn run_capture_example(scores: Vec<f64>, cancel: bool) -> (Execution, Vec<Report>) {
    let mut project = PreparedProject::load(
        Path::new(env!("CARGO_MANIFEST_DIR")).join("examples/capture-match/workflow.json"),
    )
    .unwrap();
    let Value::Record(target) = project.params.get_mut("target").unwrap() else {
        panic!()
    };
    target.insert(
        "image".into(),
        Value::Image(Image::new(1, 1, vec![0; 3]).unwrap()),
    );
    let Value::Record(config) = target.get_mut("config").unwrap() else {
        panic!()
    };
    let Value::Option { value, .. } = config.get_mut("region").unwrap() else {
        panic!()
    };
    *value = None;
    let mut matcher = NativeMatcher::spawn(
        move || Ok(Box::new(Scores(scores.into()))),
        &CancellationToken::new(),
    )
    .await
    .unwrap();
    let mut pad = Pad::new();
    let control = RunControl::new();
    let stop_task = if cancel {
        let pressed = pad.pressed.clone();
        let control = control.clone();
        Some(tokio::spawn(async move {
            pressed.notified().await;
            control.request_stop(RunStopReason::UserStop);
        }))
    } else {
        None
    };
    let mut bindings = RunBindings::default();
    bindings.videos.insert("video".into(), Box::new(Frames(0)));
    bindings.matchers.insert("opencv".into(), &mut matcher);
    bindings.pads.insert("gamepad".into(), &mut pad);
    let result = project
        .document
        .execute_controlled(bindings, project.params, &control)
        .await
        .unwrap();
    if cancel {
        assert!(
            matches!(result.outcome, FlowOutcome::Cancelled(_)),
            "{:?}",
            result.outcome
        );
    }
    if let Some(task) = stop_task {
        task.await.unwrap();
    }
    matcher.shutdown().await.unwrap();
    assert_eq!(pad.state, Report::default());
    assert!(!result.cleanup.has_failures());
    let reports = pad.reports.lock().unwrap().clone();
    (result, reports)
}

#[tokio::test]
async fn low_low_threshold_stops_with_exactly_two_a_clicks_and_fresh_frames() {
    let (result, reports) = run_capture_example(vec![0.4, 0.89, 0.9], false).await;
    let FlowOutcome::Success(Value::Record(output)) = result.outcome else {
        panic!("{:?}", result.outcome)
    };
    assert!(matches!(output["score"], Value::Float(v) if v == Finite::new(0.9).unwrap()));
    assert_eq!(
        reports
            .iter()
            .filter(|r| r.buttons == Button::A.mask())
            .count(),
        2
    );
    for pair in reports.windows(2) {
        if pair[0].buttons != 0 {
            assert_eq!(pair[1].buttons, 0);
        }
    }
}

#[tokio::test]
async fn stopping_during_hold_releases_the_controller_before_completion() {
    let (result, reports) = run_capture_example(vec![0.4], true).await;
    assert!(matches!(result.outcome, FlowOutcome::Cancelled(_)));
    assert_eq!(reports[0].buttons, Button::A.mask());
    assert_eq!(reports.last().unwrap().buttons, 0);
    assert_eq!(reports.iter().filter(|r| r.buttons != 0).count(), 1);
}

#[tokio::test]
async fn physical_cleanup_continues_after_a_disconnect_failure() {
    let mut first = Pad::new();
    first.fail_disconnect = true;
    let second = Pad::new();
    let disconnected = second.disconnects.clone();
    let resources = Resources {
        pads: BTreeMap::from([
            ("first".into(), Box::new(first) as Box<dyn Gamepad>),
            ("second".into(), Box::new(second) as Box<dyn Gamepad>),
        ]),
        ..Resources::default()
    };
    let errors = resources.shutdown().await;
    assert_eq!(errors.len(), 1);
    assert_eq!(errors[0].0, "first");
    assert_eq!(disconnected.load(Ordering::Relaxed), 1);
}

impl Project {
    fn ocr(&self) -> PathBuf {
        let path = self.offline("{}");
        std::fs::write(&path, include_str!("../../examples/ocr/workflow.json")).unwrap();
        std::fs::create_dir(self.0.join("models")).unwrap();
        for name in ["ppocr.onnx", "eng.traineddata", "chi_sim.traineddata"] {
            std::fs::write(
                self.0.join("models").join(name),
                b"model fixture; preflight never loads native code",
            )
            .unwrap();
        }
        std::fs::write(self.0.join("models/ppocr.txt"), "你\n好\n").unwrap();
        self.label("{}");
        std::fs::rename(
            self.0.join("imglabel/target.il"),
            self.0.join("imglabel/text.IL"),
        )
        .unwrap();
        path
    }
}

#[test]
fn both_ocr_nodes_bind_independent_project_models_without_native_initialization() {
    let project = Project::new();
    let path = project.ocr();
    let prepared = PreparedProject::load(&path).unwrap();
    let models::PreparedOcr::Ppocr(config) = &prepared.ocr["paddle"] else {
        panic!()
    };
    assert_eq!(
        config.recognition_model,
        project.0.join("models/ppocr.onnx").canonicalize().unwrap()
    );
    assert_eq!(
        config.dictionary,
        project.0.join("models/ppocr.txt").canonicalize().unwrap()
    );
    let models::PreparedOcr::Tesseract {
        data_path,
        language,
        page_segmentation,
    } = &prepared.ocr["tess"]
    else {
        panic!()
    };
    assert_eq!(data_path, &project.0.join("models").canonicalize().unwrap());
    assert_eq!(language, "eng+chi_sim");
    assert_eq!(*page_segmentation, 7);
    for name in ["tess", "paddle"] {
        assert!(
            matches!(&prepared.params[name], Value::Source(source) if source.kind == SourceKind::Ocr && source.binding == name)
        );
    }
    assert!(!prepared.uses_opencv); // PP-OCR owns its DNN worker; no template matcher is needed.
}

#[test]
fn ocr_preflight_rejects_missing_or_nonportable_models_and_mismatched_declarations() {
    let project = Project::new();
    let path = project.ocr();
    let original: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
    for model in [
        "../outside.onnx",
        "/models/model.onnx",
        "resources/ppocr.onnx",
        "models/../outside.onnx",
        "models/missing.onnx",
        "models/ppocr.txt",
    ] {
        let mut flow = original.clone();
        flow["ppocr"]["paddle"]["model"] = model.into();
        std::fs::write(&path, flow.to_string()).unwrap();
        assert!(PreparedProject::load(&path).is_err(), "{model}");
    }
    for files in [
        vec![],
        vec!["models/eng.traineddata", "models/eng.traineddata"],
        vec!["models/ppocr.onnx"],
    ] {
        let mut flow = original.clone();
        flow["tesseract"]["tess"]["models"] = serde_json::json!(files);
        std::fs::write(&path, flow.to_string()).unwrap();
        assert!(PreparedProject::load(&path).is_err());
    }
    for edit in [0, 1, 2] {
        let mut flow = original.clone();
        match edit {
            0 => {
                flow["tesseract"].as_object_mut().unwrap().remove("tess");
            }
            1 => {
                flow["ppocr"]["tess"] = flow["ppocr"]["paddle"].clone();
            }
            _ => {
                flow["ppocr"]["paddle"]["backend"] = "tesseract".into();
            }
        }
        std::fs::write(&path, flow.to_string()).unwrap();
        assert!(PreparedProject::load(&path).is_err());
    }
    std::fs::write(&path, original.to_string()).unwrap();
    std::fs::write(project.0.join("models/ppocr.onnx"), []).unwrap();
    assert!(PreparedProject::load(&path).is_err());
}

#[cfg(unix)]
#[test]
fn workflow_models_cannot_resolve_through_symlinks_outside_the_project() {
    let project = Project::new();
    let path = project.ocr();
    std::fs::rename(
        project.0.join("models/ppocr.onnx"),
        project.0.join("outside.onnx"),
    )
    .unwrap();
    std::os::unix::fs::symlink(
        project.0.join("outside.onnx"),
        project.0.join("models/ppocr.onnx"),
    )
    .unwrap();
    assert!(PreparedProject::load(&path).is_err());
}

#[cfg(all(feature = "ocr-tesseract", feature = "ocr-ppocr"))]
#[test]
#[ignore = "requires project models and Tesseract/OpenCV native libraries; see examples/ocr/models"]
fn real_project_workflow_runs_tesseract_and_ppocr_together() {
    let project = PreparedProject::load(
        Path::new(env!("CARGO_MANIFEST_DIR")).join("examples/ocr/workflow.json"),
    )
    .unwrap();
    let report = project.run_blocking(&RunControl::new());
    assert!(report.cleanup.is_empty(), "{:?}", report.cleanup);
    let result = report.execution.unwrap();
    assert!(!result.cleanup.has_failures());
    let FlowOutcome::Success(Value::Record(texts)) = result.outcome else {
        panic!("{:?}", result.outcome)
    };
    for name in ["tesseract", "ppocr"] {
        let Value::Str(text) = &texts[name] else {
            panic!()
        };
        assert_eq!(
            text.split_whitespace().collect::<String>(),
            "自动化测试EasyCon123",
            "{name}"
        );
    }
}
