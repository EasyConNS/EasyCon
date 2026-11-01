use super::*;
use crate::{SessionId, video::VideoFormat};

fn frame() -> VideoFrame {
    VideoFrame {
        id: SampleId {
            source: "fake-camera".into(),
            session: SessionId(42),
            sequence: 7,
        },
        time: SampleTime {
            received_ns: 123,
            source_ns: Some(100),
        },
        format: VideoFormat {
            width: 4,
            height: 3,
            frames_per_second: 30,
            encoding: "RGB3".into(),
        },
        rgb: (0..36).collect::<Vec<u8>>().into(),
    }
}

#[test]
fn image_crop_preserves_source_pixels_and_original_coordinates() {
    let frame = frame();
    let image = Image::from_frame(&frame).unwrap();
    let crop = image
        .crop(Rect {
            x: 1,
            y: 1,
            width: 3,
            height: 2,
        })
        .unwrap();
    let nested = crop
        .crop(Rect {
            x: 1,
            y: 0,
            width: 1,
            height: 2,
        })
        .unwrap();
    assert_eq!(
        nested.bounds(),
        Rect {
            x: 2,
            y: 1,
            width: 1,
            height: 2
        }
    );
    assert_eq!(nested.rgb(), &[18, 19, 20, 30, 31, 32]);
    assert_eq!(nested.source().unwrap().id, frame.id);
    assert_eq!(nested.source().unwrap().time, frame.time);
    assert_eq!(nested.source().unwrap().width, 4);
    assert_eq!(nested.pixel(0, 1).unwrap(), [30, 31, 32]);
    assert_eq!(image.rgb(), frame.rgb.as_ref());
    assert!(nested.pixel(1, 0).is_err());
}

#[test]
fn invalid_images_and_overflowing_rois_are_rejected() {
    assert!(Image::new(0, 1, vec![]).is_err());
    assert!(Image::new(1, 1, vec![0; 4]).is_err());
    assert!(Image::new(u32::MAX, u32::MAX, vec![]).is_err());
    let image = Image::from_frame(&frame()).unwrap();
    for region in [
        Rect {
            x: 0,
            y: 0,
            width: 0,
            height: 1,
        },
        Rect {
            x: 4,
            y: 0,
            width: 1,
            height: 1,
        },
        Rect {
            x: u32::MAX,
            y: 0,
            width: 2,
            height: 1,
        },
        Rect {
            x: 0,
            y: 2,
            width: 1,
            height: 2,
        },
    ] {
        assert!(image.crop(region).is_err());
    }
}

#[cfg(not(feature = "ocr-tesseract"))]
#[test]
fn unavailable_tesseract_does_not_silently_choose_another_backend() {
    let result = create_ocr(OcrConfig::Tesseract(TesseractConfig {
        runtime_library: "missing".into(),
        data_path: "missing".into(),
        language: "eng".into(),
        page_segmentation: 6,
    }));
    assert!(matches!(
        result,
        Err(VisionError::BackendUnavailable("tesseract"))
    ));
}

#[cfg(not(feature = "ocr-ppocr"))]
#[test]
fn unavailable_ppocr_does_not_silently_choose_another_backend() {
    let result = create_ocr(OcrConfig::PpOcr(PpOcrConfig::new(
        "missing".into(),
        "missing".into(),
    )));
    assert!(matches!(
        result,
        Err(VisionError::BackendUnavailable("ppocr"))
    ));
}

#[test]
fn missing_native_opencv_is_an_error_without_backend_fallback() {
    assert!(matches!(
        opencv::OpenCv5::open("missing-native-opencv-directory"),
        Err(VisionError::Backend {
            backend: "opencv-native",
            ..
        })
    ));
}

