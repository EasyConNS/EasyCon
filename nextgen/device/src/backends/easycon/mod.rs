//! EasyCon MCU backend, independently reconstructed from the existing wire contract.

use std::time::Duration;

use crate::{GamepadBackend, LinePolicy, Report, Result, protocol::ProtocolIo};

#[derive(Clone, Copy, Debug, Default)]
pub struct EasyCon;

impl GamepadBackend for EasyCon {
    fn name(&self) -> &'static str {
        "easycon"
    }
    fn baud_rates(&self) -> &'static [u32] {
        &[115_200, 9_600]
    }
    fn handshake_timeout(&self) -> Duration {
        Duration::from_secs(1)
    }
    fn line_policy(&self) -> LinePolicy {
        LinePolicy {
            dtr_on_open: !cfg!(target_os = "windows"),
        }
    }
    fn probe(&self, io: &mut ProtocolIo<'_>) -> Result<()> {
        io.write(&[0xa5, 0xa5, 0x81])?;
        let mut buffer = [0; 64];
        loop {
            let count = io.read(&mut buffer)?;
            if buffer[..count].contains(&0x80) {
                return Ok(());
            }
        }
    }
    fn encode_report(&self, report: &Report) -> Result<Vec<u8>> {
        Ok(encode(report)?.to_vec())
    }
}

/// Seven raw bytes: buttons in big endian, HAT, LX, LY, RX, RY.
pub fn payload(report: &Report) -> Result<[u8; 7]> {
    report.validate()?;
    let [hi, lo] = report.buttons.to_be_bytes();
    Ok([
        hi,
        lo,
        report.hat as u8,
        report.left.x,
        report.left.y,
        report.right.x,
        report.right.y,
    ])
}

/// The 56 payload bits become eight 7-bit chunks; only the last byte has bit 7 set.
pub fn encode(report: &Report) -> Result<[u8; 8]> {
    let [a, b, c, d, e, f, g] = payload(report)?;
    let bits = u64::from_be_bytes([0, a, b, c, d, e, f, g]);
    let mut packet = [0; 8];
    for (index, byte) in packet.iter_mut().enumerate() {
        *byte = ((bits >> (7 * (7 - index))) & 0x7f) as u8;
    }
    packet[7] |= 0x80;
    Ok(packet)
}

#[cfg(test)]
mod tests;
