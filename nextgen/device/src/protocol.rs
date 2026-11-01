//! Backend contract and bounded handshake I/O. Control actions use `Gamepad` instead.

use std::{
    io, thread,
    time::{Duration, Instant},
};

use tokio_util::sync::CancellationToken;

use crate::{
    ConnectOptions, Delivery, DeviceError, DeviceSession, LinePolicy, Report, Result, Transport,
};

pub(crate) const IO_POLL: Duration = Duration::from_millis(10);

pub trait GamepadBackend: Sized + Send {
    fn name(&self) -> &'static str;
    fn baud_rates(&self) -> &'static [u32];
    fn handshake_timeout(&self) -> Duration;
    fn line_policy(&self) -> LinePolicy;
    fn probe(&self, io: &mut ProtocolIo<'_>) -> Result<()>;
    fn encode_report(&self, report: &Report) -> Result<Vec<u8>>;
    fn complete_report(&self, _io: &mut ProtocolIo<'_>) -> Result<Delivery> {
        Ok(Delivery::Written)
    }

    fn connect(
        self,
        name: &str,
        options: ConnectOptions,
        token: &CancellationToken,
    ) -> Result<DeviceSession<Self>> {
        DeviceSession::connect(self, name, options, token)
    }
}

pub struct ProtocolIo<'a> {
    pub(crate) transport: &'a mut dyn Transport,
    pub(crate) deadline: Instant,
    pub(crate) token: &'a CancellationToken,
    pub(crate) timeout_error: DeviceError,
}

impl ProtocolIo<'_> {
    pub fn write(&mut self, packet: &[u8]) -> Result<()> {
        write_packet(self.transport, packet, self.deadline, self.token)
    }

    /// Zero means an I/O poll timed out; EOF is an error. The overall deadline remains enforced.
    pub fn read(&mut self, buffer: &mut [u8]) -> Result<usize> {
        loop {
            cancelled(self.token)?;
            if Instant::now() >= self.deadline {
                return Err(self.timeout_error.clone());
            }
            match self.transport.read(buffer) {
                Ok(0) => {
                    return Err(DeviceError::io(
                        "handshake read",
                        io::ErrorKind::UnexpectedEof.into(),
                    ));
                }
                Ok(count) => {
                    cancelled(self.token)?;
                    if Instant::now() >= self.deadline {
                        return Err(self.timeout_error.clone());
                    }
                    return Ok(count);
                }
                Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
                Err(error)
                    if matches!(
                        error.kind(),
                        io::ErrorKind::TimedOut | io::ErrorKind::WouldBlock
                    ) =>
                {
                    thread::sleep(Duration::from_millis(1));
                    return Ok(0);
                }
                Err(error) => return Err(DeviceError::io("handshake read", error)),
            }
        }
    }
}

pub(crate) fn cancelled(token: &CancellationToken) -> Result<()> {
    if token.is_cancelled() {
        Err(DeviceError::Cancelled)
    } else {
        Ok(())
    }
}

pub(crate) fn write_packet(
    transport: &mut dyn Transport,
    packet: &[u8],
    deadline: Instant,
    token: &CancellationToken,
) -> Result<()> {
    let mut written = 0;
    while written < packet.len() {
        if written == 0 {
            cancelled(token)?;
        }
        if Instant::now() >= deadline {
            return Err(DeviceError::WriteTimeout { written });
        }
        match transport.write(&packet[written..]) {
            Ok(0) => return Err(DeviceError::io("write", io::ErrorKind::WriteZero.into())),
            Ok(count) => written += count,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(error)
                if matches!(
                    error.kind(),
                    io::ErrorKind::TimedOut | io::ErrorKind::WouldBlock
                ) =>
            {
                thread::sleep(Duration::from_millis(1));
            }
            Err(error) => return Err(DeviceError::io("write", error)),
        }
    }
    Ok(())
}
