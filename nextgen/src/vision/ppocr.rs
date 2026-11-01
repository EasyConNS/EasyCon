use std::path::{Path, PathBuf};

use easycon_opencv_runtime::dnn::Dnn;
use tokio_util::sync::CancellationToken;

use super::{
    Image, OcrBackend, OcrBackendKind, OcrBox, OcrResult, PpOcrConfig, Rect, Result, VisionError,
    check_cancelled, opencv, rectangle_polygon,
};

fn error(e: impl std::fmt::Display) -> VisionError {
    VisionError::backend("ppocr-opencv-dnn", e)
}

fn asset(path: &Path) -> Result<PathBuf> {
    std::fs::canonicalize(path).map_err(|e| VisionError::Asset {
        path: path.into(),
        message: e.to_string(),
    })
}

struct Model {
    session: Dnn,
    shape: [i64; 4],
}

impl Model {
    fn open(config: &PpOcrConfig) -> Result<Self> {
        let path = asset(&config.recognition_model)?;
        let bytes = std::fs::read(&path).map_err(|e| VisionError::Asset {
            path,
            message: e.to_string(),
        })?;
        let directory = opencv::runtime_directory(config.opencv_dir.as_deref())?;
        let session =
            Dnn::open(directory.join(opencv::bridge_filename()), &bytes).map_err(error)?;
        let shape = session.input_shape();
        if !matches!(shape[0], -1 | 1)
            || shape[1] != 3
            || shape
                .iter()
                .any(|&dimension| dimension == 0 || dimension < -1)
        {
            return Err(error("expected NCHW input with batch 1 and 3 channels"));
        }
        Ok(Self { session, shape })
    }

    fn run(
        &mut self,
        data: Vec<f32>,
        height: u32,
        width: u32,
        token: &CancellationToken,
    ) -> Result<(Vec<i64>, Vec<f32>)> {
        check_cancelled(token)?;
        let result = self.session.forward(&data, height, width).map_err(error);
        // OpenCV forward is synchronous: cancellation is observed after it returns.
        check_cancelled(token)?;
        let output = result?;
        Ok((output.shape.to_vec(), output.values))
    }
}

pub struct PpOcr {
    config: PpOcrConfig,
    recognition: Model,
    dictionary: Vec<String>,
    profile: String,
}

impl PpOcr {
    pub fn open(config: PpOcrConfig) -> Result<Self> {
        if config.detection_model.is_some() {
            return Err(VisionError::BackendNotImplemented(
                "ppocr-detection-opencv5",
            ));
        }
        if !(1..=256).contains(&config.recognition_height)
            || !(32..=8192).contains(&config.max_recognition_width)
        {
            return Err(VisionError::InvalidArgument(
                "invalid PP-OCR recognition dimensions".into(),
            ));
        }
        let dict_path = asset(&config.dictionary)?;
        let text = std::fs::read_to_string(&dict_path).map_err(|e| VisionError::Asset {
            path: dict_path,
            message: e.to_string(),
        })?;
        let mut dictionary: Vec<String> = text
            .trim_start_matches('\u{feff}')
            .lines()
            .map(str::to_owned)
            .collect();
        if dictionary.is_empty() || dictionary.iter().any(String::is_empty) {
            return Err(VisionError::InvalidArgument(
                "expected a nonempty UTF-8 CTC dictionary, one token per line".into(),
            ));
        }
        if config.append_space {
            dictionary.push(" ".into());
        }
        let recognition = Model::open(&config)?;
        if recognition.shape[2] > 0 && recognition.shape[2] != i64::from(config.recognition_height)
        {
            return Err(error("recognition height does not match model input"));
        }
        if recognition.shape[3] > i64::from(config.max_recognition_width) {
            return Err(error("fixed recognition width exceeds configured maximum"));
        }
        let profile = format!(
            "ppocr-ctc-opencv-dnn-new-image-triangle/v3; opencv={}; library={}; {config:?}",
            recognition.session.version(),
            recognition.session.path().display()
        );
        Ok(Self {
            config,
            recognition,
            dictionary,
            profile,
        })
    }

