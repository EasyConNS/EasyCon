use std::{io::Write, path::PathBuf, process::ExitCode, time::Duration};

use clap::{Parser, Subcommand};
use easycon_sense::{CaptureClock, CaptureError, Result, audio, video};
use tokio_util::sync::CancellationToken;

#[derive(Parser)]
#[command(version, about = "EasyCon raw capture diagnostics (ccap / CPAL)")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand)]
enum Command {
    /// List versioned atomic contracts without loading native backends or opening devices.
    Primitives,
    /// Enumerate video inputs; ccap briefly opens devices to query their information.
    VideoDevices,
    /// Enumerate audio input endpoints without opening output devices.
    AudioDevices,
    /// Probe the actual default stream format; opens the device briefly.
    VideoFormats {
        #[arg(long)]
        device: String,
    },
    AudioFormats {
        #[arg(long)]
        device: String,
    },
    /// Capture fresh RGB8 frames; optionally save the last frame as a PPM image.
    Video {
        #[arg(long)]
        device: String,
        #[arg(long, requires_all = ["height", "fps", "encoding"], value_parser = clap::value_parser!(u32).range(1..))]
        width: Option<u32>,
        #[arg(long, requires_all = ["width", "fps", "encoding"], value_parser = clap::value_parser!(u32).range(1..))]
        height: Option<u32>,
        /// Constrain reported FPS; 0 allows an unknown rate. Does not negotiate another mode.
        #[arg(long, requires_all = ["width", "height", "encoding"])]
        fps: Option<u32>,
        #[arg(long, requires_all = ["width", "height", "fps"])]
        encoding: Option<String>,
        #[arg(long, default_value_t = 10, value_parser = clap::value_parser!(u32).range(1..))]
        frames: u32,
        #[arg(long, default_value_t = 5000, value_parser = clap::value_parser!(u64).range(1..))]
        timeout_ms: u64,
        #[arg(long)]
        output: Option<PathBuf>,
    },
    /// Read contiguous normalized audio windows and report RMS without recording audio.
    Audio {
        #[arg(long)]
        device: String,
        #[arg(long, default_value_t = 4800, value_parser = clap::value_parser!(u32).range(1..))]
        window_frames: u32,
        #[arg(long, default_value_t = 10, value_parser = clap::value_parser!(u32).range(1..))]
        windows: u32,
        #[arg(long, default_value_t = 96000, value_parser = clap::value_parser!(u32).range(1..))]
        history_frames: u32,
        #[arg(long, default_value_t = 5000, value_parser = clap::value_parser!(u64).range(1..))]
        timeout_ms: u64,
        #[arg(long, requires_all = ["channels", "sample_format"])]
        sample_rate: Option<u32>,
        #[arg(long, requires_all = ["sample_rate", "sample_format"])]
        channels: Option<u16>,
        #[arg(long, requires_all = ["sample_rate", "channels"])]
        sample_format: Option<String>,
    },
}

#[tokio::main]
async fn main() -> ExitCode {
    let cli = Cli::parse();
    let token = CancellationToken::new();
    let signal_token = token.clone();
    let signal = tokio::spawn(async move {
        tokio::select! {
            _ = signal_token.cancelled() => Ok(()),
            result = tokio::signal::ctrl_c() => {
                signal_token.cancel();
                result.map_err(|e| diagnostic_error("register Ctrl+C", e))
            }
        }
    });
    let result = execute(cli.command, &token).await;
    token.cancel();
    let signal_result = signal
        .await
        .map_err(|_| CaptureError::WorkerPanicked)
        .and_then(|result| result);
    match result.and(signal_result) {
        Ok(()) => ExitCode::SUCCESS,
        Err(CaptureError::Cancelled) => ExitCode::from(130),
        Err(error) => {
            eprintln!("{error}");
            ExitCode::FAILURE
        }
    }
}

fn diagnostic_error(operation: &'static str, error: impl std::fmt::Display) -> CaptureError {
    CaptureError::Backend {
        backend: "diagnostic",
        operation,
        message: error.to_string(),
    }
}

