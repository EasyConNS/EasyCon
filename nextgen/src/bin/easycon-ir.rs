//! Offline IR validation and execution; hardware providers are assembled through the Rust API.
use clap::{Parser, Subcommand};
use easycon_sense::{
    ir::{Document, FlowOutcome, RunControl, RunStopReason},
    meta::{LogRecord, LogSink, MetaContext},
    run::RunBindings,
};
use std::{collections::BTreeMap, path::PathBuf, sync::Arc};

#[derive(Parser)]
#[command(about = "Validate and run orchestration IR without opening hardware")]
struct Options {
    #[command(subcommand)]
    command: Command,
}
#[derive(Subcommand)]
enum Command {
    Validate {
        file: PathBuf,
    },
    /// Run a parameterless entry; only Sleep, ClockNow and console Log are assembled.
    Run {
        file: PathBuf,
    },
}
struct Console;
impl LogSink for Console {
    fn emit(&self, record: LogRecord) -> Result<(), String> {
        eprintln!(
            "[{}ms {:?}] {} {:?}",
            record.timestamp_ms, record.level, record.message, record.fields
        );
        Ok(())
    }
}
#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let command = Options::parse().command;
    let file = match &command {
        Command::Validate { file } | Command::Run { file } => file,
    };
    let document = Document::from_json(&std::fs::read_to_string(file)?)?.validate()?;
    if matches!(command, Command::Validate { .. }) {
        println!(
            "valid IR {}: {}",
            document.document().ir_version,
            document.document().entry
        );
        return Ok(());
    }
    let control = RunControl::new();
    let meta = MetaContext::new(Arc::new(Console));
    let bindings = RunBindings {
        meta: Some(&meta),
        ..RunBindings::default()
    };
    // Keep polling execution through cleanup after Ctrl+C; do not discard its future.
    let execution = document.execute_controlled(bindings, BTreeMap::new(), &control);
    tokio::pin!(execution);
    let result = tokio::select! {
        result = &mut execution => result?,
        signal = tokio::signal::ctrl_c() => {
            signal?;
            control.request_stop(RunStopReason::UserStop);
            execution.await?
        }
    };
    for event in &result.trace {
        eprintln!(
            "#{} +{}ms {:?} {:?} {:?}",
            event.event_seq, event.elapsed_ms, event.node_path, event.context, event.kind
        );
    }
    match result.outcome {
        FlowOutcome::Success(value) => {
            println!("success: {value:?}");
            Ok(())
        }
        FlowOutcome::Failure(error) | FlowOutcome::Cancelled(error) => {
            Err(error.to_string().into())
        }
    }
}
