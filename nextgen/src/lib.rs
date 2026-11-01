//! Raw video and audio acquisition, independent of the desktop UI and orchestration IR.

pub mod audio;
mod error;
pub mod ir;
pub mod meta;
pub mod native_work;
pub mod primitives;
pub mod project;
pub mod run;
mod sample;
pub mod video;
pub mod vision;

pub use error::{CaptureError, Result};
pub use sample::{CaptureClock, SampleId, SampleTime, SessionId};