#[test]
fn matcher_interface_accepts_a_mock_and_preserves_structured_results() {
    struct Mock;
    impl TemplateMatcher for Mock {
        fn kind(&self) -> TemplateBackendKind {
            TemplateBackendKind::OpenCv5
        }
        fn match_template(
            &mut self,
            image: &Image,
            _: &Image,
            config: MatchConfig,
            token: &CancellationToken,
        ) -> Result<TemplateMatch> {
            check_cancelled(token)?;
            let search = image.bounds();
            Ok(TemplateMatch {
                source: image.source().cloned(),
                search,
                bounds: Rect {
                    x: search.x,
                    y: search.y,
                    width: 1,
                    height: 1,
                },
                mode: config.mode,
                raw_score: 0.95,
                confidence: Some(0.05),
                implementation: "mock".into(),
            })
        }
    }
    let frame = frame();
    let image = Image::from_frame(&frame)
        .unwrap()
        .crop(Rect {
            x: 1,
            y: 1,
            width: 2,
            height: 2,
        })
        .unwrap();
    let mut matcher: Box<dyn TemplateMatcher> = Box::new(Mock);
    let result = matcher
        .match_template(
            &image,
            &image,
            MatchConfig::default(),
            &CancellationToken::new(),
        )
        .unwrap();
    assert_eq!(result.source.unwrap().id, frame.id);
    assert_eq!(result.search, image.bounds());
    assert_eq!((result.bounds.x, result.bounds.y), (1, 1));
    assert_eq!(result.confidence, Some(0.05));
    assert_eq!(result.implementation, "mock");
}

#[cfg(any(feature = "ocr-tesseract", feature = "ocr-ppocr"))]
mod native {
    use super::*;

    fn text_image() -> Image {
        let pixels = image::load_from_memory(include_bytes!("../../tests/fixtures/hello.png"))
            .unwrap()
            .to_rgb8();
        Image::new(pixels.width(), pixels.height(), pixels.into_raw()).unwrap()
    }

    #[cfg(feature = "ocr-tesseract")]
    #[test]
    #[ignore = "requires EASYCON_TESSERACT_LIBRARY, EASYCON_TESSDATA and eng.traineddata"]
    fn real_tesseract_recognizes_text_and_returns_word_boxes() {
        let config = OcrConfig::Tesseract(TesseractConfig {
            runtime_library: std::env::var_os("EASYCON_TESSERACT_LIBRARY")
                .expect("EASYCON_TESSERACT_LIBRARY")
                .into(),
            data_path: std::env::var_os("EASYCON_TESSDATA")
                .expect("EASYCON_TESSDATA")
                .into(),
            language: "eng".into(),
            page_segmentation: 7,
        });
        let mut engine = create_ocr(config.clone()).unwrap();
        let result = engine
            .recognize(&text_image(), None, &CancellationToken::new())
            .unwrap();
        assert_eq!(
            result.text.split_whitespace().collect::<String>(),
            "HELLO123"
        );
        assert!(!result.boxes.is_empty());
        assert!(result.boxes.iter().all(|s| s.confidence.is_some()));
        let blank = Image::new(350, 90, vec![255; 350 * 90 * 3]).unwrap();
        let after_text = engine
            .recognize(&blank, None, &CancellationToken::new())
            .unwrap();
        // Single-line PSM may hallucinate on blank input; compare a fresh engine for stale state.
        let baseline = create_ocr(config)
            .unwrap()
            .recognize(&blank, None, &CancellationToken::new())
            .unwrap();
        assert_eq!(after_text.text, baseline.text);
        assert_eq!(after_text.boxes.len(), baseline.boxes.len());
        let cancelled = CancellationToken::new();
        cancelled.cancel();
        assert!(matches!(
            engine.recognize(&text_image(), None, &cancelled),
            Err(VisionError::Cancelled)
        ));
        let repeated = engine
            .recognize(&text_image(), None, &CancellationToken::new())
            .unwrap();
        assert_eq!(repeated.text, result.text);
    }

    #[cfg(feature = "ocr-ppocr")]
    fn ppocr_config() -> PpOcrConfig {
        let value = |key| std::env::var_os(key).expect(key).into();
        PpOcrConfig::new(value("EASYCON_PPOCR_REC"), value("EASYCON_PPOCR_DICT"))
    }

