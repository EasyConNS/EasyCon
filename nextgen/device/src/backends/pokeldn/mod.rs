//! LDN Pokemon (Decryptu/pokeldn firmware/pad) serial gamepad backend.

use std::time::{Duration, Instant};

use crate::{
    Delivery, DeviceError, GamepadBackend, LinePolicy, Report, Result, protocol::ProtocolIo,
};

const MAX_BODY: usize = 600;

#[derive(Clone, Copy, Debug, Default)]
pub struct Pokeldn;

impl GamepadBackend for Pokeldn {
    fn name(&self) -> &'static str {
        "pokeldn"
    }
    fn baud_rates(&self) -> &'static [u32] {
        &[921_600]
    }
    fn handshake_timeout(&self) -> Duration {
        Duration::from_secs(3)
    }
    fn line_policy(&self) -> LinePolicy {
        LinePolicy { dtr_on_open: false }
    }
    fn probe(&self, io: &mut ProtocolIo<'_>) -> Result<()> {
        let query = frame(0x02, &[])?;
        let mut next_query = Instant::now();
        let mut decoder = FrameDecoder::default();
        let mut buffer = [0; 128];
        loop {
            if Instant::now() >= next_query {
                io.write(&query)?;
                next_query = Instant::now() + Duration::from_millis(300);
            }
            let count = io.read(&mut buffer)?;
            for byte in &buffer[..count] {
                let mut message = decoder.push(*byte);
                while let Some(frame) = message {
                    if frame.kind == 0x82 {
                        if frame.payload.len() < 5 {
                            return Err(DeviceError::MalformedResponse(
                                "Pokeldn status requires at least 5 bytes".into(),
                            ));
                        }
                        return Ok(());
                    }
                    message = decoder.next_frame();
                }
            }
        }
    }
    fn encode_report(&self, report: &Report) -> Result<Vec<u8>> {
        encode(report)
    }
    fn complete_report(&self, io: &mut ProtocolIo<'_>) -> Result<Delivery> {
        let mut decoder = FrameDecoder::default();
        let mut buffer = [0; 128];
        loop {
            let count = io.read(&mut buffer)?;
            for byte in &buffer[..count] {
                let mut message = decoder.push(*byte);
                while let Some(frame) = message {
                    if frame.kind == 0x81 {
                        if frame.payload.len() != 1 {
                            return Err(DeviceError::MalformedResponse(
                                "invalid Pokeldn result payload length".into(),
                            ));
                        }
                        let code = frame.payload[0] as i8;
                        return if code == 0 {
                            Ok(Delivery::Accepted)
                        } else {
                            Err(DeviceError::ReportRejected { code })
                        };
                    }
                    message = decoder.next_frame();
                }
            }
        }
    }
}

pub fn encode(report: &Report) -> Result<Vec<u8>> {
    report.validate()?;
    let [lo, hi] = report.buttons.to_le_bytes();
    frame(
        0x01,
        &[
            lo,
            hi,
            report.hat as u8,
            report.left.x,
            report.left.y,
            report.right.x,
            report.right.y,
            0,
        ],
    )
}

/// A5 5A, body length (type + payload) as u16 LE, type, payload, wrapping byte sum.
pub fn frame(kind: u8, payload: &[u8]) -> Result<Vec<u8>> {
    let size = payload
        .len()
        .checked_add(1)
        .filter(|size| *size <= MAX_BODY)
        .ok_or_else(|| {
            DeviceError::InvalidArgument("Pokeldn body must contain 1..=600 bytes".into())
        })?;
    let mut bytes = Vec::with_capacity(size + 5);
    bytes.extend_from_slice(&[0xa5, 0x5a]);
    bytes.extend_from_slice(&(size as u16).to_le_bytes());
    bytes.push(kind);
    bytes.extend_from_slice(payload);
    bytes.push(
        payload
            .iter()
            .fold(kind, |sum, byte| sum.wrapping_add(*byte)),
    );
    Ok(bytes)
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Frame {
    pub kind: u8,
    pub payload: Vec<u8>,
}

/// Bounded incremental parser; invalid length/checksum resynchronizes on the next magic prefix.
#[derive(Default)]
pub struct FrameDecoder {
    bytes: Vec<u8>,
}

impl FrameDecoder {
    pub fn push(&mut self, byte: u8) -> Option<Frame> {
        self.bytes.push(byte);
        self.next_frame()
    }

    pub fn next_frame(&mut self) -> Option<Frame> {
        loop {
            let Some(start) = self.bytes.windows(2).position(|pair| pair == [0xa5, 0x5a]) else {
                if self.bytes.last() == Some(&0xa5) {
                    self.bytes.drain(..self.bytes.len() - 1);
                } else {
                    self.bytes.clear();
                }
                return None;
            };
            self.bytes.drain(..start);
            if self.bytes.len() < 4 {
                return None;
            }
            let size = usize::from(u16::from_le_bytes([self.bytes[2], self.bytes[3]]));
            if !(1..=MAX_BODY).contains(&size) {
                self.bytes.remove(0);
                continue;
            }
            if self.bytes.len() < size + 5 {
                return None;
            }
            let checksum = self.bytes[4..4 + size]
                .iter()
                .fold(0u8, |sum, byte| sum.wrapping_add(*byte));
            if checksum != self.bytes[4 + size] {
                self.bytes.remove(0);
                continue;
            }
            let result = Frame {
                kind: self.bytes[4],
                payload: self.bytes[5..4 + size].to_vec(),
            };
            self.bytes.drain(..size + 5);
            return Some(result);
        }
    }
}

#[cfg(test)]
mod tests;
