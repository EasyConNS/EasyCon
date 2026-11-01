//! Provider boundaries: reject malformed observations without retrying or recapturing.

use crate::vision::{
    Image, MatchConfig, OcrBackendKind, OcrResult, Rect, TemplateMatch, VisionError,
};
use crate::{CaptureError, SampleId, audio::AudioWindow, video::VideoFrame};

pub(super) fn frame(frame: &VideoFrame, after: Option<&SampleId>) -> crate::Result<()> {
    Image::from_frame(frame).map_err(|error| CaptureError::InvalidData(error.to_string()))?;
    if let Some(after) = after {
        if (frame.id.source.as_str(), frame.id.session) != (after.source.as_str(), after.session) {
            return Err(CaptureError::SourceChanged);
        }
        if frame.id.sequence <= after.sequence {
            return Err(CaptureError::InvalidData(
                "capture did not advance beyond the requested cursor".into(),
            ));
        }
    }
    Ok(())
}

pub(super) fn audio(window: &AudioWindow, frames: usize) -> crate::Result<()> {
    if window.sample_rate == 0
        || window.channels == 0
        || frames.checked_mul(usize::from(window.channels)) != Some(window.samples.len())
    {
        return Err(CaptureError::InvalidData(
            "audio window must contain exactly the requested frames and a valid format".into(),
        ));
    }
    Ok(())
}

pub(super) fn region(image: &Image, local: Option<Rect>) -> crate::vision::Result<Rect> {
    let local = local
        .unwrap_or(Rect {
            x: 0,
            y: 0,
            width: image.width(),
            height: image.height(),
        })
        .validate(image.width(), image.height())?;
    Ok(Rect {
        x: image.bounds().x + local.x,
        y: image.bounds().y + local.y,
        ..local
    })
}

pub(super) fn match_input(
    image: &Image,
    template: &Image,
    config: MatchConfig,
) -> crate::vision::Result<Rect> {
    let search = region(image, config.region)?;
    if template.width() > search.width || template.height() > search.height {
        return Err(VisionError::InvalidArgument(
            "template must fit inside the search region".into(),
        ));
    }
    Ok(search)
}

fn contains(outer: Rect, inner: Rect) -> bool {
    inner
        .x
        .checked_sub(outer.x)
        .zip(inner.y.checked_sub(outer.y))
        .is_some_and(|(x, y)| {
            Rect { x, y, ..inner }
                .validate(outer.width, outer.height)
                .is_ok()
        })
}

pub(super) fn template_match(
    result: &TemplateMatch,
    image: &Image,
    template: &Image,
    config: MatchConfig,
    search: Rect,
) -> crate::vision::Result<()> {
    if result.source.as_ref() != image.source()
        || result.search != search
        || result.mode != config.mode
        || result.bounds.width != template.width()
        || result.bounds.height != template.height()
        || !contains(search, result.bounds)
        || result.confidence != config.mode.confidence(result.raw_score)?
    {
        return Err(VisionError::InvalidOutput(
            "template match must preserve source, mode, search coordinates, template size and score mapping".into(),
        ));
    }
    Ok(())
}

pub(super) fn ocr(
    result: &OcrResult,
    image: &Image,
    region: Rect,
    kind: OcrBackendKind,
) -> crate::vision::Result<()> {
    if result.source.as_ref() != image.source() || result.region != region || result.kind != kind {
        return Err(VisionError::InvalidOutput(
            "OCR must preserve source, selected region and backend identity".into(),
        ));
    }
    for span in &result.boxes {
        if span
            .confidence
            .is_some_and(|n| !n.is_finite() || !(0.0..=1.0).contains(&n))
            || span.polygon.iter().any(|point| {
                !point.x.is_finite()
                    || !point.y.is_finite()
                    || point.x < region.x as f32
                    || point.y < region.y as f32
                    || point.x > (region.x + region.width) as f32
                    || point.y > (region.y + region.height) as f32
            })
        {
            return Err(VisionError::InvalidOutput(
                "OCR boxes require finite coordinates inside the selected region and confidence in 0..=1".into(),
            ));
        }
    }
    Ok(())
}
