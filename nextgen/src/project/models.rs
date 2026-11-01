//! Workflow-owned OCR declarations. Model paths are portable, relative to the workflow project.

use super::HostError;
use crate::vision::{OcrConfig, PpOcrConfig, TesseractConfig, VisionError, opencv::platform_rid};
use serde::Deserialize;
use std::{
    collections::BTreeMap,
    path::{Component, Path, PathBuf},
};

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct PpOcrModel {
    model: PathBuf,
    dictionary: PathBuf,
    #[serde(default = "recognition_height")]
    recognition_height: u32,
    #[serde(default = "max_width")]
    max_recognition_width: u32,
    #[serde(default = "append_space")]
    append_space: bool,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct TesseractModel {
    models: Vec<PathBuf>,
    #[serde(default = "page_segmentation")]
    page_segmentation: u8,
}

enum OcrModel {
    Ppocr(PpOcrModel),
    Tesseract(TesseractModel),
}

fn recognition_height() -> u32 {
    48
}
fn max_width() -> u32 {
    2048
}
fn append_space() -> bool {
    true
}
fn page_segmentation() -> u8 {
    7
}

pub(super) enum PreparedOcr {
    Ppocr(PpOcrConfig),
    Tesseract {
        data_path: PathBuf,
        language: String,
        page_segmentation: u8,
    },
}

fn model_file(project: &Path, relative: &Path) -> Result<PathBuf, HostError> {
    let mut parts = relative.components();
    if relative.to_string_lossy().contains(['\\', ':'])
        || parts.next() != Some(Component::Normal("models".as_ref()))
        || parts.clone().next().is_none()
        || parts.any(|part| !matches!(part, Component::Normal(_)))
    {
        return Err(HostError::Config(format!(
            "model path must start with models/ without traversal: {}",
            relative.display()
        )));
    }
    let root = project.join("models");
    let root = root
        .canonicalize()
        .map_err(|source| HostError::Io { path: root, source })?;
    let path = project.join(relative);
    let path = path
        .canonicalize()
        .map_err(|source| HostError::Io { path, source })?;
    if !root.starts_with(project) || !path.starts_with(&root) {
        return Err(HostError::Config(format!(
            "model path escapes project models directory: {}",
            relative.display()
        )));
    }
    let metadata = path.metadata().map_err(|source| HostError::Io {
        path: path.clone(),
        source,
    })?;
    if !metadata.is_file() || metadata.len() == 0 {
        return Err(HostError::Config(format!(
            "model asset must be a nonempty file: {}",
            path.display()
        )));
    }
    Ok(path)
}

pub(super) fn prepare(
    project: &Path,
    tesseract: BTreeMap<String, TesseractModel>,
    ppocr: BTreeMap<String, PpOcrModel>,
) -> Result<BTreeMap<String, PreparedOcr>, HostError> {
    let mut prepared = BTreeMap::new();
    let declarations = tesseract
        .into_iter()
        .map(|(name, model)| (name, OcrModel::Tesseract(model)))
        .chain(
            ppocr
                .into_iter()
                .map(|(name, model)| (name, OcrModel::Ppocr(model))),
        );
    for (name, declaration) in declarations {
        if prepared.contains_key(&name) {
            return Err(HostError::Config(format!(
                "duplicate OCR binding name: {name}"
            )));
        }
        if name.trim().is_empty() {
            return Err(HostError::Config(
                "OCR binding name must be nonempty".into(),
            ));
        }
        let model = match declaration {
            OcrModel::Ppocr(PpOcrModel {
                model,
                dictionary,
                recognition_height,
                max_recognition_width,
                append_space,
            }) => {
                if !(1..=256).contains(&recognition_height)
                    || !(32..=8192).contains(&max_recognition_width)
                {
                    return Err(HostError::Config(format!(
                        "OCR {name}: invalid PP-OCR recognition dimensions"
                    )));
                }
                if model.extension().and_then(|s| s.to_str()) != Some("onnx") {
                    return Err(HostError::Config(format!(
                        "OCR {name}: PP-OCR model must be an .onnx file"
                    )));
                }
                let mut config = PpOcrConfig::new(
                    model_file(project, &model)?,
                    model_file(project, &dictionary)?,
                );
                config.recognition_height = recognition_height;
                config.max_recognition_width = max_recognition_width;
                config.append_space = append_space;
                PreparedOcr::Ppocr(config)
            }
            OcrModel::Tesseract(TesseractModel {
                models,
                page_segmentation,
            }) => {
                if models.is_empty() || !(3..=13).contains(&page_segmentation) {
                    return Err(HostError::Config(format!(
                        "OCR {name}: provide traineddata models and text PSM 3..=13"
                    )));
                }
                let mut directory = None;
                let mut languages = Vec::new();
                for model in models {
                    let language = model.file_stem().and_then(|s| s.to_str()).unwrap_or("");
                    if model.extension().and_then(|s| s.to_str()) != Some("traineddata")
                        || language.is_empty()
                        || !language
                            .chars()
                            .all(|c| c.is_ascii_alphanumeric() || c == '_')
                        || languages.iter().any(|code| code == language)
                    {
                        return Err(HostError::Config(format!(
                            "OCR {name}: expected unique language-code.traineddata files"
                        )));
                    }
                    let path = model_file(project, &model)?;
                    // Tesseract resolves each language by its filename in one shared data directory.
                    if path.file_name() != model.file_name() {
                        return Err(HostError::Config(format!(
                            "OCR {name}: traineddata symlink must preserve its language filename"
                        )));
                    }
                    let parent = path
                        .parent()
                        .expect("canonical file has a parent")
                        .to_owned();
                    if directory
                        .as_ref()
                        .is_some_and(|previous| previous != &parent)
                    {
                        return Err(HostError::Config(format!(
                            "OCR {name}: traineddata files must share one directory"
                        )));
                    }
                    directory = Some(parent);
                    languages.push(language.to_owned());
                }
                PreparedOcr::Tesseract {
                    data_path: directory.expect("nonempty models"),
                    language: languages.join("+"),
                    page_segmentation,
                }
            }
        };
        prepared.insert(name, model);
    }
    Ok(prepared)
}

impl PreparedOcr {
    pub(super) fn config(self) -> Result<OcrConfig, HostError> {
        Ok(match self {
            Self::Ppocr(config) => OcrConfig::PpOcr(config),
            Self::Tesseract {
                data_path,
                language,
                page_segmentation,
            } => OcrConfig::Tesseract(TesseractConfig {
                runtime_library: tesseract_library()?,
                data_path,
                language,
                page_segmentation,
            }),
        })
    }
}

fn tesseract_library() -> Result<PathBuf, HostError> {
    if let Some(path) = std::env::var_os("EASYCON_TESSERACT_LIBRARY") {
        if path.is_empty() {
            return Err(HostError::Config(
                "EASYCON_TESSERACT_LIBRARY must not be empty".into(),
            ));
        }
        return Ok(path.into());
    }
    let filename = if cfg!(target_os = "windows") {
        "libtesseract-5.dll"
    } else if cfg!(target_os = "macos") {
        "libtesseract.5.dylib"
    } else {
        "libtesseract.so.5"
    };
    let executable =
        std::env::current_exe().map_err(|error| VisionError::backend("tesseract", error))?;
    let rid = platform_rid()?;
    let deployed = executable
        .parent()
        .expect("executable has parent")
        .join("native")
        .join(rid)
        .join(filename);
    if deployed.is_file() {
        return Ok(deployed);
    }
    Ok(Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("native/runtimes")
        .join(rid)
        .join(filename))
}
