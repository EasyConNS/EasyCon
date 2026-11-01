use super::*;

#[test]
fn catalogue_declares_resource_ownership_and_operation_completion() {
    for id in PrimitiveId::ALL {
        let contract = id.contract();
        assert_eq!(
            contract.provider_ownership,
            if contract.capability.is_some() {
                ProviderOwnership::Borrowed
            } else {
                ProviderOwnership::None
            }
        );
        assert_eq!(
            contract.output_ownership,
            if matches!(id, PrimitiveId::Capture | PrimitiveId::ReadAudio) {
                OutputOwnership::SharedSample
            } else {
                OutputOwnership::Value
            }
        );
        if contract.kind == Kind::Pure {
            assert_eq!(contract.completion, Completion::Computed);
        }
    }
    for (id, expected) in [
        (PrimitiveId::Capture, Completion::SampleAcquired),
        (PrimitiveId::ReadAudio, Completion::SubscriptionAdvanced),
        (PrimitiveId::PadPress, Completion::InputDelivered),
        (PrimitiveId::TypeText, Completion::InputDelivered),
        (PrimitiveId::Log, Completion::LogSubmitted),
    ] {
        assert_eq!(id.contract().completion, expected);
    }
    for id in [PrimitiveId::PadPress, PrimitiveId::PadRelease] {
        assert_eq!(id.contract().inputs[1].ty, "NonEmptyList<Button>");
    }
}

#[tokio::test]
async fn invalid_selections_and_regions_never_reach_injected_providers() {
    let mut pad = PadMock::new();
    let mut matcher = MatcherMock(0);
    let mut ocr = OcrMock(0);
    let image = Image::from_frame(&frame()).unwrap();
    let oversized = Image::new(2, 1, vec![0; 6]).unwrap();
    let invalid = Some(Rect {
        x: u32::MAX,
        y: 0,
        width: 1,
        height: 1,
    });
    let mut host = PrimitiveHost::default();
    host.pads.insert("pad".into(), &mut pad);
    host.matchers.insert("matcher".into(), &mut matcher);
    host.ocr.insert("ocr".into(), &mut ocr);
    for call in [
        PrimitiveCall::PadPress {
            target: "pad",
            buttons: &[],
        },
        PrimitiveCall::PadRelease {
            target: "pad",
            buttons: &[],
        },
        PrimitiveCall::MatchTemplate {
            matcher: "matcher",
            image: &image,
            template: &oversized,
            config: MatchConfig::default(),
        },
        PrimitiveCall::MatchTemplate {
            matcher: "matcher",
            image: &image,
            template: &image,
            config: MatchConfig {
                region: invalid,
                ..MatchConfig::default()
            },
        },
        PrimitiveCall::Ocr {
            kind: OcrBackendKind::Tesseract,
            engine: "ocr",
            image: ImageInput::Image(&image),
            region: invalid,
        },
    ] {
        assert_eq!(
            invoke(&mut host, call, &CancellationToken::new())
                .await
                .unwrap_err()
                .kind(),
            ErrorKind::InvalidArgument
        );
    }
    drop(host);
    assert!(pad.reports.is_empty());
    assert_eq!(matcher.0, 0);
    assert_eq!(ocr.0, 0);
}

#[tokio::test]
async fn malformed_media_results_are_reported_once_without_recapture() {
    let token = CancellationToken::new();
    let after = frame().id.clone();
    for (returned, expected) in [
        (frame(), ErrorKind::InvalidData), // Same frame is not newer than the cursor.
        (
            Arc::new(VideoFrame {
                id: SampleId {
                    session: SessionId(999),
                    ..after.clone()
                },
                ..Arc::try_unwrap(frame()).unwrap()
            }),
            ErrorKind::SourceChanged,
        ),
        (
            Arc::new(VideoFrame {
                rgb: vec![0].into(),
                ..Arc::try_unwrap(frame()).unwrap()
            }),
            ErrorKind::InvalidData,
        ),
    ] {
        let mut video = VideoMock {
            frame: returned,
            after: vec![],
        };
        let mut host = PrimitiveHost::default();
        host.videos.insert("video".into(), &mut video);
        assert_eq!(
            invoke(
                &mut host,
                PrimitiveCall::Capture {
                    source: "video",
                    after: Some(&after)
                },
                &token
            )
            .await
            .unwrap_err()
            .kind(),
            expected
        );
        assert_eq!(video.after.len(), 1);
    }
    for returned in [
        window(), // Two frames cannot satisfy a three-frame request.
        Arc::new(AudioWindow {
            channels: 0,
            ..Arc::try_unwrap(window()).unwrap()
        }),
        Arc::new(AudioWindow {
            sample_rate: 0,
            ..Arc::try_unwrap(window()).unwrap()
        }),
        Arc::new(AudioWindow {
            samples: vec![0.0; 5].into(),
            ..Arc::try_unwrap(window()).unwrap()
        }),
    ] {
        let mut audio = AudioMock {
            window: returned,
            frames: vec![],
        };
        let mut host = PrimitiveHost::default();
        host.audio.insert("audio".into(), &mut audio);
        assert_eq!(
            invoke(
                &mut host,
                PrimitiveCall::ReadAudio {
                    source: "audio",
                    frames: 3
                },
                &token
            )
            .await
            .unwrap_err()
            .kind(),
            ErrorKind::InvalidData
        );
        assert_eq!(audio.frames, [3]);
    }
}