    #[cfg(feature = "ocr-ppocr")]
    #[test]
    #[ignore = "requires native OpenCV >= 5 DNN, PP-OCR recognition model and dictionary"]
    fn real_ppocr_recognizes_single_line() {
        let mut engine = create_ocr(OcrConfig::PpOcr(ppocr_config())).unwrap();
        let result = engine
            .recognize(
                &text_image(),
                Some(Rect {
                    x: 8,
                    y: 25,
                    width: 330,
                    height: 45,
                }),
                &CancellationToken::new(),
            )
            .unwrap();
        // A model smoke test, not an accuracy benchmark for synthetic Hershey glyphs.
        assert!(
            result
                .text
                .split_whitespace()
                .collect::<String>()
                .contains("HELLO123"),
            "{result:?}"
        );
        assert_eq!(result.boxes.len(), 1);
        assert!((0.0..=1.0).contains(&result.boxes[0].confidence.unwrap()));
        assert_eq!(result.boxes[0].polygon[0], Point { x: 8.0, y: 25.0 });
        assert!(result.profile.starts_with("ppocr-ctc-opencv-dnn-new-"));
        let bridge = opencv::runtime_directory(None)
            .unwrap()
            .join(opencv::bridge_filename());
        for bytes in [&[][..], &b"not an ONNX model"[..]] {
            assert!(matches!(
                easycon_opencv_runtime::dnn::Dnn::open(&bridge, bytes),
                Err(easycon_opencv_runtime::RuntimeError::Native(_))
            ));
        }
    }

    fn fixture(bytes: &[u8]) -> Image {
        let pixels = image::load_from_memory(bytes).unwrap().to_rgb8();
        Image::new(pixels.width(), pixels.height(), pixels.into_raw()).unwrap()
    }

    fn normalized(text: &str) -> String {
        text.split_whitespace().collect()
    }

    fn regression_corpus(engine: &mut dyn OcrBackend) {
        let cases: &[(&str, &[u8], &str)] = &[
            (
                "english",
                include_bytes!("../../tests/fixtures/ocr-english.png"),
                "EasyCon 2026",
            ),
            (
                "chinese",
                include_bytes!("../../tests/fixtures/ocr-chinese.png"),
                "你好世界123",
            ),
            (
                "mixed",
                include_bytes!("../../tests/fixtures/ocr-mixed.png"),
                "自动化测试 EasyCon 123",
            ),
        ];
        let token = CancellationToken::new();
        for (name, bytes, expected) in cases {
            let image = fixture(bytes);
            let region = Rect {
                x: 8,
                y: 5,
                width: image.width() - 16,
                height: 60,
            };
            let start = std::time::Instant::now();
            let result = engine.recognize(&image, Some(region), &token).unwrap();
            eprintln!(
                "{:?}/{name}: {:?}, {:?}",
                engine.kind(),
                result.text.trim(),
                start.elapsed()
            );
            assert_eq!(
                normalized(&result.text),
                normalized(expected),
                "{name}: {result:?}"
            );
            assert_eq!(result.region, region);
            assert!(result.source.is_none());
            assert!(!result.boxes.is_empty());
            for span in &result.boxes {
                assert!(span.confidence.is_some_and(|c| (0.0..=1.0).contains(&c)));
                for point in span.polygon {
                    assert!(
                        (region.x as f32..=(region.x + region.width) as f32).contains(&point.x)
                    );
                    assert!(
                        (region.y as f32..=(region.y + region.height) as f32).contains(&point.y)
                    );
                }
            }
            let crop = image.crop(region).unwrap();
            let nested = engine
                .recognize(
                    &crop,
                    Some(Rect {
                        x: 0,
                        y: 0,
                        width: crop.width(),
                        height: crop.height(),
                    }),
                    &token,
                )
                .unwrap();
            assert_eq!(nested.region, region);
            assert_eq!(nested.text, result.text);
            assert_eq!(nested.boxes[0].polygon, result.boxes[0].polygon);
            assert!(matches!(
                engine.recognize(
                    &image,
                    Some(Rect {
                        x: image.width(),
                        y: 0,
                        width: 1,
                        height: 1
                    }),
                    &token
                ),
                Err(VisionError::InvalidArgument(_))
            ));
            let cancelled = CancellationToken::new();
            cancelled.cancel();
            assert!(matches!(
                engine.recognize(&image, None, &cancelled),
                Err(VisionError::Cancelled)
            ));
            let again = engine.recognize(&image, Some(region), &token).unwrap();
            assert_eq!(again.text, result.text);
        }
        let blank = Image::new(320, 48, vec![255; 320 * 48 * 3]).unwrap();
        let result = engine.recognize(&blank, None, &token).unwrap();
        assert!(result.text.trim().is_empty(), "blank: {result:?}");
        assert!(result.boxes.is_empty());
        // A blank call must not leak state into the next recognition.
        let image = fixture(cases[0].1);
        let mut sample = frame();
        sample.format.width = image.width();
        sample.format.height = image.height();
        sample.rgb = image.rgb().into();
        let image = Image::from_frame(&sample).unwrap();
        let result = engine.recognize(&image, None, &token).unwrap();
        assert_eq!(result.source.as_ref(), image.source());
        assert_eq!(normalized(&result.text), normalized(cases[0].2));
    }

