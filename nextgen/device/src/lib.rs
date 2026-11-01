//! Atomic gamepad interface and serial session lifecycle, independent of firmware framing.

pub mod backends;
mod discovery;
mod error;
pub mod protocol;
mod report;
mod session;
mod transport;

pub use error::{AttemptFailure, DeviceError, Result};
pub use protocol::GamepadBackend;
pub use report::{Button, Hat, Report, Stick, StickId};
pub use session::{ConnectOptions, Delivery, DeviceSession, Gamepad, SessionInfo, WriteReceipt};
pub use transport::{LinePolicy, NativeTransport, PortInfo, Transport, ports};

pub use backends::{EasyCon, Pokeldn};
pub use discovery::connect_serial;