struct FixedMatcher {
    result: TemplateMatch,
    calls: usize,
}
impl TemplateMatcher for FixedMatcher {
    fn kind(&self) -> TemplateBackendKind {
        TemplateBackendKind::OpenCv5
    }
    fn match_template(
        &mut self,
        _: &Image,
        _: &Image,
        _: MatchConfig,
        _: &CancellationToken,
    ) -> vision::Result<TemplateMatch> {
        self.calls += 1;
        Ok(self.result.clone())
    }
}
struct FixedOcr {
    result: OcrResult,
    calls: usize,
}
impl OcrBackend for FixedOcr {
    fn kind(&self) -> OcrBackendKind {
        OcrBackendKind::Tesseract
    }
    fn recognize(
        &mut self,
        _: &Image,
        _: Option<Rect>,
        _: &CancellationToken,
    ) -> vision::Result<OcrResult> {
        self.calls += 1;
        Ok(self.result.clone())
    }
}

fn cropped_image() -> Image {
    let frame = VideoFrame {
        format: VideoFormat {
            width: 6,
            height: 5,
            ..frame().format.clone()
        },
        rgb: vec![0; 6 * 5 * 3].into(),
        ..Arc::try_unwrap(frame()).unwrap()
    };
    Image::from_frame(&frame)
        .unwrap()
        .crop(Rect {
            x: 1,
            y: 1,
            width: 4,
            height: 3,
        })
        .unwrap()
}

#[test]
fn score_mapping_is_shared_finite_and_explicit_for_all_six_modes() {
    use vision::TemplateMode::*;
    for (mode, score, expected) in [
        (SqDiff, 200.0, None),
        (CCorr, 200.0, None),
        (CCoeff, -200.0, None),
        (SqDiffNormed, 0.25, Some(0.75)),
        (CCorrNormed, 0.25, Some(0.25)),
        (CCoeffNormed, -0.5, Some(0.25)),
        (CCoeffNormed, -1.0, Some(0.0)),
        (CCoeffNormed, 1.0, Some(1.0)),
        (SqDiffNormed, 1.000001, Some(0.0)),
    ] {
        assert_eq!(mode.confidence(score).unwrap(), expected);
        for nonfinite in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
            assert!(matches!(
                mode.confidence(nonfinite),
                Err(VisionError::InvalidOutput(_))
            ));
        }
    }
}

