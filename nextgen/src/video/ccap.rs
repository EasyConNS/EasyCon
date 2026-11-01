//! Official ccap safe API only. Native state stays on the VideoSession worker.
use ::ccap::{FrameOrientation, PixelFormat, Provider, VideoFrameInfo};

use super::{VideoBuffer, VideoDevice, VideoFormat, VideoSource};
use crate::{CaptureError, Result};

const GRAB_TIMEOUT_MS: u32 = 1000;

#[derive(Debug, PartialEq, Eq)]
enum DeviceSelector<'a> {
    Index(usize),
    Name(&'a str),
}

fn selector(id: &str) -> Result<DeviceSelector<'_>> {
    if let Some(index) = id.strip_prefix("index:") {
        return index
            .parse::<u32>()
            .map(|i| DeviceSelector::Index(i as usize))
            .map_err(|_| CaptureError::InvalidArgument("invalid video index".into()));
    }
    if let Some(name) = id
        .strip_prefix("id:")
        .filter(|s| !s.trim().is_empty() && !s.contains('\0'))
    {
        return Ok(DeviceSelector::Name(name));
    }
    Err(CaptureError::InvalidArgument(
        "use a video ID returned by device enumeration".into(),
    ))
}

pub(crate) fn validate_device_id(id: &str) -> Result<()> {
    selector(id).map(|_| ())
}

/// ccap's safe enumeration briefly opens devices to query their information.
pub fn devices() -> Result<Vec<VideoDevice>> {
    Provider::get_devices()
        .map_err(|e| CaptureError::backend("ccap", "enumerate", e))
        .map(|devices| {
            devices
                .into_iter()
                .map(|device| VideoDevice {
                    id: format!("id:{}", device.name),
                    name: device.name,
                    description: "ccap".into(),
                })
                .collect()
        })
}

fn device_name(id: &str) -> Result<String> {
    match selector(id)? {
        DeviceSelector::Name(name) => Ok(name.to_owned()),
        DeviceSelector::Index(index) => {
            devices()?
                .get(index)
                .map(|d| d.name.clone())
                .ok_or_else(|| {
                    CaptureError::InvalidArgument(
                        "video index is no longer available; enumerate again".into(),
                    )
                })
        }
    }
}

pub(super) fn verify_format(actual: &VideoFormat, requested: &VideoFormat) -> Result<()> {
    if actual.width != requested.width
        || actual.height != requested.height
        || actual.encoding != requested.encoding
        || (requested.frames_per_second != 0
            && actual.frames_per_second != requested.frames_per_second)
    {
        return Err(CaptureError::UnsupportedFormat(format!(
            "ccap default stream {actual:?} does not satisfy {requested:?}; exact mode negotiation is unavailable"
        )));
    }
    Ok(())
}

pub(super) fn reported_fps(fps: f64) -> u32 {
    // Keep the integer metadata contract; 0 means unknown, including fractional FPS.
    if fps.is_finite() && fps >= 1.0 && fps <= f64::from(u32::MAX) && fps.fract() == 0.0 {
        fps as u32
    } else {
        0
    }
}

pub(super) struct CcapSource {
    provider: Provider,
    format: VideoFormat,
    first: Option<VideoBuffer>,
    source_origin: Option<u64>,
    previous: Option<(u64, u64)>,
    stopped: bool,
}

impl CcapSource {
    pub(super) fn open(id: &str, requested: Option<&VideoFormat>) -> Result<Self> {
        let name = device_name(id)?;
        let mut provider =
            Provider::new().map_err(|e| CaptureError::backend("ccap", "create", e))?;
        provider
            .open_device(Some(&name), false)
            .map_err(|e| CaptureError::backend("ccap", "open", e))?;
        let info = provider
            .device_info()
            .map_err(|e| CaptureError::backend("ccap", "device info", e))?;
        // ccap may fall back to the default camera when an unknown name is supplied.
        if !provider.is_opened() || info.name != name {
            return Err(CaptureError::backend(
                "ccap",
                "open",
                "opened a different device",
            ));
        }
        // Keep ccap's platform default packed output. Changing it after open can
        // leave frames of the old format in the upstream startup queue.
        provider
            .start_capture()
            .map_err(|e| CaptureError::backend("ccap", "start", e))?;
        let frame = grab(&mut provider)?;
        let info = frame
            .info()
            .map_err(|e| CaptureError::backend("ccap", "frame info", e))?;
        let format = VideoFormat {
            width: info.width,
            height: info.height,
            frames_per_second: reported_fps(
                provider
                    .frame_rate()
                    .map_err(|e| CaptureError::backend("ccap", "frame rate", e))?,
            ),
            encoding: info.pixel_format.as_str().into(),
        };
        let mut source = Self {
            provider,
            format,
            first: None,
            source_origin: None,
            previous: None,
            stopped: false,
        };
        source.first = Some(decode_frame(
            &info,
            &source.format,
            &mut source.source_origin,
            &mut source.previous,
        )?);
        if let Some(requested) = requested {
            verify_format(&source.format, requested)?;
        }
        Ok(source)
    }
}

