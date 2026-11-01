use std::{
    error::Error,
    fs::OpenOptions,
    io::{self, Write},
    path::PathBuf,
    process::ExitCode,
    time::Instant,
};

use ccap::{FrameOrientation, PixelFormat, PropertyName, Provider, VideoFrame, VideoFrameInfo};
use clap::{Parser, Subcommand, ValueEnum};
use serde_json::json;

type Result<T> = std::result::Result<T, Box<dyn Error>>;

#[cfg(test)]
mod regression;

#[derive(Parser)]
#[command(about = "Isolated ccap-rs hardware acceptance; no production backend changes")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand)]
enum Command {
    /// Enumerate devices and supported formats; the library briefly opens each device.
    List,
    /// Capture selected device, validate fresh frames, stop and recreate the provider each cycle.
    Capture {
        #[arg(long)]
        device: String,
        #[arg(long, value_enum, default_value = "rgb24")]
        format: Output,
        #[arg(long, default_value_t = 30, value_parser = clap::value_parser!(u32).range(1..))]
        frames: u32,
        #[arg(long, default_value_t = 3, value_parser = clap::value_parser!(u32).range(1..))]
        cycles: u32,
        #[arg(long, default_value_t = 3000, value_parser = clap::value_parser!(u32).range(1..))]
        timeout_ms: u32,
        #[arg(long, requires = "height", value_parser = clap::value_parser!(u32).range(1..))]
        width: Option<u32>,
        #[arg(long, requires = "width", value_parser = clap::value_parser!(u32).range(1..))]
        height: Option<u32>,
        #[arg(long, value_parser = clap::value_parser!(u32).range(1..))]
        fps: Option<u32>,
        /// Save only the final frame as PPM; fails if the path already exists.
        #[arg(long)]
        output: Option<PathBuf>,
    },
}

#[derive(Clone, Copy, ValueEnum)]
enum Output {
    Bgra32,
    Rgb24,
}

impl Output {
    fn native(self) -> PixelFormat {
        match self {
            Self::Bgra32 => PixelFormat::Bgra32,
            Self::Rgb24 => PixelFormat::Rgb24,
        }
    }
}

fn check(condition: bool, message: &str) -> Result<()> {
    if condition {
        Ok(())
    } else {
        Err(io::Error::other(message).into())
    }
}

fn main() -> ExitCode {
    match run(Cli::parse()) {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!(
                "{}",
                json!({"event": "error", "message": error.to_string()})
            );
            ExitCode::FAILURE
        }
    }
}

fn run(cli: Cli) -> Result<()> {
    println!(
        "{}",
        json!({"event": "runtime", "ccap": Provider::version()?, "os": std::env::consts::OS, "arch": std::env::consts::ARCH})
    );
    match cli.command {
        Command::List => {
            for (index, device) in Provider::get_devices()?.into_iter().enumerate() {
                println!(
                    "{}",
                    json!({"event": "device", "index": index, "name": device.name,
                    "formats": device.supported_pixel_formats.iter().map(|f| f.as_str()).collect::<Vec<_>>(),
                    "resolutions": device.supported_resolutions.iter().map(|r| [r.width, r.height]).collect::<Vec<_>>() })
                );
            }
        }
        Command::Capture {
            device,
            format,
            frames,
            cycles,
            timeout_ms,
            width,
            height,
            fps,
            output,
        } => {
            for cycle in 1..=cycles {
                let opened = Instant::now();
                let mut provider = Provider::new()?;
                provider.open_device(Some(&device), false)?;
                check(provider.is_opened(), "device did not open")?;
                check(
                    provider.device_info()?.name == device,
                    "opened a different device",
                )?;
                if let Some((width, height)) = width.zip(height) {
                    provider.set_resolution(width, height)?;
                }
                if let Some(fps) = fps {
                    provider.set_frame_rate(f64::from(fps))?;
                }
                provider.set_pixel_format(format.native())?;
                // Explicitly exercise the BGRA input path that failed in nokhwa.
                provider.set_property(
                    PropertyName::PixelFormatInternal,
                    PixelFormat::Bgra32.to_c_enum() as f64,
                )?;
                provider.start_capture()?;
                check(provider.is_started(), "capture did not start")?;
                println!(
                    "{}",
                    json!({"event": "connected", "cycle": cycle, "device": device,
                    "input": PixelFormat::from_c_enum(provider.get_property(PropertyName::PixelFormatInternal)? as u32).as_str(),
                    "output": provider.pixel_format()?.as_str(), "reported_resolution": provider.resolution()?, "reported_fps": provider.frame_rate()?,
                    "open_ms": opened.elapsed().as_millis()})
                );

                let result = read_frames(
                    &mut provider,
                    format.native(),
                    width.zip(height),
                    frames,
                    timeout_ms,
                );
                let stopped = Instant::now();
                let stop_result = provider.stop_capture();
                let still_started = provider.is_started();
                drop(provider);
                stop_result?;
                check(!still_started, "capture still running after stop")?;
                println!(
                    "{}",
                    json!({"event": "disconnected", "cycle": cycle, "stop_drop_ms": stopped.elapsed().as_millis()})
                );

                // Retained frames must stay readable after the provider is destroyed.
                let last = result?;
                let info = last.info()?;
                let rgb = packed_rgb(&info)?;
                check(
                    rgb.len() == info.width as usize * info.height as usize * 3,
                    "invalid retained RGB frame",
                )?;
                if cycle == cycles
                    && let Some(path) = &output
                {
                    let mut file = OpenOptions::new().write(true).create_new(true).open(path)?;
                    write!(file, "P6\n{} {}\n255\n", info.width, info.height)?;
                    file.write_all(&rgb)?;
                    println!("{}", json!({"event": "image", "path": path}));
                }
            }
            println!(
                "{}",
                json!({"event": "pass", "device": device, "cycles": cycles, "frames": u64::from(frames) * u64::from(cycles)})
            );
        }
    }
    Ok(())
}