    fn recognize_line(
        &mut self,
        image: &Image,
        token: &CancellationToken,
    ) -> Result<(String, Option<f32>)> {
        let height = self.config.recognition_height;
        let natural_width = (f64::from(image.width()) * f64::from(height)
            / f64::from(image.height()))
        .ceil()
        .max(1.0) as u32;
        let width = if self.recognition.shape[3] > 0 {
            self.recognition.shape[3] as u32
        } else {
            natural_width.max(320.min(self.config.max_recognition_width))
        };
        if width > self.config.max_recognition_width {
            return Err(VisionError::InvalidArgument(
                "text line exceeds maximum recognition width; select a narrower ROI".into(),
            ));
        }
        let resized_width = natural_width.min(width);
        let resized = image::imageops::resize(
            &image.buffer(),
            resized_width,
            height,
            image::imageops::FilterType::Triangle,
        )
        .into_raw();
        check_cancelled(token)?;
        let mut data = vec![0.0; width as usize * height as usize * 3];
        let plane = width as usize * height as usize;
        for y in 0..height as usize {
            check_cancelled(token)?;
            for x in 0..resized_width as usize {
                for c in 0..3 {
                    data[c * plane + y * width as usize + x] =
                        f32::from(resized[(y * resized_width as usize + x) * 3 + 2 - c]) / 127.5
                            - 1.0;
                }
            }
        }
        let (shape, output) = self.recognition.run(data, height, width, token)?;
        decode_ctc(&output, &shape, &self.dictionary)
    }
}

impl OcrBackend for PpOcr {
    fn kind(&self) -> OcrBackendKind {
        OcrBackendKind::PpOcr
    }

    fn recognize(
        &mut self,
        image: &Image,
        region: Option<Rect>,
        token: &CancellationToken,
    ) -> Result<OcrResult> {
        check_cancelled(token)?;
        let selected = image.selected(region)?;
        let mut boxes = Vec::new();
        let (text, confidence) = self.recognize_line(&selected, token)?;
        if !text.is_empty() {
            boxes.push(OcrBox {
                text,
                confidence,
                polygon: rectangle_polygon(selected.bounds()),
            });
        }
        check_cancelled(token)?;
        let text = boxes
            .iter()
            .map(|s| s.text.as_str())
            .collect::<Vec<_>>()
            .join("\n");
        Ok(OcrResult {
            source: selected.source().cloned(),
            region: selected.bounds(),
            kind: self.kind(),
            text,
            boxes,
            profile: self.profile.clone(),
        })
    }
}

fn decode_ctc(
    values: &[f32],
    shape: &[i64],
    dictionary: &[String],
) -> Result<(String, Option<f32>)> {
    let classes = dictionary.len() + 1;
    if shape.len() != 3
        || shape[0] != 1
        || shape[1] <= 0
        || shape[2] != classes as i64
        || (shape[1] as usize).checked_mul(classes) != Some(values.len())
        || values
            .iter()
            .any(|p| !p.is_finite() || !(0.0..=1.0).contains(p))
    {
        return Err(VisionError::InvalidOutput("CTC requires [1,T,dictionary+blank] probabilities in 0..=1; check model/dictionary/space setting".into()));
    }
    let mut text = String::new();
    let (mut previous, mut sum, mut count) = (0, 0.0, 0);
    for row in values.chunks_exact(classes) {
        let (mut index, mut score) = (0, row[0]);
        for (i, p) in row.iter().enumerate().skip(1) {
            if *p > score {
                index = i;
                score = *p;
            }
        }
        if index != 0 && index != previous {
            text.push_str(&dictionary[index - 1]);
            sum += score;
            count += 1;
        }
        previous = index;
    }
    Ok((text, (count > 0).then(|| sum / count as f32)))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ctc_removes_adjacent_duplicates_but_blank_allows_repetition() {
        let dict = vec!["你".into(), "好".into()];
        let probabilities = [
            0.0, 0.9, 0.1, 0.0, 0.8, 0.2, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0,
        ];
        let (text, confidence) = decode_ctc(&probabilities, &[1, 5, 3], &dict).unwrap();
        assert_eq!(text, "你你好");
        assert!((confidence.unwrap() - 2.9 / 3.0).abs() < 0.00001);
        assert!(decode_ctc(&probabilities, &[1, 5, 4], &dict).is_err());
        assert!(decode_ctc(&[f32::NAN, 0.0, 1.0], &[1, 1, 3], &dict).is_err());
        assert_eq!(
            decode_ctc(&[1.0, 0.0, 0.0], &[1, 1, 3], &dict).unwrap(),
            (String::new(), None)
        );
    }

    #[test]
    fn detection_is_rejected_before_loading_any_assets_or_runtime() {
        let mut config = PpOcrConfig::new("missing".into(), "missing".into());
        config.detection_model = Some("missing".into());
        assert!(matches!(
            PpOcr::open(config),
            Err(VisionError::BackendNotImplemented(
                "ppocr-detection-opencv5"
            ))
        ));
    }
}