pub(super) fn decode_frame(
    info: &VideoFrameInfo<'_>,
    format: &VideoFormat,
    source_origin: &mut Option<u64>,
    previous: &mut Option<(u64, u64)>,
) -> Result<VideoBuffer> {
    if (info.width, info.height) != (format.width, format.height)
        || info.pixel_format.as_str() != format.encoding
    {
        return Err(CaptureError::InvalidData(
            "video format changed within a session".into(),
        ));
    }
    if previous.is_some_and(|(index, timestamp)| {
        info.frame_index <= index
            || (info.timestamp != 0 && timestamp != 0 && info.timestamp <= timestamp)
    }) {
        return Err(CaptureError::InvalidData(
            "ccap frame sequence or timestamp did not advance".into(),
        ));
    }
    let rgb = packed_rgb(info)?;
    let source_ns = if info.timestamp == 0 {
        None
    } else {
        Some(
            info.timestamp
                .checked_sub(*source_origin.get_or_insert(info.timestamp))
                .ok_or_else(|| {
                    CaptureError::InvalidData("ccap source clock moved backwards".into())
                })?,
        )
    };
    *previous = Some((info.frame_index, info.timestamp));
    Ok(VideoBuffer { rgb, source_ns })
}

fn grab(provider: &mut Provider) -> Result<::ccap::VideoFrame> {
    provider
        .grab_frame(GRAB_TIMEOUT_MS)
        .map_err(|e| CaptureError::backend("ccap", "read", e))?
        .ok_or_else(|| CaptureError::backend("ccap", "read", "no frame within 1000 ms"))
}

impl VideoSource for CcapSource {
    fn format(&self) -> VideoFormat {
        self.format.clone()
    }
    fn next_frame(&mut self) -> Result<VideoBuffer> {
        if let Some(first) = self.first.take() {
            return Ok(first);
        }
        let frame = grab(&mut self.provider)?;
        let info = frame
            .info()
            .map_err(|e| CaptureError::backend("ccap", "frame info", e))?;
        decode_frame(
            &info,
            &self.format,
            &mut self.source_origin,
            &mut self.previous,
        )
    }
    fn stop(&mut self) -> Result<()> {
        if !self.stopped {
            self.provider
                .stop_capture()
                .map_err(|e| CaptureError::backend("ccap", "stop", e))?;
            if self.provider.is_started() {
                return Err(CaptureError::backend(
                    "ccap",
                    "stop",
                    "capture still running",
                ));
            }
            self.stopped = true;
        }
        Ok(())
    }
}

impl Drop for CcapSource {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

pub(super) fn packed_rgb(info: &VideoFrameInfo<'_>) -> Result<Vec<u8>> {
    let channels: usize = match info.pixel_format {
        PixelFormat::Rgb24 | PixelFormat::Bgr24 => 3,
        PixelFormat::Bgra32 | PixelFormat::Rgba32 => 4,
        _ => {
            return Err(CaptureError::UnsupportedFormat(format!(
                "ccap packed output {}",
                info.pixel_format.as_str()
            )));
        }
    };
    let width = info.width as usize;
    let height = info.height as usize;
    let row_bytes = width
        .checked_mul(channels)
        .ok_or_else(|| CaptureError::InvalidData("video row overflow".into()))?;
    let stride = info.strides[0] as usize;
    let expected = width
        .checked_mul(height)
        .and_then(|n| n.checked_mul(3))
        .filter(|n| *n > 0)
        .ok_or_else(|| CaptureError::InvalidData("invalid RGB8 dimensions".into()))?;
    let required = height
        .checked_sub(1)
        .and_then(|h| h.checked_mul(stride))
        .and_then(|n| n.checked_add(row_bytes))
        .ok_or_else(|| CaptureError::InvalidData("video plane overflow".into()))?;
    let data = info.data_planes[0]
        .filter(|data| data.len() >= required && stride >= row_bytes)
        .ok_or_else(|| {
            CaptureError::InvalidData("missing or truncated packed image plane".into())
        })?;
    let mut rgb = vec![0; expected];
    for (y, output) in rgb.chunks_exact_mut(width * 3).enumerate() {
        let source_y = match info.orientation {
            FrameOrientation::TopToBottom => y,
            FrameOrientation::BottomToTop => height - 1 - y,
        };
        let input = &data[source_y * stride..source_y * stride + row_bytes];
        match info.pixel_format {
            PixelFormat::Rgb24 => output.copy_from_slice(input),
            PixelFormat::Bgr24 => {
                for (out, pixel) in output
                    .as_chunks_mut::<3>()
                    .0
                    .iter_mut()
                    .zip(input.as_chunks::<3>().0)
                {
                    *out = [pixel[2], pixel[1], pixel[0]];
                }
            }
            PixelFormat::Bgra32 => {
                for (out, pixel) in output
                    .as_chunks_mut::<3>()
                    .0
                    .iter_mut()
                    .zip(input.as_chunks::<4>().0)
                {
                    *out = [pixel[2], pixel[1], pixel[0]];
                }
            }
            PixelFormat::Rgba32 => {
                for (out, pixel) in output
                    .as_chunks_mut::<3>()
                    .0
                    .iter_mut()
                    .zip(input.as_chunks::<4>().0)
                {
                    out.copy_from_slice(&pixel[..3]);
                }
            }
            _ => unreachable!(),
        }
    }
    Ok(rgb)
}
