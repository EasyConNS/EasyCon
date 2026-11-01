use super::*;
use crate::{DeviceSession, LinePolicy, Transport};
use std::{
    collections::VecDeque,
    io::{self, Read, Write},
    sync::{Arc, Mutex},
    time::Duration,
};

struct Wire {
    rate: u32,
    winner: Option<u32>,
    replies: VecDeque<u8>,
    events: Arc<Mutex<Vec<String>>>,
}
impl Read for Wire {
    fn read(&mut self, bytes: &mut [u8]) -> io::Result<usize> {
        if self.replies.is_empty() {
            return Err(io::ErrorKind::UnexpectedEof.into());
        }
        let count = bytes.len().min(self.replies.len());
        for byte in &mut bytes[..count] {
            *byte = self.replies.pop_front().unwrap();
        }
        Ok(count)
    }
}
impl Write for Wire {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        if self.winner == Some(self.rate) {
            if self.rate == 921_600 {
                let reply = if bytes[4] == 2 {
                    crate::backends::pokeldn::frame(0x82, &[0; 5])
                } else {
                    crate::backends::pokeldn::frame(0x81, &[0])
                }
                .unwrap();
                self.replies.extend(reply);
            } else if bytes == [0xa5, 0xa5, 0x81] {
                self.replies.push_back(0x80);
            }
        }
        Ok(bytes.len())
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
impl Transport for Wire {
    fn set_timeout(&mut self, _: Duration) -> io::Result<()> {
        Ok(())
    }
}
impl Drop for Wire {
    fn drop(&mut self) {
        self.events
            .lock()
            .unwrap()
            .push(format!("close:{}", self.rate));
    }
}

#[test]
fn ordered_probe_reopens_ports_and_stops_at_first_success() {
    for winner in [115_200, 9_600, 921_600] {
        let events = Arc::new(Mutex::new(vec![]));
        let open = |_: &str, rate: u32, _: LinePolicy| {
            events.lock().unwrap().push(format!("open:{rate}"));
            Ok(Wire {
                rate,
                winner: Some(winner),
                replies: VecDeque::new(),
                events: events.clone(),
            })
        };
        let token = CancellationToken::new();
        let mut pad = detect(
            || {
                DeviceSession::connect_with(
                    EasyCon,
                    "port",
                    ConnectOptions::default(),
                    &token,
                    open,
                )
            },
            || {
                DeviceSession::connect_with(
                    Pokeldn,
                    "port",
                    ConnectOptions::default(),
                    &token,
                    open,
                )
            },
            &token,
        )
        .unwrap();
        assert_eq!(pad.info().baud_rate, winner);
        assert_eq!(pad.state(), Some(crate::Report::default()));
        pad.disconnect().unwrap();
        let rates: Vec<_> = [115_200, 9_600, 921_600]
            .into_iter()
            .take_while(|rate| *rate != winner)
            .chain([winner])
            .collect();
        let expected: Vec<_> = rates
            .iter()
            .flat_map(|rate| [format!("open:{rate}"), format!("close:{rate}")])
            .collect();
        assert_eq!(*events.lock().unwrap(), expected);
    }
}

#[test]
fn all_probe_failures_remain_typed_and_include_all_rates() {
    let events = Arc::new(Mutex::new(vec![]));
    let open = |_: &str, rate: u32, _: LinePolicy| {
        Ok(Wire {
            rate,
            winner: None,
            replies: VecDeque::new(),
            events: events.clone(),
        })
    };
    let token = CancellationToken::new();
    let result = detect(
        || DeviceSession::connect_with(EasyCon, "port", ConnectOptions::default(), &token, open),
        || DeviceSession::connect_with(Pokeldn, "port", ConnectOptions::default(), &token, open),
        &token,
    );
    let Err(DeviceError::ProtocolDetectionFailed { easycon, pokeldn }) = result else {
        panic!("expected typed aggregate");
    };
    let DeviceError::ConnectionFailed { attempts } = *easycon else {
        panic!()
    };
    assert_eq!(
        attempts.iter().map(|a| a.baud_rate).collect::<Vec<_>>(),
        [115_200, 9_600]
    );
    let DeviceError::ConnectionFailed { attempts } = *pokeldn else {
        panic!()
    };
    assert_eq!(attempts[0].baud_rate, 921_600);
}

#[test]
fn cancellation_and_established_session_failure_never_fall_through() {
    for error in [
        DeviceError::Cancelled,
        DeviceError::CleanupFailed {
            error: Box::new(DeviceError::Closed),
            previous: None,
        },
        DeviceError::ReportRejected { code: -1 },
    ] {
        let result = detect::<DeviceSession<EasyCon, Wire>, DeviceSession<Pokeldn, Wire>>(
            || Err(error.clone()),
            || panic!("must not probe another protocol"),
            &CancellationToken::new(),
        );
        assert!(matches!(result, Err(actual) if actual == error));
    }
    let token = CancellationToken::new();
    let result = detect::<DeviceSession<EasyCon, Wire>, DeviceSession<Pokeldn, Wire>>(
        || {
            token.cancel();
            Err(DeviceError::ConnectionFailed { attempts: vec![] })
        },
        || panic!("must not probe after cancellation"),
        &token,
    );
    assert!(matches!(result, Err(DeviceError::Cancelled)));
}
