use std::{
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering},
    },
    time::Instant,
};

static NEXT_SESSION: AtomicU64 = AtomicU64::new(1);

/// Process-local identity. Opening a source again always creates a new session.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SessionId(pub u64);

impl SessionId {
    pub(crate) fn next() -> Self {
        Self(NEXT_SESSION.fetch_add(1, Ordering::Relaxed))
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SampleId {
    pub source: String,
    pub session: SessionId,
    /// Video frame number, or the first audio sample-frame offset (not sample count).
    pub sequence: u64,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SampleTime {
    /// Receipt time on a shared CaptureClock, never hardware exposure time.
    pub received_ns: u64,
    /// Source clock, relative to this session's first timestamp; not cross-device time.
    pub source_ns: Option<u64>,
}

/// Share one instance between video and audio to compare host receipt timestamps.
#[derive(Clone, Debug)]
pub struct CaptureClock(Arc<Instant>);

impl Default for CaptureClock {
    fn default() -> Self {
        Self(Arc::new(Instant::now()))
    }
}

impl CaptureClock {
    pub fn now_ns(&self) -> u64 {
        self.0.elapsed().as_nanos().min(u64::MAX as u128) as u64
    }
}
