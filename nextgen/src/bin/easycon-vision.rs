use std::{
    path::{Path, PathBuf},
    process::ExitCode,
};

use clap::{Parser, Subcommand, ValueEnum};
use easycon_sense::vision::MatchConfig;
use easycon_sense::vision::{
    self, Image, OcrBackendKind, OcrConfig, PpOcrConfig, Rect, Result, TemplateBackendKind,
    TemplateMode, TesseractConfig, VisionError,
};
use tokio_util::sync::CancellationToken;

#[derive(Parser)]
#[command(
    version,
    about = "Offline vision diagnostics (Native OpenCV >= 5 / Tesseract / PP-OCR DNN)"
)]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Clone, Copy, ValueEnum)]
enum Mode {
    SqDiff,
    SqDiffNormed,
    CCorr,
    CCorrNormed,
    CCoeff,
    CCoeffNormed,
}

impl From<Mode> for TemplateMode {
    fn from(value: Mode) -> Self {
        match value {
            Mode::SqDiff => Self::SqDiff,
            Mode::SqDiffNormed => Self::SqDiffNormed,
            Mode::CCorr => Self::CCorr,
            Mode::CCorrNormed => Self::CCorrNormed,
            Mode::CCoeff => Self::CCoeff,
            Mode::CCoeffNormed => Self::CCoeffNormed,
        }
    }
}

#[derive(Clone, Copy, ValueEnum)]
enum Backend {
    Tesseract,
    Ppocr,
}

#[derive(Subcommand)]
enum Command {
    /// Measure RGB channel statistics over one saved image or ROI.
    Stats {
        #[arg(long)]
        image: PathBuf,
        #[arg(long,value_parser=rect)]
        roi: Option<Rect>,
    },
    /// Measure RGB differences between two explicit images or equal-sized ROIs.
    Diff {
        #[arg(long)]
        left: PathBuf,
        #[arg(long)]
        right: PathBuf,
        #[arg(long,value_parser=rect)]
        left_roi: Option<Rect>,
        #[arg(long,value_parser=rect)]
        right_roi: Option<Rect>,
    },
    /// Search one saved image for one saved template, without capture or hardware access.
    Match {
        #[arg(long)]
        image: PathBuf,
        #[arg(long)]
        template: PathBuf,
        /// Directory containing the native OpenCV runtime and EasyCon bridge.
        #[arg(long)]
        opencv_dir: Option<PathBuf>,
        #[arg(long, value_enum, default_value = "sq-diff-normed")]
        mode: Mode,
        #[arg(long,value_parser=rect)]
        roi: Option<Rect>,
    },
    /// Recognize one saved image with an explicitly selected backend.
    Ocr {
        #[arg(long)]
        image: PathBuf,
        #[arg(long, value_enum)]
        backend: Backend,
        #[arg(long,value_parser=rect)]
        roi: Option<Rect>,
        #[arg(long, required_if_eq("backend", "tesseract"))]
        tessdata: Option<PathBuf>,
        #[arg(long, required_if_eq("backend", "tesseract"))]
        tesseract_library: Option<PathBuf>,
        #[arg(long, default_value = "eng")]
        language: String,
        #[arg(long,default_value_t=6,value_parser=clap::value_parser!(u8).range(3..=13))]
        psm: u8,
        /// Directory containing the OpenCV >= 5 runtime and DNN-capable EasyCon bridge.
        #[arg(long)]
        opencv_dir: Option<PathBuf>,
        #[arg(long, required_if_eq("backend", "ppocr"))]
        rec_model: Option<PathBuf>,
        #[arg(long, required_if_eq("backend", "ppocr"))]
        dictionary: Option<PathBuf>,
        /// Reserved text detection model; currently reports not implemented.
        #[arg(long)]
        det_model: Option<PathBuf>,
        #[arg(long, default_value_t = 48)]
        rec_height: u32,
        #[arg(long)]
        no_space: bool,
    },
}

fn rect(text: &str) -> std::result::Result<Rect, String> {
    let parts = text
        .split(',')
        .map(str::parse::<u32>)
        .collect::<std::result::Result<Vec<_>, _>>()
        .map_err(|e| e.to_string())?;
    let [x, y, width, height]: [u32; 4] = parts
        .try_into()
        .map_err(|_| "ROI must be x,y,width,height")?;
    Ok(Rect {
        x,
        y,
        width,
        height,
    })
}

