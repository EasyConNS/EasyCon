use easycon_tesseract_runtime::Runtime;
use tokio_util::sync::CancellationToken;

use super::{
    Image, OcrBackend, OcrBackendKind, OcrBox, OcrResult, Rect, Result, TesseractConfig,
    VisionError, check_cancelled, rectangle_polygon,
};

pub struct Tesseract {
    engine: Runtime,
    profile: String,
}

impl Tesseract {
    pub fn open(config: TesseractConfig) -> Result<Self> {
        if !(3..=13).contains(&config.page_segmentation)
            || config.language.is_empty()
            || !config
                .language
                .chars()
                .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '+')
        {
            return Err(VisionError::InvalidArgument(
                "expected language codes joined by '+' and text PSM 3..=13".into(),
            ));
        }
        for language in config.language.split('+') {
            let path = config.data_path.join(format!("{language}.traineddata"));
            if language.is_empty() || !path.is_file() {
                return Err(VisionError::Asset {
                    path,
                    message: "missing traineddata file".into(),
                });
            }
        }
        let path = config
            .data_path
            .to_str()
            .filter(|p| !p.contains('\0'))
            .ok_or_else(|| {
                VisionError::InvalidArgument("tessdata path must be UTF-8 without NUL".into())
            })?;
        let mut engine = Runtime::open(
            &config.runtime_library,
            std::path::Path::new(path),
            &config.language,
        )
        .map_err(|e| VisionError::backend("tesseract", e))?;
        engine
            .set_variable(
                "tessedit_pageseg_mode",
                &config.page_segmentation.to_string(),
            )
            .map_err(|e| VisionError::backend("tesseract", e))?;
        engine
            .set_variable("classify_enable_learning", "0")
            .map_err(|e| VisionError::backend("tesseract", e))?;
        engine
            .set_variable("tessedit_enable_doc_dict", "0")
            .map_err(|e| VisionError::backend("tesseract", e))?;
        let profile = format!(
            "tesseract-capi/{}; runtime={}; tessdata={}; language={}; psm={}",
            engine.version(),
            config.runtime_library.display(),
            config.data_path.display(),
            config.language,
            config.page_segmentation
        );
        Ok(Self { engine, profile })
    }
}

impl OcrBackend for Tesseract {
    fn kind(&self) -> OcrBackendKind {
        OcrBackendKind::Tesseract
    }

    fn recognize(
        &mut self,
        image: &Image,
        region: Option<Rect>,
        token: &CancellationToken,
    ) -> Result<OcrResult> {
        check_cancelled(token)?;
        let selected = image.selected(region)?;
        let result = self
            .engine
            .recognize_rgb(selected.width(), selected.height(), selected.rgb())
            .map_err(|e| VisionError::backend("tesseract", e))?;
        check_cancelled(token)?;
        let boxes = parse_tsv(&result.tsv, selected.bounds())?;
        check_cancelled(token)?;
        Ok(OcrResult {
            source: selected.source().cloned(),
            region: selected.bounds(),
            kind: self.kind(),
            text: result.text,
            boxes,
            profile: self.profile.clone(),
        })
    }
}

fn parse_tsv(tsv: &str, region: Rect) -> Result<Vec<OcrBox>> {
    let mut boxes = Vec::new();
    for line in tsv.lines().skip(1) {
        let fields: Vec<_> = line.splitn(12, '\t').collect();
        if fields.len() != 12 {
            return Err(VisionError::InvalidOutput(
                "malformed Tesseract TSV row".into(),
            ));
        }
        if fields[0] != "5" || fields[11].is_empty() {
            continue;
        }
        let number = |i: usize| {
            fields[i]
                .parse::<u32>()
                .map_err(|_| VisionError::InvalidOutput("invalid Tesseract word box".into()))
        };
        let local = Rect {
            x: number(6)?,
            y: number(7)?,
            width: number(8)?,
            height: number(9)?,
        }
        .validate(region.width, region.height)
        .map_err(|e| VisionError::InvalidOutput(e.to_string()))?;
        let confidence = fields[10]
            .parse::<f32>()
            .map_err(|_| VisionError::InvalidOutput("invalid Tesseract confidence".into()))?;
        if !confidence.is_finite() || !(-1.0..=100.0).contains(&confidence) {
            return Err(VisionError::InvalidOutput(
                "Tesseract confidence outside -1..=100".into(),
            ));
        }
        boxes.push(OcrBox {
            text: fields[11].into(),
            confidence: (confidence >= 0.0).then_some(confidence / 100.0),
            polygon: rectangle_polygon(Rect {
                x: region.x + local.x,
                y: region.y + local.y,
                ..local
            }),
        });
    }
    Ok(boxes)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tsv_keeps_words_confidence_and_translates_roi_coordinates() {
        let tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n1\t1\t0\t0\t0\t0\t0\t0\t90\t40\t-1\t\n5\t1\t1\t1\t1\t1\t3\t4\t10\t12\t93.5\t你好\n";
        let boxes = parse_tsv(
            tsv,
            Rect {
                x: 100,
                y: 200,
                width: 90,
                height: 40,
            },
        )
        .unwrap();
        assert_eq!(boxes.len(), 1);
        assert_eq!(boxes[0].text, "你好");
        assert_eq!(boxes[0].confidence, Some(0.935));
        assert_eq!(
            boxes[0].polygon[0],
            super::super::Point { x: 103.0, y: 204.0 }
        );
        assert!(
            parse_tsv(
                "header\nbad\n",
                Rect {
                    x: 0,
                    y: 0,
                    width: 1,
                    height: 1
                }
            )
            .is_err()
        );
    }
}
