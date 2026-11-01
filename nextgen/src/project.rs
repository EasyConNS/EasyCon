//! Project-relative assets and an explicitly owned hardware host for arbitrary validated IR.

use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
    sync::Arc,
};

use easycon_device::Gamepad;
use serde::{Deserialize, de::DeserializeOwned};
use thiserror::Error;

use crate::{
    CaptureClock, CaptureError,
    ir::{
        Definition, Document, Execution, Flow, Node, RunControl, SourceKind, SourceRef, Type,
        ValidatedDocument, ValidationError, Value,
    },
    meta::{LogRecord, LogSink, MetaContext},
    run::RunBindings,
    video::{self, VideoSession},
    vision::{
        OcrBackend, TemplateMatcher, VisionError,
        worker::{NativeMatcher, NativeOcr},
    },
};

mod labels;
mod models;
pub use labels::load_image_label;
#[cfg(test)]
mod tests;

#[derive(Debug, Error)]
pub enum HostError {
    #[error("{path}: {source}")]
    Io {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },
    #[error("{path}: {source}")]
    Json {
        path: PathBuf,
        #[source]
        source: serde_json::Error,
    },
    #[error("invalid project configuration: {0}")]
    Config(String),
    #[error(transparent)]
    Validation(#[from] ValidationError),
    #[error(transparent)]
    Vision(#[from] VisionError),
    #[error(transparent)]
    Capture(#[from] CaptureError),
    #[error(transparent)]
    Device(#[from] easycon_device::DeviceError),
}

impl HostError {
    pub fn is_cancelled(&self) -> bool {
        matches!(
            self,
            Self::Capture(CaptureError::Cancelled)
                | Self::Device(easycon_device::DeviceError::Cancelled)
                | Self::Vision(VisionError::Cancelled)
                | Self::Vision(VisionError::Worker(
                    crate::native_work::WorkerError::Cancelled
                ))
        )
    }
}

fn read_json<T: DeserializeOwned>(path: &Path) -> Result<T, HostError> {
    let text = std::fs::read_to_string(path).map_err(|source| HostError::Io {
        path: path.into(),
        source,
    })?;
    serde_json::from_str(text.trim_start_matches('\u{feff}')).map_err(|source| HostError::Json {
        path: path.into(),
        source,
    })
}

#[derive(Default, Deserialize)]
#[serde(deny_unknown_fields)]
struct Config {
    video: Option<String>,
    gamepad: Option<String>,
}

/// Host declarations extend the workflow file without adding backend configuration to core IR.
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Workflow {
    ir_version: String,
    entry: String,
    #[serde(deserialize_with = "crate::ir::unique_map")]
    definitions: BTreeMap<String, Definition>,
    #[serde(default, deserialize_with = "crate::ir::unique_map")]
    tesseract: BTreeMap<String, models::TesseractModel>,
    #[serde(default, deserialize_with = "crate::ir::unique_map")]
    ppocr: BTreeMap<String, models::PpOcrModel>,
}

/// Loading checks IR, label resources and entry arguments without touching hardware/OpenCV.
pub struct PreparedProject {
    pub directory: PathBuf,
    config: Config,
    document: ValidatedDocument,
    params: BTreeMap<String, Value>,
    uses_opencv: bool,
    ocr: BTreeMap<String, models::PreparedOcr>,
}

impl PreparedProject {
    pub fn load(path: impl AsRef<Path>) -> Result<Self, HostError> {
        let path = if path.as_ref().is_dir() {
            path.as_ref().join("workflow.json")
        } else {
            path.as_ref().to_owned()
        };
        let workflow: Workflow = read_json(&path)?;
        let document = Document {
            ir_version: workflow.ir_version,
            entry: workflow.entry,
            definitions: workflow.definitions,
        }
        .validate()?;
        let directory = path
            .parent()
            .filter(|parent| !parent.as_os_str().is_empty())
            .unwrap_or(Path::new("."))
            .to_owned();
        let directory = directory.canonicalize().map_err(|source| HostError::Io {
            path: directory,
            source,
        })?;
        let ocr = models::prepare(&directory, workflow.tesseract, workflow.ppocr)?;
        let config = match read_json::<Config>(&directory.join("config.json")) {
            Ok(config) => config,
            Err(HostError::Io { source, .. }) if source.kind() == std::io::ErrorKind::NotFound => {
                Config::default()
            }
            Err(error) => return Err(error),
        };
        if let Some(device) = &config.video {
            video::validate_device_id(device)?;
        }
        if config
            .gamepad
            .as_ref()
            .is_some_and(|port| port.trim().is_empty())
        {
            return Err(HostError::Config(
                "gamepad must be a nonempty serial port name".into(),
            ));
        }
        let uses_opencv = document
            .document()
            .definitions
            .values()
            .any(|definition| uses_primitive(&definition.body, &["match_template"]));
        let uses_vision = document.document().definitions.values().any(|definition| {
            uses_primitive(
                &definition.body,
                &[
                    "match_template",
                    "ocr",
                    "region_statistics",
                    "image_difference",
                ],
            )
        });
        let labels = if uses_vision {
            labels::scan(&directory)?
        } else {
            BTreeMap::new()
        };
        let mut params = BTreeMap::new();
        let entry = &document.document().definitions[&document.document().entry];
        for (name, ty) in &entry.params {
            let source = match ty {
                Type::Source(SourceKind::Video) => {
                    if config.video.is_none() {
                        return Err(HostError::Config(format!(
                            "entry parameter {name} requires video in config.json"
                        )));
                    }
                    Some((SourceKind::Video, "video"))
                }
                Type::Source(SourceKind::Gamepad) => {
                    if config.gamepad.is_none() {
                        return Err(HostError::Config(format!(
                            "entry parameter {name} requires gamepad in config.json"
                        )));
                    }
                    Some((SourceKind::Gamepad, "gamepad"))
                }
                Type::Source(SourceKind::TemplateMatcher) => {
                    Some((SourceKind::TemplateMatcher, "opencv"))
                }
                Type::Source(SourceKind::Ocr) => {
                    if !ocr.contains_key(name) {
                        return Err(HostError::Config(format!(
                            "entry parameter {name} requires a matching workflow tesseract or ppocr declaration"
                        )));
                    }
                    Some((SourceKind::Ocr, name.as_str()))
                }
                Type::Record(_) if uses_vision => None,
                _ => {
                    return Err(HostError::Config(format!(
                        "entry parameter {name}: use a video/gamepad/template-matcher/ocr source or an imglabel record; define ordinary values in workflow expressions"
                    )));
                }
            };
            let value = match source {
                Some((kind, binding)) => Value::Source(SourceRef {
                    kind,
                    binding: binding.into(),
                }),
                None => labels
                    .get(name)
                    .ok_or_else(|| {
                        HostError::Config(format!("imglabel: entry label {name:?} not found"))
                    })?
                    .load()?,
            };
            params.insert(name.clone(), value);
        }
        document.validate_params(&params)?;
        Ok(Self {
            directory,
            config,
            document,
            params,
            uses_opencv,
            ocr,
        })
    }

    /// Run on an owned blocking thread. This function waits for all native cleanup.
    /// The CLI keeps Ctrl+C on its outer runtime and always joins this host.
    pub fn run_blocking(self, control: &RunControl) -> HostReport {
        match tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
        {
            Ok(runtime) => runtime.block_on(self.run(control)),
            Err(source) => HostReport {
                execution: Err(HostError::Io {
                    path: PathBuf::from("host runtime"),
                    source,
                }),
                cleanup: vec![],
            },
        }
    }

    async fn run(self, control: &RunControl) -> HostReport {
        let mut resources = Resources::default();
        let execution = async {
            if self.uses_opencv {
                resources.matchers.insert(
                    "opencv".into(),
                    NativeMatcher::open_opencv(None, control.token()).await?,
                );
            }
            for (name, model) in self.ocr {
                resources.ocr.insert(
                    name,
                    NativeOcr::open(model.config()?, control.token()).await?,
                );
            }
            let clock = CaptureClock::default();
            if let Some(device) = &self.config.video {
                if control.is_cancelled() {
                    return Err(CaptureError::Cancelled.into());
                }
                let session = VideoSession::open_default(device, clock)?;
                eprintln!("video {device}: {:?}", session.format());
                resources.videos.insert("video".into(), session);
            }
            if let Some(port) = &self.config.gamepad {
                resources.pads.insert(
                    "gamepad".into(),
                    easycon_device::connect_serial(port, control.token())?,
                );
            }
            let meta = MetaContext::new(Arc::new(ConsoleLog));
            let mut bindings = RunBindings {
                meta: Some(&meta),
                ..RunBindings::default()
            };
            for (name, session) in &resources.videos {
                bindings
                    .videos
                    .insert(name.clone(), Box::new(session.subscribe()));
            }
            for (name, matcher) in &mut resources.matchers {
                bindings
                    .matchers
                    .insert(name.clone(), matcher as &mut dyn TemplateMatcher);
            }
            for (name, pad) in &mut resources.pads {
                bindings.pads.insert(name.clone(), &mut **pad);
            }
            for (name, ocr) in &mut resources.ocr {
                bindings
                    .ocr
                    .insert(name.clone(), ocr as &mut dyn OcrBackend);
            }
            Ok(self
                .document
                .execute_controlled(bindings, self.params, control)
                .await?)
        }
        .await;
        let cleanup = resources.shutdown().await;
        HostReport { execution, cleanup }
    }
}

fn uses_primitive(node: &Node, names: &[&str]) -> bool {
    let visit = |child: &Node| uses_primitive(child, names);
    match &node.flow {
        Flow::Primitive { name, .. } => names.contains(&name.as_str()),
        Flow::Eval { .. } | Flow::SubflowCall { .. } => false,
        Flow::Seq { steps } => steps.iter().any(visit),
        Flow::Par { branches, .. } => branches.iter().any(visit),
        Flow::Bind { next, .. } => visit(next),
        Flow::Choose { then, else_, .. } => visit(then) || else_.as_deref().is_some_and(visit),
        Flow::Retry { body, .. } | Flow::Timeout { body, .. } | Flow::LoopUntil { body, .. } => {
            visit(body)
        }
        Flow::Catch { body, handler, .. } => visit(body) || visit(handler),
        Flow::Fallback { primary, secondary } => visit(primary) || visit(secondary),
        Flow::Pipe { source, sink } => visit(source) || visit(sink),
    }
}

#[derive(Debug)]
#[must_use = "inspect both execution and host cleanup before reporting success"]
pub struct HostReport {
    pub execution: Result<Execution, HostError>,
    /// Physical teardown errors; IR's lease cleanup remains in Execution.cleanup.
    pub cleanup: Vec<(String, HostError)>,
}

#[derive(Default)]
struct Resources {
    videos: BTreeMap<String, VideoSession>,
    matchers: BTreeMap<String, NativeMatcher>,
    pads: BTreeMap<String, Box<dyn Gamepad>>,
    ocr: BTreeMap<String, NativeOcr>,
}

impl Resources {
    async fn shutdown(mut self) -> Vec<(String, HostError)> {
        let mut errors = Vec::new();
        // Input release comes first. A failure must never skip other resources.
        for (name, pad) in &mut self.pads {
            if let Err(error) = pad.disconnect() {
                errors.push((name.clone(), error.into()));
            }
        }
        for (name, video) in &mut self.videos {
            if let Err(error) = video.stop() {
                errors.push((name.clone(), error.into()));
            }
        }
        for (name, matcher) in self.matchers {
            if let Err(error) = matcher.shutdown().await {
                errors.push((name, error.into()));
            }
        }
        for (name, ocr) in self.ocr {
            if let Err(error) = ocr.shutdown().await {
                errors.push((name, error.into()));
            }
        }
        errors
    }
}

struct ConsoleLog;
impl LogSink for ConsoleLog {
    fn emit(&self, record: LogRecord) -> Result<(), String> {
        eprintln!(
            "{}ms {:?}: {} {:?}",
            record.timestamp_ms, record.level, record.message, record.fields
        );
        Ok(())
    }
}