    #[cfg(feature = "ocr-tesseract")]
    #[test]
    #[ignore = "requires dynamic Tesseract library and eng+chi_sim traineddata"]
    fn real_tesseract_regression_corpus() {
        let mut engine = create_ocr(OcrConfig::Tesseract(TesseractConfig {
            runtime_library: std::env::var_os("EASYCON_TESSERACT_LIBRARY")
                .expect("EASYCON_TESSERACT_LIBRARY")
                .into(),
            data_path: std::env::var_os("EASYCON_TESSDATA")
                .expect("EASYCON_TESSDATA")
                .into(),
            language: "eng+chi_sim".into(),
            page_segmentation: 6,
        }))
        .unwrap();
        regression_corpus(engine.as_mut());
    }

    #[cfg(feature = "ocr-ppocr")]
    #[test]
    #[ignore = "requires OpenCV >= 5 DNN and real PP-OCR recognition model/dictionary"]
    fn real_ppocr_regression_corpus() {
        let mut engine = create_ocr(OcrConfig::PpOcr(ppocr_config())).unwrap();
        regression_corpus(engine.as_mut());

        // Cancel a long request after entry, then reuse the same native session.
        let image = Image::new(2048, 48, vec![255; 2048 * 48 * 3]).unwrap();
        let token = CancellationToken::new();
        let signal = token.clone();
        let cancel = std::thread::spawn(move || {
            std::thread::sleep(std::time::Duration::from_millis(30));
            signal.cancel();
        });
        let result = engine.recognize(&image, None, &token);
        cancel.join().unwrap();
        assert!(matches!(result, Err(VisionError::Cancelled)), "{result:?}");
        let result = engine
            .recognize(&text_image(), None, &CancellationToken::new())
            .unwrap();
        assert_eq!(normalized(&result.text), "HELLO123");

        let mut config = ppocr_config();
        config.append_space = false;
        let mut mismatched = create_ocr(OcrConfig::PpOcr(config)).unwrap();
        assert!(matches!(
            mismatched.recognize(&text_image(), None, &CancellationToken::new()),
            Err(VisionError::InvalidOutput(_))
        ));
    }

    #[cfg(feature = "ocr-ppocr")]
    #[test]
    #[ignore = "requires EASYCON_OCR_GAME_ASSETS and real PP-OCR model/runtime"]
    fn real_ppocr_game_screenshot_rois() {
        let assets = std::path::PathBuf::from(
            std::env::var_os("EASYCON_OCR_GAME_ASSETS").expect("EASYCON_OCR_GAME_ASSETS"),
        );
        let mut engine = create_ocr(OcrConfig::PpOcr(ppocr_config())).unwrap();
        let cases = [
            (
                "ppocr3.png",
                Rect {
                    x: 1260,
                    y: 234,
                    width: 330,
                    height: 81,
                },
                "TAUROS",
            ),
            (
                "ppocr2.png",
                Rect {
                    x: 640,
                    y: 847,
                    width: 490,
                    height: 83,
                },
                "CHLOROPHYLL",
            ),
            (
                "ppocr.png",
                Rect {
                    x: 1130,
                    y: 625,
                    width: 610,
                    height: 75,
                },
                "がくしゅうそうち",
            ),
        ];
        for (name, region, expected) in cases {
            let pixels = image::open(assets.join(name)).unwrap().to_rgb8();
            let image = Image::new(pixels.width(), pixels.height(), pixels.into_raw()).unwrap();
            let result = engine
                .recognize(&image, Some(region), &CancellationToken::new())
                .unwrap();
            eprintln!("game/{name}: {:?}", result.text);
            assert_eq!(normalized(&result.text), expected, "{result:?}");
            assert_eq!(result.region, region);
            assert_eq!(
                result.boxes[0].polygon[0],
                Point {
                    x: region.x as f32,
                    y: region.y as f32
                }
            );
        }
    }
}