async fn execute(command: Command, token: &CancellationToken) -> Result<()> {
    let clock = CaptureClock::default();
    match command {
        Command::Primitives => {
            for id in easycon_sense::primitives::PrimitiveId::ALL {
                println!("{:#?}", id.contract());
            }
        }
        Command::VideoDevices => {
            for device in video::devices()? {
                println!("{}\t{}\t{}", device.id, device.name, device.description);
            }
        }
        Command::AudioDevices => {
            for device in audio::devices()? {
                println!("{}\t{}", device.id, device.name);
            }
        }
        Command::VideoFormats { device } => {
            for format in video::formats(&device)? {
                println!("{format:?}");
            }
        }
        Command::AudioFormats { device } => {
            for format in audio::formats(&device)? {
                println!("{format:?}");
            }
        }
        Command::Video {
            device,
            width,
            height,
            fps,
            encoding,
            frames,
            timeout_ms,
            output,
        } => {
            let mut session = match (width, height, fps, encoding) {
                (Some(width), Some(height), Some(fps), Some(encoding)) => {
                    video::VideoSession::open(
                        &device,
                        video::VideoFormat {
                            width,
                            height,
                            frames_per_second: fps,
                            encoding,
                        },
                        clock,
                    )?
                }
                _ => video::VideoSession::open_default(&device, clock)?,
            };
            let result = async {
                let mut reader = session.subscribe();
                let mut after = None;
                let mut last = None;
                for _ in 0..frames {
                    let frame = tokio::time::timeout(
                        Duration::from_millis(timeout_ms),
                        reader.capture(after.as_ref(), token),
                    )
                    .await
                    .map_err(|e| diagnostic_error("wait for video frame", e))??;
                    println!(
                        "frame={} session={} received_ns={} RGB8={}x{} bytes={}",
                        frame.id.sequence,
                        frame.id.session.0,
                        frame.time.received_ns,
                        frame.format.width,
                        frame.format.height,
                        frame.rgb.len()
                    );
                    after = Some(frame.id.clone());
                    last = Some(frame);
                }
                if let (Some(path), Some(frame)) = (output, last) {
                    let mut file = std::fs::OpenOptions::new()
                        .write(true)
                        .create_new(true)
                        .open(path)
                        .map_err(|e| diagnostic_error("create PPM image", e))?;
                    write!(
                        file,
                        "P6\n{} {}\n255\n",
                        frame.format.width, frame.format.height
                    )
                    .and_then(|()| file.write_all(&frame.rgb))
                    .map_err(|e| diagnostic_error("write PPM image", e))?;
                }
                Ok(())
            }
            .await;
            finish(result, session.stop())?;
        }
        Command::Audio {
            device,
            window_frames,
            windows,
            history_frames,
            timeout_ms,
            sample_rate,
            channels,
            sample_format,
        } => {
            if window_frames > history_frames {
                return Err(CaptureError::InvalidArgument(
                    "window-frames must not exceed history-frames".into(),
                ));
            }
            let format = sample_rate.zip(channels).zip(sample_format).map(
                |((sample_rate, channels), sample_format)| audio::AudioFormat {
                    sample_rate,
                    channels,
                    sample_format,
                },
            );
            let mut session = audio::AudioSession::open(
                &device,
                audio::AudioConfig {
                    format,
                    history_frames: history_frames as usize,
                },
                clock,
            )?;
            println!("input={:?}", session.format());
            let result = async {
                let mut reader = session.subscribe()?;
                for _ in 0..windows {
                    let window = tokio::time::timeout(Duration::from_millis(timeout_ms), reader.read_window(window_frames as usize, token))
                        .await.map_err(|e| diagnostic_error("wait for audio window", e))??;
                    println!("start_frame={} session={} frames={} received_ns={} source_ns={:?} rms={:.6}",
                        window.id.sequence, window.id.session.0, window.frames(), window.time.received_ns,
                        window.time.source_ns, window.rms()?);
                }
                Ok(())
            }.await;
            println!("dropped_callback_frames={}", session.dropped_frames());
            finish(result, session.stop())?;
        }
    }
    Ok(())
}

fn finish(result: Result<()>, stopped: Result<()>) -> Result<()> {
    match stopped {
        Err(error @ CaptureError::CleanupFailed { .. }) => Err(error),
        other => result.and(other),
    }
}
