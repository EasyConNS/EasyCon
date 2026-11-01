use std::{
    sync::atomic::{AtomicU64, Ordering},
    thread,
    time::{Duration, Instant},
};

use tokio_util::sync::CancellationToken;

use crate::{
    AttemptFailure, Button, DeviceError, GamepadBackend, Hat, LinePolicy, NativeTransport, Report,
    Result, Stick, StickId, Transport,
    protocol::{IO_POLL, ProtocolIo, cancelled, write_packet},
};

const REPORT_INTERVAL: Duration = Duration::from_millis(30);
const CANCEL_POLL: Duration = Duration::from_millis(5);
static NEXT_SESSION: AtomicU64 = AtomicU64::new(1);

#[derive(Clone, Debug, Default)]
pub struct ConnectOptions {
    /// None probes the selected backend's supported baud rates in order.
    pub baud_rate: Option<u32>,
    /// None uses the backend's handshake budget.
    pub handshake_timeout: Option<Duration>,
    /// None uses 200 ms for a report write and any firmware acknowledgement.
    pub write_timeout: Option<Duration>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SessionInfo {
    pub port: String,
    pub backend: &'static str,
    pub baud_rate: u32,
    /// Process-local identity; reconnecting creates a new session.
    pub session: u64,
}

#[derive(Clone, Debug)]
pub struct WriteReceipt {
    pub session: u64,
    pub sequence: u64,
    pub delivery: Delivery,
    /// Completion of OS serial writes; not a game acknowledgement.
    pub written_at: Instant,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Delivery {
    Written,
    /// Firmware returned a successful report result; the game may not have processed it yet.
    Accepted,
}

/// Atomic gamepad capabilities. Incremental actions preserve all untouched report fields.
pub trait Gamepad: Send {
    fn info(&self) -> &SessionInfo;
    fn is_connected(&self) -> bool;
    /// Last fully written state, or None when the current device state is uncertain.
    fn state(&self) -> Option<Report>;
    fn apply_report(&mut self, report: Report, token: &CancellationToken) -> Result<WriteReceipt>;
    fn press(&mut self, button: Button, token: &CancellationToken) -> Result<WriteReceipt> {
        self.press_buttons(&[button], token)
    }
    fn release(&mut self, button: Button, token: &CancellationToken) -> Result<WriteReceipt> {
        self.release_buttons(&[button], token)
    }
    /// Set all selected button bits in one report; duplicates and empty selections are allowed.
    fn press_buttons(
        &mut self,
        buttons: &[Button],
        token: &CancellationToken,
    ) -> Result<WriteReceipt> {
        let mut report = self.state().unwrap_or_default();
        for button in buttons {
            report.buttons |= button.mask();
        }
        self.apply_report(report, token)
    }
    /// Clear all selected button bits in one report, preserving unselected buttons.
    fn release_buttons(
        &mut self,
        buttons: &[Button],
        token: &CancellationToken,
    ) -> Result<WriteReceipt> {
        let mut report = self.state().unwrap_or_default();
        for button in buttons {
            report.buttons &= !button.mask();
        }
        self.apply_report(report, token)
    }
    /// Replace the complete directional HAT, including diagonals or Center to release it.
    fn set_hat(&mut self, hat: Hat, token: &CancellationToken) -> Result<WriteReceipt> {
        let report = Report {
            hat,
            ..self.state().unwrap_or_default()
        };
        self.apply_report(report, token)
    }
    /// Set one stick's raw coordinates: 0..=255, center 128; x rightward, y downward.
    fn set_stick(
        &mut self,
        stick: StickId,
        position: Stick,
        token: &CancellationToken,
    ) -> Result<WriteReceipt> {
        let mut report = self.state().unwrap_or_default();
        match stick {
            StickId::Left => report.left = position,
            StickId::Right => report.right = position,
        }
        self.apply_report(report, token)
    }
    fn center_stick(&mut self, stick: StickId, token: &CancellationToken) -> Result<WriteReceipt> {
        self.set_stick(stick, Stick::default(), token)
    }
    fn reset(&mut self, token: &CancellationToken) -> Result<WriteReceipt> {
        self.apply_report(Report::default(), token)
    }
    /// Send neutral as cleanup, then release the connection, even if cleanup fails.
    fn disconnect(&mut self) -> Result<()>;
}

/// Blocking, uniquely owned connection. Mutable access serializes actions without a queue.
pub struct DeviceSession<B: GamepadBackend, T: Transport = NativeTransport> {
    backend: B,
    transport: Option<T>,
    info: SessionInfo,
    write_timeout: Duration,
    next_write: Instant,
    sequence: u64,
    state: Option<Report>,
    failure: Option<DeviceError>,
    close_result: Option<Result<()>>,
}

impl<B: GamepadBackend> DeviceSession<B> {
    pub fn connect(
        backend: B,
        name: &str,
        options: ConnectOptions,
        token: &CancellationToken,
    ) -> Result<Self> {
        Self::connect_with(backend, name, options, token, NativeTransport::open)
    }
}

impl<B: GamepadBackend, T: Transport> DeviceSession<B, T> {
    /// Opens a fresh transport for each attempt; failed attempts are dropped before fallback.
    pub fn connect_with(
        backend: B,
        name: &str,
        options: ConnectOptions,
        token: &CancellationToken,
        mut open: impl FnMut(&str, u32, LinePolicy) -> Result<T>,
    ) -> Result<Self> {
        let handshake_timeout = options
            .handshake_timeout
            .unwrap_or_else(|| backend.handshake_timeout());
        let write_timeout = options.write_timeout.unwrap_or(Duration::from_millis(200));
        if name.trim().is_empty()
            || handshake_timeout.is_zero()
            || write_timeout.is_zero()
            || Instant::now().checked_add(handshake_timeout).is_none()
            || Instant::now().checked_add(write_timeout).is_none()
        {
            return Err(DeviceError::InvalidArgument(
                "port and finite positive timeouts are required".into(),
            ));
        }
        let rates = match &options.baud_rate {
            Some(rate) if backend.baud_rates().contains(rate) => std::slice::from_ref(rate),
            Some(_) => {
                return Err(DeviceError::InvalidArgument(
                    "unsupported backend baud rate".into(),
                ));
            }
            None => backend.baud_rates(),
        };
        let mut attempts = Vec::new();
        for rate in rates {
            cancelled(token)?;
            let result = (|| {
                let mut transport = open(name, *rate, backend.line_policy())?;
                transport
                    .set_timeout(IO_POLL)
                    .map_err(|error| DeviceError::io("set timeout", error))?;
                let deadline = Instant::now()
                    .checked_add(handshake_timeout)
                    .ok_or_else(|| {
                        DeviceError::InvalidArgument("handshake timeout is too large".into())
                    })?;
                backend.probe(&mut ProtocolIo {
                    transport: &mut transport,
                    deadline,
                    token,
                    timeout_error: DeviceError::HandshakeTimeout,
                })?;
                cancelled(token)?;
                Ok(transport)
            })();
            match result {
                Ok(transport) => {
                    let mut session = Self {
                        info: SessionInfo {
                            port: name.to_owned(),
                            backend: backend.name(),
                            baud_rate: *rate,
                            session: NEXT_SESSION.fetch_add(1, Ordering::Relaxed),
                        },
                        backend,
                        transport: Some(transport),
                        write_timeout,
                        next_write: Instant::now(),
                        sequence: 0,
                        state: None,
                        failure: None,
                        close_result: None,
                    };
                    // Establish a known neutral state before allowing incremental press/release.
                    if let Err(error) = session.reset(token) {
                        if session.failure.is_none() {
                            session.failure = Some(error.clone());
                        }
                        return match session.disconnect() {
                            Ok(()) => Err(error),
                            Err(cleanup) => Err(cleanup),
                        };
                    }
                    return Ok(session);
                }
                Err(DeviceError::Cancelled) => return Err(DeviceError::Cancelled),
                Err(error) => attempts.push(AttemptFailure {
                    baud_rate: *rate,
                    error,
                }),
            }
            if attempts.len() < rates.len() {
                wait_until(Instant::now() + Duration::from_millis(100), token)?;
            }
        }
        Err(DeviceError::ConnectionFailed { attempts })
    }

