//! Meta capabilities. User logs are separate from automatic execution traces.

use std::{collections::BTreeMap, sync::Arc, time::Duration};

use thiserror::Error;
use tokio::time::Instant;
use tokio_util::sync::CancellationToken;

#[cfg(test)]
mod tests;

pub type Result<T> = std::result::Result<T, MetaError>;

#[derive(Debug, Error, PartialEq, Eq)]
pub enum MetaError {
    #[error("operation cancelled")]
    Cancelled,
    #[error("invalid argument: {0}")]
    InvalidArgument(&'static str),
    #[error("run clock exceeds i64 milliseconds")]
    ClockOverflow,
    #[error("log sink: {0}")]
    Sink(String),
}

fn check_cancelled(token: &CancellationToken) -> Result<()> {
    if token.is_cancelled() {
        Err(MetaError::Cancelled)
    } else {
        Ok(())
    }
}

/// Wait once, without owning a task. Dropping the future releases the timer.
/// A zero duration yields to the scheduler once; negative durations are invalid.
pub async fn sleep(duration_ms: i64, token: &CancellationToken) -> Result<()> {
    check_cancelled(token)?;
    let duration = u64::try_from(duration_ms)
        .map(Duration::from_millis)
        .map_err(|_| MetaError::InvalidArgument("duration_ms must be nonnegative"))?;
    let deadline = Instant::now()
        .checked_add(duration)
        .ok_or(MetaError::InvalidArgument("sleep deadline is out of range"))?;
    tokio::select! {
        biased;
        _ = token.cancelled() => return Err(MetaError::Cancelled),
        _ = async {
            if duration.is_zero() {
                tokio::task::yield_now().await;
            } else {
                tokio::time::sleep_until(deadline).await;
            }
        } => {}
    }
    check_cancelled(token)
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LogLevel {
    Debug,
    Info,
    Warn,
    Error,
}

/// Flat, typed user fields; this is not the future IR's general Value type.
#[derive(Clone, Debug, PartialEq)]
pub enum LogValue {
    Null,
    Bool(bool),
    Int(i64),
    Float(f64),
    Str(String),
}

#[derive(Clone, Debug, PartialEq)]
pub struct LogRecord {
    pub timestamp_ms: i64,
    pub level: LogLevel,
    pub message: String,
    pub fields: BTreeMap<String, LogValue>,
}

/// Called synchronously once. Sinks must return promptly and coordinate concurrent writers.
/// Delivery cannot be forcibly interrupted; errors are surfaced without retry.
pub trait LogSink: Send + Sync {
    fn emit(&self, record: LogRecord) -> std::result::Result<(), String>;
}

/// Construct once per run. Clones share the origin and sink; new instances start at zero.
#[derive(Clone)]
pub struct MetaContext {
    origin: Instant,
    sink: Arc<dyn LogSink>,
}

impl MetaContext {
    pub fn new(sink: Arc<dyn LogSink>) -> Self {
        Self {
            origin: Instant::now(),
            sink,
        }
    }

    /// Monotonic elapsed milliseconds, not epoch time and not a pure expression function.
    pub fn clock_now(&self, token: &CancellationToken) -> Result<i64> {
        check_cancelled(token)?;
        i64::try_from(self.origin.elapsed().as_millis()).map_err(|_| MetaError::ClockOverflow)
    }

    pub fn log(
        &self,
        level: LogLevel,
        message: impl Into<String>,
        fields: BTreeMap<String, LogValue>,
        token: &CancellationToken,
    ) -> Result<()> {
        check_cancelled(token)?;
        if fields
            .values()
            .any(|value| matches!(value, LogValue::Float(n) if !n.is_finite()))
        {
            return Err(MetaError::InvalidArgument("log numbers must be finite"));
        }
        let record = LogRecord {
            timestamp_ms: self.clock_now(token)?,
            level,
            message: message.into(),
            fields,
        };
        check_cancelled(token)?;
        // Delivery is the commit boundary: cancellation afterward must not hide a delivered log.
        self.sink.emit(record).map_err(MetaError::Sink)
    }
}
