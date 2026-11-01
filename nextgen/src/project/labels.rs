use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
};

use base64::{Engine, engine::general_purpose::STANDARD};
use serde::Deserialize;

use super::{HostError, read_json};
use crate::{
    ir::{Symbol, SymbolKind, Type, Value},
    vision::{Image, VisionError},
};

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase", deny_unknown_fields)]
struct LegacyLabel {
    #[serde(rename = "searchMethod", default = "default_method")]
    search_method: i32,
    #[serde(default)]
    img_base64: String,
    #[serde(default)]
    range_x: i64,
    #[serde(default)]
    range_y: i64,
    #[serde(default)]
    range_width: i64,
    #[serde(default)]
    range_height: i64,
    // These describe editing/prebaked preprocessing, not another runtime crop or filter.
    #[serde(rename = "TargetX", default)]
    _target_x: i64,
    #[serde(rename = "TargetY", default)]
    _target_y: i64,
    #[serde(rename = "TargetWidth", default)]
    _target_width: i64,
    #[serde(rename = "TargetHeight", default)]
    _target_height: i64,
    #[serde(rename = "UseGrayscale", default)]
    _use_grayscale: bool,
    #[serde(rename = "UseBinary", default)]
    _use_binary: bool,
    #[serde(rename = "UseGaussianBlur", default)]
    _use_gaussian_blur: bool,
    #[serde(rename = "UseOther", default)]
    _use_other: bool,
}

fn default_method() -> i32 {
    5
}

pub(super) struct LabelFile {
    path: PathBuf,
    label: LegacyLabel,
}

/// Scan descriptors only; unused labels need not have supported algorithms or decoded images.
pub(super) fn scan(project: &Path) -> Result<BTreeMap<String, LabelFile>, HostError> {
    let mut directory = None;
    for name in ["imglabel", "ImgLabel"] {
        let path = project.join(name);
        let resolved = match path.canonicalize() {
            Ok(path) => path,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(source) => return Err(HostError::Io { path, source }),
        };
        if directory
            .as_ref()
            .is_some_and(|previous| previous != &resolved)
        {
            return Err(HostError::Config(
                "both imglabel and ImgLabel directories exist; use one label directory".into(),
            ));
        }
        directory = Some(resolved);
    }
    let Some(directory) = directory else {
        return Ok(BTreeMap::new());
    };
    let mut paths = Vec::new();
    for entry in std::fs::read_dir(&directory).map_err(|source| HostError::Io {
        path: directory.clone(),
        source,
    })? {
        let path = entry
            .map_err(|source| HostError::Io {
                path: directory.clone(),
                source,
            })?
            .path();
        if path.is_file()
            && path
                .extension()
                .and_then(|s| s.to_str())
                .is_some_and(|extension| extension.eq_ignore_ascii_case("il"))
        {
            paths.push(path);
        }
    }
    paths.sort();
    let mut labels = BTreeMap::new();
    for path in paths {
        let name = path
            .file_stem()
            .and_then(|s| s.to_str())
            .filter(|s| !s.is_empty())
            .ok_or_else(|| {
                HostError::Config(format!(
                    "{}: label name must be UTF-8 and nonempty",
                    path.display()
                ))
            })?
            .to_owned();
        let label = read_json(&path)?;
        if labels
            .insert(name.clone(), LabelFile { path, label })
            .is_some()
        {
            return Err(HostError::Config(format!("duplicate label: {name}")));
        }
    }
    Ok(labels)
}

/// Read a legacy EasyCon .IL with its embedded image, without loading OpenCV.
pub fn load_image_label(project: &Path, name: &str) -> Result<Value, HostError> {
    scan(project)?
        .get(name)
        .ok_or_else(|| HostError::Config(format!("imglabel: label {name:?} not found")))?
        .load()
}

fn record<const N: usize>(fields: [(&str, Value); N]) -> Value {
    Value::Record(fields.into_iter().map(|(k, v)| (k.into(), v)).collect())
}

impl LabelFile {
    pub(super) fn load(&self) -> Result<Value, HostError> {
        let config = &self.label;
        let invalid =
            |message: &str| HostError::Config(format!("{}: {message}", self.path.display()));
        let mode = match config.search_method {
            0 => "SqDiff",
            1 => "SqDiffNormed",
            2 => "CCorr",
            3 => "CCorrNormed",
            4 => "CCoeff",
            5 => "CCoeffNormed",
            method => {
                return Err(invalid(&format!(
                    "unsupported legacy searchMethod {method}; supported template modes are 0..=5"
                )));
            }
        };
        let encoded: String = config
            .img_base64
            .chars()
            .filter(|c| !c.is_ascii_whitespace())
            .collect();
        let bytes = STANDARD
            .decode(encoded)
            .map_err(|error| invalid(&format!("invalid ImgBase64: {error}")))?;
        let decoded = image::load_from_memory(&bytes).map_err(|error| VisionError::Asset {
            path: self.path.clone(),
            message: error.to_string(),
        })?;
        let pixels = decoded.to_rgb8();
        let image = Image::new(pixels.width(), pixels.height(), pixels.into_raw())?;
        let rect_type = Type::Record(
            ["x", "y", "width", "height"]
                .map(|key| (key.into(), Type::Int))
                .into(),
        );
        let region = if [
            config.range_x,
            config.range_y,
            config.range_width,
            config.range_height,
        ] == [0; 4]
        {
            None
        } else {
            let [x, y, width, height]: [u32; 4] = [
                config.range_x,
                config.range_y,
                config.range_width,
                config.range_height,
            ]
            .map(u32::try_from)
            .into_iter()
            .collect::<Result<Vec<_>, _>>()
            .map_err(|_| invalid("Range fields must be nonnegative 32-bit coordinates"))?
            .try_into()
            .expect("four coordinates");
            if width == 0
                || height == 0
                || x.checked_add(width).is_none()
                || y.checked_add(height).is_none()
            {
                return Err(invalid("search region must be nonempty without overflow"));
            }
            if image.width() > width || image.height() > height {
                return Err(invalid("label image must fit inside its search region"));
            }
            Some(Box::new(record([
                ("x", Value::Int(x.into())),
                ("y", Value::Int(y.into())),
                ("width", Value::Int(width.into())),
                ("height", Value::Int(height.into())),
            ])))
        };
        Ok(record([
            ("image", Value::Image(image)),
            (
                "config",
                record([
                    (
                        "mode",
                        Value::Symbol(
                            Symbol::new(SymbolKind::TemplateMode, mode).expect("known mode"),
                        ),
                    ),
                    (
                        "region",
                        Value::Option {
                            element: rect_type,
                            value: region,
                        },
                    ),
                ]),
            ),
        ]))
    }
}