    fn transmit(
        &mut self,
        packet: &[u8],
        token: &CancellationToken,
    ) -> Result<(Instant, Delivery)> {
        let transport = self.transport.as_mut().ok_or(DeviceError::Closed)?;
        let deadline = Instant::now()
            .checked_add(self.write_timeout)
            .ok_or_else(|| DeviceError::InvalidArgument("write timeout is too large".into()))?;
        let result = (|| {
            write_packet(transport, packet, deadline, token)?;
            let written_at = Instant::now();
            // Once bytes have been written, resolve the outcome even if the caller cancels.
            let completion = CancellationToken::new();
            let delivery = self.backend.complete_report(&mut ProtocolIo {
                transport,
                deadline,
                token: &completion,
                timeout_error: DeviceError::AcknowledgementTimeout,
            })?;
            Ok((written_at, delivery))
        })();
        self.next_write = Instant::now() + REPORT_INTERVAL;
        result
    }
}

impl<B: GamepadBackend, T: Transport> Gamepad for DeviceSession<B, T> {
    fn info(&self) -> &SessionInfo {
        &self.info
    }
    fn is_connected(&self) -> bool {
        self.transport.is_some() && self.failure.is_none()
    }
    fn state(&self) -> Option<Report> {
        self.state
    }

    /// Cancellation is honored until the first byte. After that, finish the packet or fault the session.
    fn apply_report(&mut self, report: Report, token: &CancellationToken) -> Result<WriteReceipt> {
        if self.transport.is_none() {
            return Err(DeviceError::Closed);
        }
        if let Some(error) = &self.failure {
            return Err(error.clone());
        }
        report.validate()?;
        let packet = self.backend.encode_report(&report)?;
        wait_until(self.next_write, token)?;
        let next_sequence = self
            .sequence
            .checked_add(1)
            .ok_or_else(|| DeviceError::InvalidArgument("report sequence exhausted".into()))?;
        match self.transmit(&packet, token) {
            Ok((written_at, delivery)) => {
                self.sequence = next_sequence;
                self.state = Some(report);
                Ok(WriteReceipt {
                    session: self.info.session,
                    sequence: self.sequence,
                    delivery,
                    written_at,
                })
            }
            Err(DeviceError::Cancelled) => Err(DeviceError::Cancelled),
            Err(error) => {
                self.state = None;
                self.failure = Some(error.clone());
                Err(error)
            }
        }
    }

    fn disconnect(&mut self) -> Result<()> {
        if let Some(result) = &self.close_result {
            return result.clone();
        }
        let token = CancellationToken::new();
        let result = wait_until(self.next_write, &token)
            .and_then(|()| self.transmit(&self.backend.encode_report(&Report::default())?, &token))
            .map(|_| ())
            .map_err(|error| DeviceError::CleanupFailed {
                error: Box::new(error),
                previous: self.failure.clone().map(Box::new),
            });
        drop(self.transport.take());
        self.state = None;
        self.close_result = Some(result.clone());
        result
    }
}

impl<B: GamepadBackend, T: Transport> Drop for DeviceSession<B, T> {
    fn drop(&mut self) {
        let _ = self.disconnect();
    }
}

fn wait_until(deadline: Instant, token: &CancellationToken) -> Result<()> {
    loop {
        cancelled(token)?;
        let Some(remaining) = deadline.checked_duration_since(Instant::now()) else {
            return Ok(());
        };
        thread::sleep(remaining.min(CANCEL_POLL));
    }
}
