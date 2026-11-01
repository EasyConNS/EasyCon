use std::{
    collections::VecDeque,
    io::{self, Read, Write},
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

use easycon_device::backends::pokeldn::FrameDecoder;
use easycon_device::{ConnectOptions, DeviceSession, GamepadBackend, Result, Transport};
use tokio_util::sync::CancellationToken;

pub enum WriteStep {
    Limit(usize),
    Fail(io::ErrorKind),
    Zero,
    CancelAfter(usize, CancellationToken),
}

pub enum ReadStep {
    Bytes(Vec<u8>),
    Cancel(CancellationToken),
    Fail(io::ErrorKind),
}

#[derive(Default)]
pub struct State {
    pub reads: VecDeque<ReadStep>,
    pub write_steps: VecDeque<WriteStep>,
    pub writes: Vec<(Vec<u8>, Instant)>,
    pub timeout: Option<Duration>,
    pub dropped: usize,
    pub report_result: Option<i8>,
    pub query_reply_after: Option<usize>,
    pub query_count: usize,
    decoder: FrameDecoder,
}

pub type Shared = Arc<Mutex<State>>;

pub struct FakeTransport(pub Shared);

impl Read for FakeTransport {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        let mut state = self.0.lock().unwrap();
        match state.reads.pop_front() {
            Some(ReadStep::Bytes(mut bytes)) => {
                let count = bytes.len().min(buffer.len());
                buffer[..count].copy_from_slice(&bytes[..count]);
                if count < bytes.len() {
                    state
                        .reads
                        .push_front(ReadStep::Bytes(bytes.split_off(count)));
                }
                Ok(count)
            }
            Some(ReadStep::Cancel(token)) => {
                token.cancel();
                Err(io::ErrorKind::Interrupted.into())
            }
            Some(ReadStep::Fail(kind)) => Err(kind.into()),
            None => Err(io::ErrorKind::TimedOut.into()),
        }
    }
}

impl Write for FakeTransport {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        let mut state = self.0.lock().unwrap();
        let count = match state.write_steps.pop_front() {
            Some(WriteStep::Limit(count)) => count.min(bytes.len()),
            Some(WriteStep::Fail(kind)) => return Err(kind.into()),
            Some(WriteStep::Zero) => return Ok(0),
            Some(WriteStep::CancelAfter(count, token)) => {
                token.cancel();
                count.min(bytes.len())
            }
            None => bytes.len(),
        };
        state.writes.push((bytes[..count].to_vec(), Instant::now()));
        for byte in &bytes[..count] {
            if let Some(frame) = state.decoder.push(*byte) {
                if frame.kind == 0x01 {
                    if let Some(code) = state.report_result {
                        state.reads.push_back(ReadStep::Bytes(
                            easycon_device::backends::pokeldn::frame(0x81, &[code as u8]).unwrap(),
                        ));
                    }
                } else if frame.kind == 0x02 {
                    state.query_count += 1;
                    if state
                        .query_reply_after
                        .is_some_and(|after| state.query_count >= after)
                    {
                        state.reads.push_back(ReadStep::Bytes(
                            easycon_device::backends::pokeldn::frame(0x82, &[0, 0, 0, 0, 0])
                                .unwrap(),
                        ));
                    }
                }
            }
        }
        Ok(count)
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

impl Transport for FakeTransport {
    fn set_timeout(&mut self, timeout: Duration) -> io::Result<()> {
        self.0.lock().unwrap().timeout = Some(timeout);
        Ok(())
    }
}

impl Drop for FakeTransport {
    fn drop(&mut self) {
        self.0.lock().unwrap().dropped += 1;
    }
}

pub fn easycon() -> Shared {
    let state = Arc::new(Mutex::new(State::default()));
    state.lock().unwrap().reads.extend([
        ReadStep::Bytes(vec![0xfe, 0xff]),
        ReadStep::Bytes(vec![0, 0x80, 0]),
    ]);
    state
}

pub fn pokeldn() -> Shared {
    Arc::new(Mutex::new(State {
        report_result: Some(0),
        query_reply_after: Some(1),
        ..State::default()
    }))
}

pub fn options() -> ConnectOptions {
    ConnectOptions {
        handshake_timeout: Some(Duration::from_millis(40)),
        write_timeout: Some(Duration::from_millis(20)),
        ..ConnectOptions::default()
    }
}

pub fn connect<B: GamepadBackend>(
    backend: B,
    shared: &Shared,
) -> Result<DeviceSession<B, FakeTransport>> {
    DeviceSession::connect_with(
        backend,
        "fake-port",
        options(),
        &CancellationToken::new(),
        |_, _, _| Ok(FakeTransport(shared.clone())),
    )
}

pub fn wire(shared: &Shared) -> Vec<u8> {
    shared
        .lock()
        .unwrap()
        .writes
        .iter()
        .flat_map(|(bytes, _)| bytes.iter().copied())
        .collect()
}
