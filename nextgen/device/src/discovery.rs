//! Automatic discovery over the device crate's built-in serial backends.

use crate::{
    ConnectOptions, DeviceError, Gamepad, GamepadBackend,
    backends::{EasyCon, Pokeldn},
};
use tokio_util::sync::CancellationToken;

/// Blocking: EasyCon 115200, EasyCon 9600, then Pokeldn 921600.
/// Only handshake/open failures allow fallback; established-session failures must surface.
pub fn connect_serial(port: &str, token: &CancellationToken) -> crate::Result<Box<dyn Gamepad>> {
    detect(
        || EasyCon.connect(port, ConnectOptions::default(), token),
        || Pokeldn.connect(port, ConnectOptions::default(), token),
        token,
    )
}

fn detect<A: Gamepad + 'static, B: Gamepad + 'static>(
    easycon: impl FnOnce() -> crate::Result<A>,
    pokeldn: impl FnOnce() -> crate::Result<B>,
    token: &CancellationToken,
) -> crate::Result<Box<dyn Gamepad>> {
    if token.is_cancelled() {
        return Err(DeviceError::Cancelled);
    }
    let first = match easycon() {
        Ok(pad) => return Ok(Box::new(pad)),
        Err(error @ DeviceError::ConnectionFailed { .. }) => error,
        Err(error) => return Err(error),
    };
    if token.is_cancelled() {
        return Err(DeviceError::Cancelled);
    }
    match pokeldn() {
        Ok(pad) => Ok(Box::new(pad)),
        Err(error @ DeviceError::ConnectionFailed { .. }) => {
            Err(DeviceError::ProtocolDetectionFailed {
                easycon: Box::new(first),
                pokeldn: Box::new(error),
            })
        }
        Err(error) => Err(error),
    }
}

#[cfg(test)]
mod tests;
