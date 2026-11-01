use std::process::ExitCode;

use clap::{Parser, Subcommand, ValueEnum};
use easycon_device::EasyCon;
use easycon_device::backends::pokeldn::Pokeldn;
use easycon_device::{
    ConnectOptions, DeviceError, Gamepad, GamepadBackend, Hat, Report, Result, Stick, ports,
};
use tokio_util::sync::CancellationToken;

#[derive(Parser)]
#[command(version, about = "Atomic gamepad diagnostics (EasyCon / Pokeldn)")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Clone, Copy, ValueEnum)]
enum Backend {
    Easycon,
    Pokeldn,
}

#[derive(Subcommand)]
enum Command {
    /// Enumerate all serial ports without opening or probing them.
    Ports,
    /// Detect protocol from the port, write neutral, then disconnect.
    Probe {
        #[arg(long)]
        port: String,
        #[arg(long, value_enum)]
        backend: Option<Backend>,
        #[arg(long, requires = "backend")]
        baud: Option<u32>,
    },
    /// Encode a controller state offline, without opening a port or sending any action.
    Encode {
        #[arg(long, value_enum)]
        backend: Backend,
        #[arg(long, default_value = "0", value_parser = mask)]
        buttons: u16,
        #[arg(long, default_value_t = 8, value_parser = clap::value_parser!(u8).range(0..=8))]
        hat: u8,
        #[arg(long, default_value_t = 128)]
        lx: u8,
        #[arg(long, default_value_t = 128)]
        ly: u8,
        #[arg(long, default_value_t = 128)]
        rx: u8,
        #[arg(long, default_value_t = 128)]
        ry: u8,
    },
}

fn mask(value: &str) -> std::result::Result<u16, String> {
    let result = if let Some(hex) = value.strip_prefix("0x") {
        u16::from_str_radix(hex, 16)
    } else {
        value.parse()
    };
    result.map_err(|error| error.to_string())
}

#[tokio::main]
async fn main() -> ExitCode {
    let command = Cli::parse().command;
    let token = CancellationToken::new();
    let worker_token = token.clone();
    let mut worker = tokio::task::spawn_blocking(move || execute(command, &worker_token));
    let result = tokio::select! {
        result = &mut worker => result.unwrap_or(Err(DeviceError::WorkerPanicked)),
        signal = tokio::signal::ctrl_c() => {
            token.cancel();
            let result = worker.await.unwrap_or(Err(DeviceError::WorkerPanicked));
            signal.map_err(|error| DeviceError::Io { operation: "Ctrl+C", kind: error.kind(), message: error.to_string() })
                .and(result)
        }
    };
    match result {
        Ok(()) => ExitCode::SUCCESS,
        Err(DeviceError::Cancelled) => ExitCode::from(130),
        Err(error) => {
            eprintln!("{error}");
            ExitCode::FAILURE
        }
    }
}

fn execute(command: Command, token: &CancellationToken) -> Result<()> {
    match command {
        Command::Ports => {
            for port in ports()? {
                println!(
                    "{}\tVID={:?}\tPID={:?}\tserial={:?}",
                    port.name, port.usb_vendor_id, port.usb_product_id, port.serial_number
                );
            }
        }
        Command::Probe {
            port,
            backend,
            baud,
        } => {
            let options = ConnectOptions {
                baud_rate: baud,
                ..ConnectOptions::default()
            };
            let mut pad: Box<dyn Gamepad> = match backend {
                Some(Backend::Easycon) => Box::new(EasyCon.connect(&port, options, token)?),
                Some(Backend::Pokeldn) => Box::new(Pokeldn.connect(&port, options, token)?),
                None => easycon_device::connect_serial(&port, token)?,
            };
            println!("connected: {:?}", pad.info());
            pad.disconnect()?;
            println!("neutral written; port released (MCU execution unconfirmed)");
        }
        Command::Encode {
            backend,
            buttons,
            hat,
            lx,
            ly,
            rx,
            ry,
        } => {
            let report = Report {
                buttons,
                hat: Hat::try_from(hat)?,
                left: Stick { x: lx, y: ly },
                right: Stick { x: rx, y: ry },
            };
            let packet = match backend {
                Backend::Easycon => EasyCon.encode_report(&report)?,
                Backend::Pokeldn => Pokeldn.encode_report(&report)?,
            };
            println!("wire={packet:02X?}");
        }
    }
    Ok(())
}
