//! Pure RGB8 measurements of explicit images. No capture, baseline state or business thresholds.

use tokio_util::sync::CancellationToken;

use super::{FrameSource, Image, Rect, Result, VisionError, check_cancelled};

#[cfg(test)]
mod tests;

// Bound the amount of CPU work between cancellation checks, even for a very wide row.
const CHUNK_BYTES: usize = 4096 * 3;

#[derive(Clone, Debug, PartialEq)]
pub struct RegionStatistics {
    pub implementation: &'static str,
    pub source: Option<FrameSource>,
    pub region: Rect,
    pub pixel_count: u64,
    pub rgb_min: [u8; 3],
    pub rgb_max: [u8; 3],
    /// Arithmetic channel means in the stored RGB8 encoding, in 0..=255.
    pub rgb_mean: [f64; 3],
}

#[derive(Clone, Debug, PartialEq)]
pub struct ImageDifference {
    pub implementation: &'static str,
    pub left_source: Option<FrameSource>,
    pub right_source: Option<FrameSource>,
    pub left_region: Rect,
    pub right_region: Rect,
    pub pixel_count: u64,
    /// Number of pixels with at least one unequal channel, without a tolerance threshold.
    pub changed_pixels: u64,
    /// MAE and RMSE across all RGB channel pairs, in 0..=255; neither is a confidence.
    pub mean_absolute_error: f64,
    pub root_mean_square_error: f64,
    pub max_channel_delta: u8,
}

fn local_region(image: &Image, region: Option<Rect>) -> Result<Rect> {
    region
        .unwrap_or(Rect {
            x: 0,
            y: 0,
            width: image.width(),
            height: image.height(),
        })
        .validate(image.width(), image.height())
}

fn absolute_region(image: &Image, region: Rect) -> Rect {
    Rect {
        x: image.bounds().x + region.x,
        y: image.bounds().y + region.y,
        ..region
    }
}

fn row(image: &Image, region: Rect, y: u32) -> &[u8] {
    let start = ((region.y + y) as usize * image.width() as usize + region.x as usize) * 3;
    &image.rgb()[start..start + region.width as usize * 3]
}

/// Measure each channel once over the selected ROI; input coordinates are local to the image.
/// RGB8 values are measured as stored, without gamma conversion or luminance weighting.
pub fn region_statistics(
    image: &Image,
    region: Option<Rect>,
    token: &CancellationToken,
) -> Result<RegionStatistics> {
    check_cancelled(token)?;
    let region = local_region(image, region)?;
    let pixel_count = u64::from(region.width) * u64::from(region.height);
    let mut rgb_min = [u8::MAX; 3];
    let mut rgb_max = [0; 3];
    // Exact integer accumulation avoids overflow at all validated Image dimensions.
    let mut rgb_sum = [0u128; 3];
    for y in 0..region.height {
        for chunk in row(image, region, y).chunks(CHUNK_BYTES) {
            check_cancelled(token)?;
            for pixel in chunk.as_chunks::<3>().0 {
                for channel in 0..3 {
                    rgb_min[channel] = rgb_min[channel].min(pixel[channel]);
                    rgb_max[channel] = rgb_max[channel].max(pixel[channel]);
                    rgb_sum[channel] += u128::from(pixel[channel]);
                }
            }
        }
    }
    check_cancelled(token)?;
    Ok(RegionStatistics {
        implementation: "rgb8-statistics/v1",
        source: image.source().cloned(),
        region: absolute_region(image, region),
        pixel_count,
        rgb_min,
        rgb_max,
        rgb_mean: rgb_sum.map(|sum| sum as f64 / pixel_count as f64),
    })
}

/// Compare corresponding RGB pixels in two equal-sized ROIs; each ROI uses its image's local
/// coordinates. No resizing, alignment, implicit previous frame or source-clock comparison.
pub fn image_difference(
    left: &Image,
    right: &Image,
    left_region: Option<Rect>,
    right_region: Option<Rect>,
    token: &CancellationToken,
) -> Result<ImageDifference> {
    check_cancelled(token)?;
    let left_region = local_region(left, left_region)?;
    let right_region = local_region(right, right_region)?;
    if (left_region.width, left_region.height) != (right_region.width, right_region.height) {
        return Err(VisionError::InvalidArgument(
            "image difference requires equal ROI dimensions".into(),
        ));
    }
    let pixel_count = u64::from(left_region.width) * u64::from(left_region.height);
    let mut changed_pixels = 0;
    let mut absolute_sum = 0u128;
    let mut square_sum = 0u128;
    let mut max_channel_delta = 0;
    for y in 0..left_region.height {
        let left = row(left, left_region, y);
        let right = row(right, right_region, y);
        for (left, right) in left.chunks(CHUNK_BYTES).zip(right.chunks(CHUNK_BYTES)) {
            check_cancelled(token)?;
            for (left, right) in left.as_chunks::<3>().0.iter().zip(right.as_chunks::<3>().0) {
                let mut changed = false;
                for (&left, &right) in left.iter().zip(right) {
                    let delta = left.abs_diff(right);
                    changed |= delta != 0;
                    max_channel_delta = max_channel_delta.max(delta);
                    absolute_sum += u128::from(delta);
                    square_sum += u128::from(delta) * u128::from(delta);
                }
                changed_pixels += u64::from(changed);
            }
        }
    }
    check_cancelled(token)?;
    let channel_count = pixel_count as f64 * 3.0;
    Ok(ImageDifference {
        implementation: "rgb8-difference/v1",
        left_source: left.source().cloned(),
        right_source: right.source().cloned(),
        left_region: absolute_region(left, left_region),
        right_region: absolute_region(right, right_region),
        pixel_count,
        changed_pixels,
        mean_absolute_error: absolute_sum as f64 / channel_count,
        root_mean_square_error: (square_sum as f64 / channel_count).sqrt(),
        max_channel_delta,
    })
}