fn read_frames(
    provider: &mut Provider,
    format: PixelFormat,
    resolution: Option<(u32, u32)>,
    count: u32,
    timeout_ms: u32,
) -> Result<VideoFrame> {
    let started = Instant::now();
    let mut previous = None;
    let mut last = None;
    for number in 1..=count {
        let frame = provider
            .grab_frame(timeout_ms)?
            .ok_or_else(|| io::Error::new(io::ErrorKind::TimedOut, "no frame before deadline"))?;
        let info = frame.info()?;
        check(info.width > 0 && info.height > 0, "empty frame")?;
        if let Some(expected) = resolution
            && expected != (info.width, info.height)
        {
            return Err(io::Error::other(format!(
                "requested resolution {}x{}, received {}x{}",
                expected.0, expected.1, info.width, info.height
            ))
            .into());
        }
        if info.pixel_format != format {
            eprintln!(
                "{}",
                json!({"event": "format_mismatch", "number": number, "index": info.frame_index,
                "timestamp_ns": info.timestamp, "expected": format.as_str(), "actual": info.pixel_format.as_str(),
                "resolution": [info.width, info.height], "stride": info.strides[0]})
            );
        }
        check(
            info.pixel_format == format,
            "backend changed requested output format",
        )?;
        let rgb = packed_rgb(&info)?;
        if let Some((index, timestamp)) = previous {
            check(info.frame_index > index, "frame sequence did not advance")?;
            check(
                info.timestamp > timestamp,
                "frame timestamp did not advance",
            )?;
        }
        previous = Some((info.frame_index, info.timestamp));
        if number == 1 || number == count {
            println!(
                "{}",
                json!({"event": "frame", "number": number, "index": info.frame_index, "timestamp_ns": info.timestamp,
                "resolution": [info.width, info.height], "format": info.pixel_format.as_str(), "stride": info.strides[0],
                "bytes": info.size_in_bytes, "rgb_bytes": rgb.len(), "orientation": format!("{:?}", info.orientation),
                "min": rgb.iter().min(), "max": rgb.iter().max(), "elapsed_ms": started.elapsed().as_millis()})
            );
        }
        last = Some(frame);
    }
    last.ok_or_else(|| io::Error::other("no frames captured").into())
}

fn packed_rgb(info: &VideoFrameInfo<'_>) -> Result<Vec<u8>> {
    let channels = match info.pixel_format {
        PixelFormat::Rgb24 => 3,
        PixelFormat::Bgra32 => 4,
        _ => return Err(io::Error::other("probe supports only RGB24 and BGRA32").into()),
    };
    let row_bytes = info.width as usize * channels;
    let stride = info.strides[0] as usize;
    let data = info.data_planes[0].ok_or_else(|| io::Error::other("missing packed image plane"))?;
    check(stride >= row_bytes && stride > 0, "invalid row stride")?;
    check(
        data.len() / stride >= info.height as usize,
        "truncated image plane",
    )?;
    let mut rgb = Vec::with_capacity(info.width as usize * info.height as usize * 3);
    for y in 0..info.height as usize {
        let source_y = match info.orientation {
            FrameOrientation::TopToBottom => y,
            FrameOrientation::BottomToTop => info.height as usize - 1 - y,
        };
        let row = &data[source_y * stride..source_y * stride + row_bytes];
        if channels == 3 {
            rgb.extend_from_slice(row);
        } else {
            for pixel in row.as_chunks::<4>().0 {
                rgb.extend_from_slice(&[pixel[2], pixel[1], pixel[0]]);
            }
        }
    }
    Ok(rgb)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bgra_conversion_handles_stride_and_bottom_up_orientation() {
        let data = [3, 2, 1, 255, 90, 90, 90, 90, 6, 5, 4, 255, 90, 90, 90, 90];
        let info = VideoFrameInfo {
            width: 1,
            height: 2,
            pixel_format: PixelFormat::Bgra32,
            size_in_bytes: 16,
            timestamp: 1,
            frame_index: 1,
            orientation: FrameOrientation::BottomToTop,
            data_planes: [Some(&data), None, None],
            strides: [8, 0, 0],
        };
        assert_eq!(packed_rgb(&info).unwrap(), [4, 5, 6, 1, 2, 3]);
    }

    #[test]
    fn rejects_truncated_rows() {
        let data = [1, 2, 3];
        let info = VideoFrameInfo {
            width: 2,
            height: 1,
            pixel_format: PixelFormat::Rgb24,
            size_in_bytes: 3,
            timestamp: 1,
            frame_index: 1,
            orientation: FrameOrientation::TopToBottom,
            data_planes: [Some(&data), None, None],
            strides: [6, 0, 0],
        };
        assert!(packed_rgb(&info).is_err());
    }
}
