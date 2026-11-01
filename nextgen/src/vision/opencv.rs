//! Dynamic native OpenCV >= 5 template matching. No OpenCvSharp dependency.

use easycon_opencv_runtime::{Method, RgbImage, Runtime};
use std::path::{Path, PathBuf};
use tokio_util::sync::CancellationToken;

use super::{
    Image, MatchConfig, Result, TemplateBackendKind, TemplateMatch, TemplateMatcher, TemplateMode,
    VisionError, check_cancelled,
};
pub use easycon_opencv_runtime::Version as OpenCvVersion;

pub struct OpenCv5 {
    runtime: Runtime,
}

pub fn platform_rid() -> Result<&'static str> {
    match (std::env::consts::OS, std::env::consts::ARCH) {
        ("windows", "x86_64") => Ok("win-x64"),
        ("windows", "aarch64") => Ok("win-arm64"),
        ("macos", "aarch64") => Ok("osx-arm64"),
        ("macos", "x86_64") => Ok("osx-x64"),
        ("linux", "x86_64") => Ok("linux-x64"),
        ("linux", "aarch64") => Ok("linux-arm64"),
        _ => Err(VisionError::backend(
            "opencv-native",
            "unsupported platform or architecture",
        )),
    }
}

pub fn bridge_filename() -> &'static str {
    if cfg!(target_os = "windows") {
        "easycon_opencv.dll"
    } else if cfg!(target_os = "macos") {
        "libeasycon_opencv.dylib"
    } else {
        "libeasycon_opencv.so"
    }
}

pub(super) fn runtime_directory(explicit: Option<&Path>) -> Result<PathBuf> {
    if let Some(directory) = explicit {
        return Ok(directory.into());
    }
    if let Some(directory) = std::env::var_os("EASYCON_OPENCV_DIR") {
        if directory.is_empty() {
            return Err(VisionError::InvalidArgument(
                "EASYCON_OPENCV_DIR must not be empty".into(),
            ));
        }
        return Ok(directory.into());
    }
    let rid = platform_rid()?;
    let executable =
        std::env::current_exe().map_err(|e| VisionError::backend("opencv-native", e))?;
    if let Some(parent) = executable.parent() {
        let deployed = parent.join("native").join(rid);
        if deployed.join(bridge_filename()).is_file() {
            return Ok(deployed);
        }
    }
    Ok(Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("native/runtimes")
        .join(rid))
}

impl OpenCv5 {
    /// Directory containing our ABI-v1 bridge and its native OpenCV shared libraries.
    pub fn open(directory: impl AsRef<Path>) -> Result<Self> {
        let runtime = Runtime::open(directory.as_ref().join(bridge_filename()))
            .map_err(|e| VisionError::backend("opencv-native", e))?;
        Ok(Self { runtime })
    }

    /// Explicit environment override, then deployed executable assets, then workspace assets.
    /// An explicit override failure is reported without falling back to another runtime.
    pub fn open_default() -> Result<Self> {
        Self::open(runtime_directory(None)?)
    }

    pub fn version(&self) -> OpenCvVersion {
        self.runtime.version()
    }
    pub fn library_path(&self) -> &Path {
        self.runtime.path()
    }
}

impl TemplateMatcher for OpenCv5 {
    fn kind(&self) -> TemplateBackendKind {
        TemplateBackendKind::OpenCv5
    }

    fn match_template(
        &mut self,
        image: &Image,
        template: &Image,
        config: MatchConfig,
        token: &CancellationToken,
    ) -> Result<TemplateMatch> {
        check_cancelled(token)?;
        let selected = match config.region {
            Some(region) => image.crop(region)?,
            None => image.clone(),
        };
        if template.width() > selected.width() || template.height() > selected.height() {
            return Err(VisionError::InvalidArgument(
                "template must fit inside the search region".into(),
            ));
        }
        if matches!(
            config.mode,
            TemplateMode::CCoeff | TemplateMode::CCoeffNormed
        ) && template
            .rgb()
            .as_chunks::<3>()
            .0
            .iter()
            .all(|pixel| pixel.as_slice() == &template.rgb()[..3])
        {
            return Err(VisionError::InvalidArgument(
                "CCoeff requires spatial variation in the template".into(),
            ));
        }
        let method = match config.mode {
            TemplateMode::SqDiff => Method::SqDiff,
            TemplateMode::SqDiffNormed => Method::SqDiffNormed,
            TemplateMode::CCorr => Method::CCorr,
            TemplateMode::CCorrNormed => Method::CCorrNormed,
            TemplateMode::CCoeff => Method::CCoeff,
            TemplateMode::CCoeffNormed => Method::CCoeffNormed,
        };
        fn input(image: &Image) -> RgbImage<'_> {
            RgbImage {
                width: image.width(),
                height: image.height(),
                pixels: image.rgb(),
            }
        }
        check_cancelled(token)?;
        let measured = self
            .runtime
            .match_template(input(&selected), input(template), method);
        check_cancelled(token)?;
        let measured = measured.map_err(|e| VisionError::backend("opencv-native", e))?;
        let confidence = config.mode.confidence(measured.score)?;
        let search = selected.bounds();
        Ok(TemplateMatch {
            source: selected.source().cloned(),
            search,
            bounds: super::Rect {
                x: search.x + measured.x as u32,
                y: search.y + measured.y as u32,
                width: template.width(),
                height: template.height(),
            },
            mode: config.mode,
            raw_score: measured.score,
            confidence,
            implementation: format!(
                "opencv-native/{}; {}",
                self.version(),
                self.library_path().display()
            ),
        })
    }
}