fn load(path: &Path) -> Result<Image> {
    let pixels = image::open(path)
        .map_err(|e| VisionError::Asset {
            path: path.into(),
            message: e.to_string(),
        })?
        .to_rgb8();
    Image::new(pixels.width(), pixels.height(), pixels.into_raw())
}

fn execute(command: Command, token: &CancellationToken) -> Result<()> {
    if token.is_cancelled() {
        return Err(VisionError::Cancelled);
    }
    match command {
        Command::Stats { image, roi } => {
            let result = vision::measure::region_statistics(&load(&image)?, roi, token)?;
            println!("{result:#?}");
        }
        Command::Diff {
            left,
            right,
            left_roi,
            right_roi,
        } => {
            let result = vision::measure::image_difference(
                &load(&left)?,
                &load(&right)?,
                left_roi,
                right_roi,
                token,
            )?;
            println!("{result:#?}");
        }
        Command::Match {
            image,
            template,
            opencv_dir,
            mode,
            roi,
        } => {
            let mut matcher: Box<dyn vision::TemplateMatcher> = match opencv_dir {
                Some(path) => Box::new(vision::opencv::OpenCv5::open(path)?),
                None => vision::create_template_matcher(TemplateBackendKind::OpenCv5)?,
            };
            let result = matcher.match_template(
                &load(&image)?,
                &load(&template)?,
                MatchConfig {
                    mode: mode.into(),
                    region: roi,
                },
                token,
            )?;
            println!("{result:#?}");
        }
        Command::Ocr {
            image,
            backend,
            roi,
            tessdata,
            tesseract_library,
            language,
            psm,
            opencv_dir,
            rec_model,
            dictionary,
            det_model,
            rec_height,
            no_space,
        } => {
            let config = match backend {
                Backend::Tesseract => {
                    if opencv_dir.is_some()
                        || rec_model.is_some()
                        || dictionary.is_some()
                        || det_model.is_some()
                    {
                        return Err(VisionError::InvalidArgument(
                            "ONNX model options require --backend ppocr".into(),
                        ));
                    }
                    OcrConfig::Tesseract(TesseractConfig {
                        runtime_library: tesseract_library
                            .expect("clap required tesseract library"),
                        data_path: tessdata.expect("clap required tessdata"),
                        language,
                        page_segmentation: psm,
                    })
                }
                Backend::Ppocr => {
                    if tessdata.is_some() || tesseract_library.is_some() {
                        return Err(VisionError::InvalidArgument(
                            "--tessdata/--tesseract-library require --backend tesseract".into(),
                        ));
                    }
                    let mut config = PpOcrConfig::new(
                        rec_model.expect("clap required model"),
                        dictionary.expect("clap required dictionary"),
                    );
                    config.opencv_dir = opencv_dir;
                    config.detection_model = det_model;
                    config.recognition_height = rec_height;
                    config.append_space = !no_space;
                    OcrConfig::PpOcr(config)
                }
            };
            let mut engine = vision::create_ocr(config)?;
            let kind: OcrBackendKind = engine.kind();
            let result = engine.recognize(&load(&image)?, roi, token)?;
            println!("backend={kind:?}\n{result:#?}");
        }
    }
    Ok(())
}

#[tokio::main]
async fn main() -> ExitCode {
    let command = Cli::parse().command;
    let token = CancellationToken::new();
    let worker_token = token.clone();
    let mut worker = tokio::task::spawn_blocking(move || execute(command, &worker_token));
    let joined = |result: std::result::Result<Result<()>, tokio::task::JoinError>| {
        result.unwrap_or_else(|e| {
            Err(VisionError::Backend {
                backend: "diagnostic",
                message: e.to_string(),
            })
        })
    };
    let result = tokio::select! {
        result = &mut worker => joined(result),
        signal = tokio::signal::ctrl_c() => {
            token.cancel();
            let result = joined(worker.await);
            signal.map_err(|e| VisionError::Backend {backend:"diagnostic",message:e.to_string()}).and(result)
        }
    };
    match result {
        Ok(()) => ExitCode::SUCCESS,
        Err(VisionError::Cancelled) => ExitCode::from(130),
        Err(error) => {
            eprintln!("{error}");
            ExitCode::FAILURE
        }
    }
}