#[tokio::test]
async fn template_results_preserve_nested_coordinates_and_reject_invalid_provider_values() {
    let image = cropped_image();
    let template = Image::new(1, 1, vec![1, 2, 3]).unwrap();
    let local = Rect {
        x: 1,
        y: 1,
        width: 2,
        height: 2,
    };
    let config = MatchConfig {
        region: Some(local),
        ..MatchConfig::default()
    };
    let valid = TemplateMatch {
        source: image.source().cloned(),
        search: Rect {
            x: 2,
            y: 2,
            width: 2,
            height: 2,
        },
        bounds: Rect {
            x: 3,
            y: 3,
            width: 1,
            height: 1,
        },
        mode: config.mode,
        raw_score: 1.0,
        confidence: Some(0.0),
        implementation: "mock/v1".into(),
    };
    let mut cases = vec![(valid.clone(), true)]; // Low similarity remains successful.
    for change in [
        |r: &mut TemplateMatch| r.raw_score = f64::NAN,
        |r: &mut TemplateMatch| r.raw_score = f64::INFINITY,
        |r: &mut TemplateMatch| r.confidence = Some(f64::NAN),
        |r: &mut TemplateMatch| r.confidence = Some(1.5),
        |r: &mut TemplateMatch| r.confidence = Some(0.5),
        |r: &mut TemplateMatch| r.confidence = None,
        |r: &mut TemplateMatch| r.source = None,
        |r: &mut TemplateMatch| r.search.x = 0,
        |r: &mut TemplateMatch| r.bounds.x = u32::MAX,
        |r: &mut TemplateMatch| r.bounds.width = 2,
        |r: &mut TemplateMatch| r.mode = vision::TemplateMode::CCorr,
    ] {
        let mut invalid = valid.clone();
        change(&mut invalid);
        cases.push((invalid, false));
    }
    for (result, success) in cases {
        let mut matcher = FixedMatcher { result, calls: 0 };
        let mut host = PrimitiveHost::default();
        host.matchers.insert("matcher".into(), &mut matcher);
        let result = invoke(
            &mut host,
            PrimitiveCall::MatchTemplate {
                matcher: "matcher",
                image: &image,
                template: &template,
                config,
            },
            &CancellationToken::new(),
        )
        .await;
        if success {
            let PrimitiveOutput::Match(result) = result.unwrap() else {
                panic!()
            };
            assert_eq!(result.bounds, valid.bounds);
            assert_eq!(result.confidence, Some(0.0));
        } else {
            let error = result.unwrap_err();
            assert_eq!(error.kind(), ErrorKind::RecognitionError);
            assert!(matches!(
                error,
                PrimitiveError::Vision(VisionError::InvalidOutput(_))
            ));
        }
        assert_eq!(matcher.calls, 1);
    }
}

#[tokio::test]
async fn ocr_results_preserve_unicode_optional_confidence_and_original_coordinates() {
    let image = cropped_image();
    let local = Rect {
        x: 1,
        y: 1,
        width: 2,
        height: 2,
    };
    let valid = OcrResult {
        source: image.source().cloned(),
        region: Rect {
            x: 2,
            y: 2,
            width: 2,
            height: 2,
        },
        kind: OcrBackendKind::Tesseract,
        text: "你e\u{301}".into(),
        boxes: vec![vision::OcrBox {
            text: "你e\u{301}".into(),
            confidence: None,
            polygon: [
                vision::Point { x: 2.0, y: 2.0 },
                vision::Point { x: 4.0, y: 2.0 },
                vision::Point { x: 4.0, y: 4.0 },
                vision::Point { x: 2.0, y: 4.0 },
            ],
        }],
        profile: "mock/v1".into(),
    };
    let mut cases = vec![(valid.clone(), true)];
    for change in [
        |r: &mut OcrResult| r.source = None,
        |r: &mut OcrResult| r.region.x = 0,
        |r: &mut OcrResult| r.kind = OcrBackendKind::PpOcr,
        |r: &mut OcrResult| r.boxes[0].confidence = Some(f32::NAN),
        |r: &mut OcrResult| r.boxes[0].confidence = Some(1.1),
        |r: &mut OcrResult| r.boxes[0].polygon[0].x = f32::INFINITY,
        |r: &mut OcrResult| r.boxes[0].polygon[0].x = 1.0,
        |r: &mut OcrResult| r.boxes[0].polygon[0].y = 5.0,
    ] {
        let mut invalid = valid.clone();
        change(&mut invalid);
        cases.push((invalid, false));
    }
    for (result, success) in cases {
        let mut ocr = FixedOcr { result, calls: 0 };
        let mut host = PrimitiveHost::default();
        host.ocr.insert("ocr".into(), &mut ocr);
        let result = invoke(
            &mut host,
            PrimitiveCall::Ocr {
                kind: OcrBackendKind::Tesseract,
                engine: "ocr",
                image: ImageInput::Image(&image),
                region: Some(local),
            },
            &CancellationToken::new(),
        )
        .await;
        if success {
            let PrimitiveOutput::Ocr(result) = result.unwrap() else {
                panic!()
            };
            assert_eq!(result.text, "你e\u{301}");
            assert_eq!(result.boxes[0].confidence, None);
        } else {
            assert!(matches!(
                result,
                Err(PrimitiveError::Vision(VisionError::InvalidOutput(_)))
            ));
        }
        assert_eq!(ocr.calls, 1);
    }
}
