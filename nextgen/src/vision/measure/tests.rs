use super::*;
use crate::{SampleId, SampleTime, SessionId, video::VideoFormat, video::VideoFrame};

fn frame() -> Image {
    Image::from_frame(&VideoFrame {
        id: SampleId {
            source: "test-camera".into(),
            session: SessionId(11),
            sequence: 3,
        },
        time: SampleTime {
            received_ns: 123,
            source_ns: Some(100),
        },
        format: VideoFormat {
            width: 3,
            height: 2,
            frames_per_second: 30,
            encoding: "RGB3".into(),
        },
        rgb: vec![
            0, 100, 200, 10, 110, 210, 20, 120, 220, 30, 130, 230, 40, 140, 240, 50, 150, 250,
        ]
        .into(),
    })
    .unwrap()
}

#[test]
fn statistics_preserve_nested_roi_coordinates_source_and_channel_order() {
    let image = frame();
    let cropped = image
        .crop(Rect {
            x: 1,
            y: 0,
            width: 2,
            height: 2,
        })
        .unwrap();
    let result = region_statistics(
        &cropped,
        Some(Rect {
            x: 0,
            y: 1,
            width: 2,
            height: 1,
        }),
        &CancellationToken::new(),
    )
    .unwrap();
    assert_eq!(result.implementation, "rgb8-statistics/v1");
    assert_eq!(result.source.as_ref(), image.source());
    assert_eq!(
        result.region,
        Rect {
            x: 1,
            y: 1,
            width: 2,
            height: 1,
        }
    );
    assert_eq!(result.pixel_count, 2);
    assert_eq!(result.rgb_min, [40, 140, 240]);
    assert_eq!(result.rgb_max, [50, 150, 250]);
    assert_eq!(result.rgb_mean, [45.0, 145.0, 245.0]);
    assert_eq!(image.pixel(0, 0).unwrap(), [0, 100, 200]);
}

#[test]
fn difference_reports_exact_pixel_count_and_rgb_error_metrics_symmetrically() {
    let left = Image::new(2, 1, vec![0, 0, 0, 255, 100, 50]).unwrap();
    let right = Image::new(2, 1, vec![255, 0, 0, 255, 103, 46]).unwrap();
    let token = CancellationToken::new();
    let result = image_difference(&left, &right, None, None, &token).unwrap();
    assert_eq!(result.implementation, "rgb8-difference/v1");
    assert_eq!(result.pixel_count, 2);
    assert_eq!(result.changed_pixels, 2);
    assert_eq!(result.max_channel_delta, 255);
    assert_eq!(result.mean_absolute_error, 262.0 / 6.0);
    assert_eq!(result.root_mean_square_error, (65050.0f64 / 6.0).sqrt());
    assert_eq!(result.left_source, None);
    assert_eq!(result.right_source, None);
    assert_eq!(
        result,
        image_difference(&right, &left, None, None, &token).unwrap()
    );
}

#[test]
fn difference_keeps_both_sources_and_accepts_equal_rois_of_different_images() {
    let image = frame();
    let cropped = image
        .crop(Rect {
            x: 1,
            y: 1,
            width: 2,
            height: 1,
        })
        .unwrap();
    let asset = Image::new(1, 1, vec![50, 150, 250]).unwrap();
    let result = image_difference(
        &cropped,
        &asset,
        Some(Rect {
            x: 1,
            y: 0,
            width: 1,
            height: 1,
        }),
        None,
        &CancellationToken::new(),
    )
    .unwrap();
    assert_eq!(result.left_source.as_ref(), image.source());
    assert_eq!(result.right_source, None);
    assert_eq!((result.left_region.x, result.left_region.y), (2, 1));
    assert_eq!(result.right_region, asset.bounds());
    assert_eq!(result.pixel_count, 1);
    assert_eq!(result.changed_pixels, 0);
    assert_eq!(result.mean_absolute_error, 0.0);
    assert_eq!(result.root_mean_square_error, 0.0);
    assert_eq!(result.max_channel_delta, 0);
}

#[test]
fn uniform_black_and_white_measure_full_scale_without_integer_wraparound() {
    let black = Image::new(2, 3, vec![0; 18]).unwrap();
    let white = Image::new(2, 3, vec![255; 18]).unwrap();
    let token = CancellationToken::new();
    let stats = region_statistics(&white, None, &token).unwrap();
    assert_eq!(stats.region, white.bounds());
    assert_eq!(stats.pixel_count, 6);
    assert_eq!(stats.rgb_mean, [255.0; 3]);
    let diff = image_difference(&black, &white, None, None, &token).unwrap();
    assert_eq!(diff.changed_pixels, 6);
    assert_eq!(diff.mean_absolute_error, 255.0);
    assert_eq!(diff.root_mean_square_error, 255.0);
    assert_eq!(diff.max_channel_delta, 255);
}

#[test]
fn chunk_tail_and_last_row_are_measured_without_dropping_pixels() {
    let width = 4101;
    let pixel_count = width * 2;
    let base = Image::new(width, 2, [5, 10, 20].repeat(pixel_count as usize)).unwrap();
    let mut changed = base.rgb().to_vec();
    changed[(pixel_count as usize - 1) * 3..].copy_from_slice(&[255, 0, 0]);
    let changed = Image::new(width, 2, changed).unwrap();
    let token = CancellationToken::new();
    let stats = region_statistics(&changed, None, &token).unwrap();
    assert_eq!(stats.pixel_count, u64::from(pixel_count));
    assert_eq!(stats.rgb_min, [5, 0, 0]);
    assert_eq!(stats.rgb_max, [255, 10, 20]);
    assert_eq!(
        stats.rgb_mean[0],
        (f64::from(pixel_count) * 5.0 + 250.0) / f64::from(pixel_count)
    );
    let diff = image_difference(&base, &changed, None, None, &token).unwrap();
    assert_eq!(diff.changed_pixels, 1);
    assert_eq!(diff.max_channel_delta, 250);
    assert_eq!(
        diff.mean_absolute_error,
        280.0 / (f64::from(pixel_count) * 3.0)
    );
}

#[test]
fn invalid_regions_dimensions_and_pre_cancelled_calls_are_explicit_errors() {
    let image = frame();
    let small = Image::new(1, 1, vec![0; 3]).unwrap();
    let token = CancellationToken::new();
    assert!(matches!(
        image_difference(&image, &small, None, None, &token),
        Err(VisionError::InvalidArgument(_))
    ));
    for region in [
        Rect {
            x: 0,
            y: 0,
            width: 0,
            height: 1,
        },
        Rect {
            x: 2,
            y: 0,
            width: 2,
            height: 1,
        },
        Rect {
            x: u32::MAX,
            y: 0,
            width: 2,
            height: 1,
        },
    ] {
        assert!(matches!(
            region_statistics(&image, Some(region), &token),
            Err(VisionError::InvalidArgument(_))
        ));
        assert!(matches!(
            image_difference(&image, &image, Some(region), None, &token),
            Err(VisionError::InvalidArgument(_))
        ));
        assert!(matches!(
            image_difference(&image, &image, None, Some(region), &token),
            Err(VisionError::InvalidArgument(_))
        ));
    }
    token.cancel();
    assert!(matches!(
        region_statistics(&image, None, &token),
        Err(VisionError::Cancelled)
    ));
    assert!(matches!(
        image_difference(&image, &image, None, None, &token),
        Err(VisionError::Cancelled)
    ));
}
