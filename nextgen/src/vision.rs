//! Explicit, immutable RGB images and interchangeable recognition backends.

use std::{future::Future, path::PathBuf, pin::Pin, sync::Arc};

use thiserror::Error;
use tokio_util::sync::CancellationToken;

use crate::{SampleId, SampleTime, video::VideoFrame};

pub mod measure;
pub mod opencv;
#[cfg(feature = "ocr-ppocr")]
pub mod ppocr;
#[cfg(feature = "ocr-tesseract")]
pub mod tesseract;
pub mod worker;

#[cfg(test)]
mod tests;

pub type Result<T> = std::result::Result<T, VisionError>;
/// Local future: inline providers may be thread-local; managed providers await worker replies.
pub type VisionFuture<'a, T> = Pin<Box<dyn Future<Output = Result<T>> + 'a>>;

#[derive(Debug, Error)]
pub enum VisionError {
    #[error("operation cancelled")]
    Cancelled,
    #[error("invalid argument: {0}")]
    InvalidArgument(String),
    #[error("invalid RGB image: {0}")]
    InvalidImage(String),
    #[error("backend {0} is not enabled in this build")]
    BackendUnavailable(&'static str),
    #[error("backend {0} is not implemented yet")]
    BackendNotImplemented(&'static str),
    #[error("asset {path}: {message}")]
    Asset { path: PathBuf, message: String },
    #[error("{backend}: {message}")]
    Backend {
        backend: &'static str,
        message: String,
    },
    #[error("invalid backend output: {0}")]
    InvalidOutput(String),
    #[error("native worker failed: {0}")]
    Worker(#[source] crate::native_work::WorkerError),
}

impl VisionError {
    pub(crate) fn backend(backend: &'static str, error: impl std::fmt::Display) -> Self {
        Self::Backend {
            backend,
            message: error.to_string(),
        }
    }
}

pub(crate) fn check_cancelled(token: &CancellationToken) -> Result<()> {
    if token.is_cancelled() {
        Err(VisionError::Cancelled)
    } else {
        Ok(())
    }
}

/// Pixel coordinates: top-left origin, x rightward, y downward; right/bottom exclusive.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Rect {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

impl Rect {
    pub fn validate(self, width: u32, height: u32) -> Result<Self> {
        if self.width == 0
            || self.height == 0
            || self
                .x
                .checked_add(self.width)
                .is_none_or(|right| right > width)
            || self
                .y
                .checked_add(self.height)
                .is_none_or(|bottom| bottom > height)
        {
            return Err(VisionError::InvalidArgument(
                "ROI must be nonempty and inside the image".into(),
            ));
        }
        Ok(self)
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FrameSource {
    pub id: SampleId,
    pub time: SampleTime,
    pub width: u32,
    pub height: u32,
}

/// A validated RGB8 asset. Crops retain the original source and coordinate offset.
#[derive(Clone, Debug)]
pub struct Image {
    width: u32,
    height: u32,
    rgb: Arc<[u8]>,
    x: u32,
    y: u32,
    source: Option<FrameSource>,
}

impl Image {
    pub fn new(width: u32, height: u32, rgb: impl Into<Arc<[u8]>>) -> Result<Self> {
        let rgb = rgb.into();
        let expected = (width as usize)
            .checked_mul(height as usize)
            .and_then(|n| n.checked_mul(3));
        if width == 0
            || height == 0
            || width > i32::MAX as u32
            || height > i32::MAX as u32
            || expected != Some(rgb.len())
        {
            return Err(VisionError::InvalidImage(
                "expected positive i32 dimensions and exactly width*height*3 bytes".into(),
            ));
        }
        Ok(Self {
            width,
            height,
            rgb,
            x: 0,
            y: 0,
            source: None,
        })
    }

    pub fn from_frame(frame: &VideoFrame) -> Result<Self> {
        let mut image = Self::new(frame.format.width, frame.format.height, frame.rgb.clone())?;
        image.source = Some(FrameSource {
            id: frame.id.clone(),
            time: frame.time,
            width: image.width,
            height: image.height,
        });
        Ok(image)
    }

    pub fn width(&self) -> u32 {
        self.width
    }
    pub fn height(&self) -> u32 {
        self.height
    }
    pub fn rgb(&self) -> &[u8] {
        &self.rgb
    }
    pub fn source(&self) -> Option<&FrameSource> {
        self.source.as_ref()
    }

    /// Bounds in the original image/frame coordinate system.
    pub fn bounds(&self) -> Rect {
        Rect {
            x: self.x,
            y: self.y,
            width: self.width,
            height: self.height,
        }
    }

    /// Input coordinates are local to this image.
    pub fn pixel(&self, x: u32, y: u32) -> Result<[u8; 3]> {
        Rect {
            x,
            y,
            width: 1,
            height: 1,
        }
        .validate(self.width, self.height)?;
        let start = (y as usize * self.width as usize + x as usize) * 3;
        Ok(self.rgb[start..start + 3]
            .try_into()
            .expect("validated RGB pixel"))
    }

    /// Copies only the selected RGB region; input ROI is local, output origin remains absolute.
    pub fn crop(&self, region: Rect) -> Result<Self> {
        let region = region.validate(self.width, self.height)?;
        let mut rgb = Vec::with_capacity(region.width as usize * region.height as usize * 3);
        let stride = self.width as usize * 3;
        for y in region.y..region.y + region.height {
            let start = y as usize * stride + region.x as usize * 3;
            rgb.extend_from_slice(&self.rgb[start..start + region.width as usize * 3]);
        }
        Ok(Self {
            width: region.width,
            height: region.height,
            rgb: rgb.into(),
            x: self.x + region.x,
            y: self.y + region.y,
            source: self.source.clone(),
        })
    }

    #[cfg(any(feature = "ocr-ppocr", feature = "ocr-tesseract"))]
    pub(crate) fn selected(&self, region: Option<Rect>) -> Result<Self> {
        match region {
            Some(region) => self.crop(region),
            None => Ok(self.clone()),
        }
    }

    #[cfg(feature = "ocr-ppocr")]
    pub(crate) fn buffer(&self) -> image::RgbImage {
        image::RgbImage::from_raw(self.width, self.height, self.rgb.to_vec())
            .expect("validated RGB image")
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TemplateMode {
    SqDiff,
    SqDiffNormed,
    CCorr,
    CCorrNormed,
    CCoeff,
    CCoeffNormed,
}

impl TemplateMode {
    /// Contract score mapping, independent of backend; similarity is not a probability.
    pub fn confidence(self, raw_score: f64) -> Result<Option<f64>> {
        if !raw_score.is_finite() {
            return Err(VisionError::InvalidOutput(
                "template score must be finite".into(),
            ));
        }
        Ok(match self {
            Self::SqDiffNormed => Some((1.0 - raw_score).clamp(0.0, 1.0)),
            Self::CCorrNormed => Some(raw_score.clamp(0.0, 1.0)),
            Self::CCoeffNormed => Some(((raw_score + 1.0) / 2.0).clamp(0.0, 1.0)),
            _ => None,
        })
    }
}

#[derive(Clone, Copy, Debug)]
pub struct MatchConfig {
    pub mode: TemplateMode,
    /// Search ROI, local to the supplied image.
    pub region: Option<Rect>,
}

impl Default for MatchConfig {
    fn default() -> Self {
        Self {
            mode: TemplateMode::SqDiffNormed,
            region: None,
        }
    }
}

#[derive(Clone, Debug)]
pub struct TemplateMatch {
    pub source: Option<FrameSource>,
    pub search: Rect,
    pub bounds: Rect,
    pub mode: TemplateMode,
    pub raw_score: f64,
    /// Present only for normalized modes. Not a probability.
    pub confidence: Option<f64>,
    pub implementation: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TemplateBackendKind {
    OpenCv5,
}

/// One measurement of explicit images. No capture, threshold decision, retry or logging.
/// Synchronous backends belong on a host-managed blocking task when used from async code.
/// Providers bound to the Pure primitive catalogue must use fixed assets/configuration and
/// must not read mutable external state. Stateful observers need a separate Effectful contract.
pub trait TemplateMatcher {
    fn kind(&self) -> TemplateBackendKind;
    fn match_template(
        &mut self,
        image: &Image,
        template: &Image,
        config: MatchConfig,
        token: &CancellationToken,
    ) -> Result<TemplateMatch>;

    /// Inline by default. Async hosts should bind NativeMatcher for blocking native backends.
    fn match_template_async<'a>(
        &'a mut self,
        image: &'a Image,
        template: &'a Image,
        config: MatchConfig,
        token: &'a CancellationToken,
    ) -> VisionFuture<'a, TemplateMatch> {
        Box::pin(async move { self.match_template(image, template, config, token) })
    }
}

pub fn create_template_matcher(kind: TemplateBackendKind) -> Result<Box<dyn TemplateMatcher>> {
    match kind {
        TemplateBackendKind::OpenCv5 => Ok(Box::new(opencv::OpenCv5::open_default()?)),
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum OcrBackendKind {
    Tesseract,
    PpOcr,
}

#[derive(Clone, Debug)]
pub struct TesseractConfig {
    pub runtime_library: PathBuf,
    pub data_path: PathBuf,
    pub language: String,
    /// Tesseract text PSM 3..=13; use 6 for a uniform text block, 7 for a single line.
    pub page_segmentation: u8,
}

#[derive(Clone, Debug)]
pub struct PpOcrConfig {
    /// Explicit native OpenCV directory; None uses the template matcher's default lookup.
    pub opencv_dir: Option<PathBuf>,
    pub recognition_model: PathBuf,
    pub dictionary: PathBuf,
    /// None selects single-line recognition. Some requests deferred OpenCV text detection.
    pub detection_model: Option<PathBuf>,
    pub recognition_height: u32,
    pub max_recognition_width: u32,
    pub append_space: bool,
    /// Reserved DB detector settings; detection is currently not implemented.
    pub detection_max_side: u32,
    pub pixel_threshold: f32,
    pub box_threshold: f32,
    pub unclip_ratio: f32,
}

impl PpOcrConfig {
    pub fn new(recognition_model: PathBuf, dictionary: PathBuf) -> Self {
        Self {
            opencv_dir: None,
            recognition_model,
            dictionary,
            detection_model: None,
            recognition_height: 48,
            max_recognition_width: 2048,
            append_space: true,
            detection_max_side: 960,
            pixel_threshold: 0.3,
            box_threshold: 0.6,
            unclip_ratio: 1.5,
        }
    }
}

#[derive(Clone, Debug)]
pub enum OcrConfig {
    Tesseract(TesseractConfig),
    PpOcr(PpOcrConfig),
}

pub fn create_ocr(config: OcrConfig) -> Result<Box<dyn OcrBackend>> {
    match config {
        OcrConfig::Tesseract(config) => {
            #[cfg(feature = "ocr-tesseract")]
            {
                Ok(Box::new(tesseract::Tesseract::open(config)?))
            }
            #[cfg(not(feature = "ocr-tesseract"))]
            {
                let _ = config;
                Err(VisionError::BackendUnavailable("tesseract"))
            }
        }
        OcrConfig::PpOcr(config) => {
            #[cfg(feature = "ocr-ppocr")]
            {
                Ok(Box::new(ppocr::PpOcr::open(config)?))
            }
            #[cfg(not(feature = "ocr-ppocr"))]
            {
                let _ = config;
                Err(VisionError::BackendUnavailable("ppocr"))
            }
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Point {
    pub x: f32,
    pub y: f32,
}

#[derive(Clone, Debug)]
pub struct OcrBox {
    pub text: String,
    /// Backend confidence in 0..=1, when available; not comparable across engines.
    pub confidence: Option<f32>,
    /// Clockwise polygon in original image/frame coordinates.
    pub polygon: [Point; 4],
}

#[derive(Clone, Debug)]
pub struct OcrResult {
    pub source: Option<FrameSource>,
    pub region: Rect,
    pub kind: OcrBackendKind,
    pub text: String,
    pub boxes: Vec<OcrBox>,
    /// Fixed configuration/model paths used by this instance, for diagnostics.
    pub profile: String,
}

impl OcrResult {
    /// Mean box confidence, or None for empty results or any unknown box confidence.
    /// This aggregate is not a probability and is not comparable across OCR kinds.
    pub fn confidence(&self) -> Option<f32> {
        if self.boxes.is_empty() {
            return None;
        }
        let total = self.boxes.iter().try_fold(0.0_f64, |sum, item| {
            item.confidence.map(|value| sum + f64::from(value))
        })?;
        Some((total / self.boxes.len() as f64) as f32)
    }
}

/// One observation of an explicit image. Implementations must not capture, retry or log.
/// Pure catalogue bindings require fixed loaded models/configuration and no adaptive learning.
pub trait OcrBackend {
    fn kind(&self) -> OcrBackendKind;
    fn recognize(
        &mut self,
        image: &Image,
        region: Option<Rect>,
        token: &CancellationToken,
    ) -> Result<OcrResult>;

    /// Inline by default. Async hosts should bind NativeOcr for blocking native backends.
    fn recognize_async<'a>(
        &'a mut self,
        image: &'a Image,
        region: Option<Rect>,
        token: &'a CancellationToken,
    ) -> VisionFuture<'a, OcrResult> {
        Box::pin(async move { self.recognize(image, region, token) })
    }
}

#[cfg(any(feature = "ocr-tesseract", feature = "ocr-ppocr"))]
pub(crate) fn rectangle_polygon(rect: Rect) -> [Point; 4] {
    let (x, y, r, b) = (
        rect.x as f32,
        rect.y as f32,
        (rect.x + rect.width) as f32,
        (rect.y + rect.height) as f32,
    );
    [
        Point { x, y },
        Point { x: r, y },
        Point { x: r, y: b },
        Point { x, y: b },
    ]
}
