use std::{path::PathBuf, process::ExitCode};

use clap::Parser;
use easycon_sense::{
    ir::{FlowOutcome, RunControl, RunStopReason},
    project::{HostError, PreparedProject},
};

#[derive(Parser)]
#[command(
    version,
    about = "Run a project workflow with bundled OpenCV and serial protocol discovery"
)]
struct Cli {
    /// Workflow file (or project directory); reads config.json and imglabel/*.il beside the workflow.
    #[arg(default_value = "workflow.json")]
    workflow: PathBuf,
    /// Validate IR, configuration and imglabel/*.il assets without loading native libraries or devices.
    #[arg(long)]
    check: bool,
}

#[tokio::main]
async fn main() -> ExitCode {
    let cli = Cli::parse();
    let control = RunControl::new();
    let worker_control = control.clone();
    let mut worker = tokio::task::spawn_blocking(move || {
        let project = PreparedProject::load(cli.workflow)?;
        if cli.check {
            println!("project validated: {}", project.directory.display());
            Ok(None)
        } else {
            Ok(Some(project.run_blocking(&worker_control)))
        }
    });
    let (joined, signal) = tokio::select! {
        result = &mut worker => (result, Ok(())),
        signal = tokio::signal::ctrl_c() => {
            control.request_stop(RunStopReason::UserStop);
            (worker.await, signal)
        }
    };
    if let Err(ref error) = signal {
        eprintln!("Ctrl+C handler: {error}");
    }
    let report = match joined {
        Ok(Ok(None)) => {
            return if signal.is_err() {
                ExitCode::FAILURE
            } else if control.is_cancelled() {
                ExitCode::from(130)
            } else {
                ExitCode::SUCCESS
            };
        }
        Ok(Ok(Some(report))) => report,
        Ok(Err(error)) => {
            let error: HostError = error;
            eprintln!("{error}");
            return ExitCode::FAILURE;
        }
        Err(error) => {
            eprintln!("workflow host failed: {error}");
            return ExitCode::FAILURE;
        }
    };
    for (resource, error) in &report.cleanup {
        eprintln!("host cleanup {resource}: {error}");
    }
    let code = match report.execution {
        Ok(execution) => {
            for event in &execution.trace {
                println!(
                    "{}ms {} {:?}",
                    event.elapsed_ms,
                    event.node_path.as_deref().unwrap_or("run"),
                    event.kind
                );
            }
            match execution.outcome {
                FlowOutcome::Success(value) => {
                    println!("success: {:?}", value.ty());
                    ExitCode::SUCCESS
                }
                FlowOutcome::Cancelled(error) => {
                    eprintln!("{error}");
                    ExitCode::from(130)
                }
                FlowOutcome::Failure(error) => {
                    eprintln!("{error}");
                    ExitCode::FAILURE
                }
            }
        }
        Err(error) => {
            eprintln!("{error}");
            if error.is_cancelled() {
                ExitCode::from(130)
            } else {
                ExitCode::FAILURE
            }
        }
    };
    if !report.cleanup.is_empty() || signal.is_err() {
        ExitCode::FAILURE
    } else {
        code
    }
}