#[test]
#[ignore = "requires native OpenCV >= 5 runtime and EasyCon bridge; see native/README.md"]
fn real_opencv_matches_six_modes_and_preserves_coordinates_and_source() {
    let bits = [
        [1, 0, 1, 0, 1, 0, 0, 1, 0],
        [0, 1, 1, 1, 0, 0, 1, 0, 0],
        [1, 0, 0, 1, 0, 1, 0, 0, 1],
    ];
    let mut rgb = vec![0; 8 * 6 * 3];
    for y in 0..3 {
        for x in 0..3 {
            for channel in 0..3 {
                rgb[((y + 2) * 8 + x + 3) * 3 + channel] = bits[channel][y * 3 + x] * 200;
            }
        }
    }
    let image = Image::new(8, 6, rgb).unwrap();
    let template = image
        .crop(Rect {
            x: 3,
            y: 2,
            width: 3,
            height: 3,
        })
        .unwrap();
    let mut matcher = create_template_matcher(TemplateBackendKind::OpenCv5).unwrap();
    let token = CancellationToken::new();
    for mode in [
        TemplateMode::SqDiff,
        TemplateMode::SqDiffNormed,
        TemplateMode::CCorr,
        TemplateMode::CCorrNormed,
        TemplateMode::CCoeff,
        TemplateMode::CCoeffNormed,
    ] {
        let result = matcher
            .match_template(
                &image,
                &template,
                MatchConfig { mode, region: None },
                &token,
            )
            .unwrap();
        assert_eq!(result.bounds, template.bounds());
        assert_eq!(result.mode, mode);
        assert!(result.raw_score.is_finite());
        assert!(result.implementation.starts_with("opencv-native/"));
        match mode {
            TemplateMode::SqDiffNormed | TemplateMode::CCorrNormed | TemplateMode::CCoeffNormed => {
                assert!(result.confidence.unwrap() > 0.999)
            }
            _ => assert_eq!(result.confidence, None),
        }
    }
    let crop = image
        .crop(Rect {
            x: 1,
            y: 1,
            width: 7,
            height: 5,
        })
        .unwrap();
    let result = matcher
        .match_template(&crop, &template, MatchConfig::default(), &token)
        .unwrap();
    assert_eq!(result.bounds, template.bounds());
    assert!(result.confidence.unwrap() > 0.999);
    let captured = frame();
    let image = Image::from_frame(&captured).unwrap();
    let result = matcher
        .match_template(&image, &image, MatchConfig::default(), &token)
        .unwrap();
    assert_eq!(result.source.unwrap().id, captured.id);
    assert!(matches!(
        matcher.match_template(&template, &image, MatchConfig::default(), &token),
        Err(VisionError::InvalidArgument(_))
    ));
    let flat = Image::new(2, 2, vec![7; 12]).unwrap();
    assert!(matches!(
        matcher.match_template(
            &image,
            &flat,
            MatchConfig {
                mode: TemplateMode::CCoeffNormed,
                region: None
            },
            &token
        ),
        Err(VisionError::InvalidArgument(_))
    ));
    token.cancel();
    assert!(matches!(
        matcher.match_template(&image, &image, MatchConfig::default(), &token),
        Err(VisionError::Cancelled)
    ));
}

#[test]
#[ignore = "requires native OpenCV >= 5 runtime and EasyCon bridge"]
fn real_opencv_low_similarity_is_a_successful_measurement() {
    let image = Image::new(1, 1, vec![0; 3]).unwrap();
    let template = Image::new(1, 1, vec![255; 3]).unwrap();
    let result = create_template_matcher(TemplateBackendKind::OpenCv5)
        .unwrap()
        .match_template(
            &image,
            &template,
            MatchConfig::default(),
            &CancellationToken::new(),
        )
        .unwrap();
    assert_eq!(result.confidence, Some(0.0));
}

#[test]
fn ocr_confidence_is_mean_of_all_boxes_and_unknown_for_empty_or_partial_scores() {
    let mut result = OcrResult {
        source: None,
        region: Rect {
            x: 0,
            y: 0,
            width: 1,
            height: 1,
        },
        kind: OcrBackendKind::Tesseract,
        text: String::new(),
        boxes: vec![],
        profile: "test".into(),
    };
    assert_eq!(result.confidence(), None);
    for score in [Some(0.25), Some(0.75)] {
        result.boxes.push(OcrBox {
            text: "x".into(),
            confidence: score,
            polygon: [Point { x: 0.0, y: 0.0 }; 4],
        });
    }
    assert_eq!(result.confidence(), Some(0.5));
    result.boxes[0].confidence = None;
    assert_eq!(result.confidence(), None);
}
