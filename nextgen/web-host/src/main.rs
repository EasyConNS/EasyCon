use std::{net::Ipv4Addr, path::PathBuf};

use clap::Parser;

#[derive(Parser)]
#[command(version, about = "Local EasyCon device console")]
struct Cli {
    #[arg(long, default_value_t = 19392)]
    port: u16,
    /// Initial directory of the workflow file picker.
    #[arg(long, default_value = ".")]
    directory: PathBuf,
    /// Built Vue application directory.
    #[arg(long, default_value = concat!(env!("CARGO_MANIFEST_DIR"), "/../web/dist"))]
    assets: PathBuf,
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let cli = Cli::parse();
    if !cli.assets.join("index.html").is_file() {
        return Err(
            "Vue assets missing: run `cd nextgen/web && npm ci && npm run build`, or pass --assets"
                .into(),
        );
    }
    let directory = cli.directory.canonicalize()?;
    let listener = tokio::net::TcpListener::bind((Ipv4Addr::LOCALHOST, cli.port)).await?;
    let authority = listener.local_addr()?.to_string();
    let hardware = easycon_web::hardware::Hardware::new();
    let app = easycon_web::router(hardware.clone(), directory, cli.assets, authority.clone());
    println!("EasyCon 控制台 http://{authority}");
    let stopping = hardware.stop.clone();
    let result = axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            if let Err(error) = tokio::signal::ctrl_c().await {
                eprintln!("Ctrl+C: {error}");
            }
            stopping.cancel();
        })
        .await;
    hardware.shutdown().await;
    result?;
    Ok(())
}
