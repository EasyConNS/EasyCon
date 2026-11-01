mod support;

use std::{
    io,
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

use easycon_device::EasyCon;
use easycon_device::backends::pokeldn::Pokeldn;
use easycon_device::{
    Button, ConnectOptions, Delivery, DeviceError, DeviceSession, Gamepad, Hat, Report, Stick,
    StickId,
};
use tokio_util::sync::CancellationToken;

use support::{
    FakeTransport, ReadStep, Shared, State, WriteStep, connect, easycon, options, pokeldn, wire,
};

fn exercise_buttons(mut pad: Box<dyn Gamepad>, delivery: Delivery) {
    let token = CancellationToken::new();
    assert_eq!(pad.state(), Some(Report::default()));
    let initial = Report {
        buttons: Button::B.mask(),
        hat: Hat::DownLeft,
        left: Stick { x: 12, y: 34 },
        right: Stick { x: 56, y: 78 },
    };
    pad.apply_report(initial, &token).unwrap();
    let pressed = pad.press(Button::A, &token).unwrap();
    assert_eq!(pressed.delivery, delivery);
    assert_eq!(pressed.session, pad.info().session);
    assert_eq!(pressed.sequence, 3);
    assert_eq!(
        pad.state(),
        Some(Report {
            buttons: Button::A.mask() | Button::B.mask(),
            ..initial
        })
    );
    pad.release(Button::A, &token).unwrap();
    assert_eq!(pad.state(), Some(initial));
    pad.reset(&token).unwrap();
    assert_eq!(pad.state(), Some(Report::default()));
    pad.disconnect().unwrap();
    assert!(!pad.is_connected());
    assert_eq!(pad.state(), None);
    assert_eq!(
        pad.press(Button::A, &token).unwrap_err(),
        DeviceError::Closed
    );
}

#[test]
fn generic_buttons_preserve_other_state_with_easycon_backend() {
    let state = easycon();
    let pad = connect(EasyCon, &state).unwrap();
    assert_eq!(pad.info().backend, "easycon");
    exercise_buttons(Box::new(pad), Delivery::Written);
    assert_eq!(state.lock().unwrap().dropped, 1);
    assert_eq!(&wire(&state)[..3], &[0xa5, 0xa5, 0x81]);
}

#[test]
fn generic_buttons_preserve_other_state_with_pokeldn_backend() {
    let state = pokeldn();
    let pad = connect(Pokeldn, &state).unwrap();
    assert_eq!(pad.info().baud_rate, 921_600);
    exercise_buttons(Box::new(pad), Delivery::Accepted);
    assert_eq!(state.lock().unwrap().dropped, 1);
}

#[test]
fn fallback_releases_previous_port_before_opening_next_rate() {
    let first = Arc::new(Mutex::new(State::default()));
    let second = easycon();
    let mut attempted = Vec::new();
    let mut pad = DeviceSession::connect_with(
        EasyCon,
        "fake-port",
        options(),
        &CancellationToken::new(),
        |name, baud, lines| {
            assert_eq!(name, "fake-port");
            assert_eq!(lines.dtr_on_open, !cfg!(target_os = "windows"));
            attempted.push(baud);
            if baud == 115_200 {
                Ok(FakeTransport(first.clone()))
            } else {
                assert_eq!(first.lock().unwrap().dropped, 1);
                Ok(FakeTransport(second.clone()))
            }
        },
    )
    .unwrap();
    assert_eq!(attempted, [115_200, 9_600]);
    assert_eq!(pad.info().baud_rate, 9_600);
    pad.disconnect().unwrap();
}

#[test]
fn all_failed_attempts_are_preserved_and_selected_baud_never_falls_back() {
    let failure =
        |_: &str, _: u32, _: easycon_device::LinePolicy| -> easycon_device::Result<FakeTransport> {
            Err(DeviceError::Io {
                operation: "open",
                kind: io::ErrorKind::PermissionDenied,
                message: "denied".into(),
            })
        };
    let error = DeviceSession::connect_with(
        EasyCon,
        "fake",
        options(),
        &CancellationToken::new(),
        failure,
    )
    .err()
    .unwrap();
    assert!(
        matches!(error, DeviceError::ConnectionFailed { ref attempts }
        if attempts.iter().map(|attempt| attempt.baud_rate).collect::<Vec<_>>() == [115_200, 9_600])
    );
    let error = DeviceSession::connect_with(
        EasyCon,
        "fake",
        ConnectOptions {
            baud_rate: Some(9_600),
            ..options()
        },
        &CancellationToken::new(),
        failure,
    )
    .err()
    .unwrap();
    assert!(
        matches!(error, DeviceError::ConnectionFailed { ref attempts } if attempts.len() == 1 && attempts[0].baud_rate == 9_600)
    );
}

#[test]
fn invalid_options_never_open_a_port() {
    for (name, options) in [
        ("", options()),
        (
            "fake",
            ConnectOptions {
                baud_rate: Some(921_600),
                ..options()
            },
        ),
        (
            "fake",
            ConnectOptions {
                write_timeout: Some(Duration::ZERO),
                ..options()
            },
        ),
        (
            "fake",
            ConnectOptions {
                handshake_timeout: Some(Duration::MAX),
                ..options()
            },
        ),
    ] {
        let result = DeviceSession::<_, FakeTransport>::connect_with(
            EasyCon,
            name,
            options,
            &CancellationToken::new(),
            |_, _, _| panic!("invalid input opened a port"),
        );
        assert!(matches!(result, Err(DeviceError::InvalidArgument(_))));
    }
    assert!(Hat::try_from(9).is_err());
}

#[test]
fn cancelled_handshake_stops_probe_and_does_not_fall_back() {
    let token = CancellationToken::new();
    let state = Arc::new(Mutex::new(State::default()));
    state
        .lock()
        .unwrap()
        .reads
        .push_back(ReadStep::Cancel(token.clone()));
    let mut attempts = 0;
    let result = DeviceSession::connect_with(EasyCon, "fake", options(), &token, |_, _, _| {
        attempts += 1;
        Ok(FakeTransport(state.clone()))
    });
    assert!(matches!(result, Err(DeviceError::Cancelled)));
    assert_eq!(attempts, 1);
    assert_eq!(state.lock().unwrap().dropped, 1);
}

#[test]
fn cancellation_before_report_preserves_state_and_sequence() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    let before = wire(&state);
    let cancelled = CancellationToken::new();
    cancelled.cancel();
    assert_eq!(
        pad.press(Button::A, &cancelled).unwrap_err(),
        DeviceError::Cancelled
    );
    assert_eq!(wire(&state), before);
    assert_eq!(pad.state(), Some(Report::default()));
    assert_eq!(
        pad.press(Button::B, &CancellationToken::new())
            .unwrap()
            .sequence,
        2
    );
    pad.disconnect().unwrap();
}

#[test]
fn partial_writes_finish_packet_even_if_cancelled_after_first_byte() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    let before = wire(&state).len();
    let token = CancellationToken::new();
    state.lock().unwrap().write_steps.extend([
        WriteStep::CancelAfter(1, token.clone()),
        WriteStep::Limit(2),
    ]);
    let receipt = pad.press(Button::A, &token).unwrap();
    assert_eq!(receipt.sequence, 2);
    assert!(token.is_cancelled());
    assert_eq!(&wire(&state)[before..], &[0, 1, 1, 8, 4, 2, 1, 0x80]);
    assert_eq!(pad.state().unwrap().buttons, Button::A.mask());
    pad.disconnect().unwrap();
}

#[test]
fn partial_write_failure_faults_session_without_replaying_action() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    state.lock().unwrap().write_steps.extend([
        WriteStep::Limit(2),
        WriteStep::Fail(io::ErrorKind::BrokenPipe),
    ]);
    let error = pad.press(Button::A, &CancellationToken::new()).unwrap_err();
    let before = wire(&state);
    assert!(!pad.is_connected());
    assert_eq!(pad.state(), None);
    assert_eq!(
        pad.press(Button::B, &CancellationToken::new()).unwrap_err(),
        error
    );
    assert_eq!(wire(&state), before);
    pad.disconnect().unwrap();
    assert_eq!(&wire(&state)[before.len()..], &[0, 0, 1, 8, 4, 2, 1, 0x80]);
    assert_eq!(state.lock().unwrap().dropped, 1);
}

#[test]
fn cleanup_failure_still_releases_port_and_disconnect_is_idempotent() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    state.lock().unwrap().write_steps.push_back(WriteStep::Zero);
    let error = pad.disconnect().unwrap_err();
    assert!(matches!(error, DeviceError::CleanupFailed { .. }));
    assert_eq!(state.lock().unwrap().dropped, 1);
    assert_eq!(pad.disconnect().unwrap_err(), error);
    assert_eq!(state.lock().unwrap().dropped, 1);
    assert!(!pad.is_connected());
}

#[test]
fn report_write_timeout_is_bounded_and_does_not_leave_session_ready() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    state
        .lock()
        .unwrap()
        .write_steps
        .extend((0..500).map(|_| WriteStep::Fail(io::ErrorKind::TimedOut)));
    assert_eq!(
        pad.press(Button::A, &CancellationToken::new()).unwrap_err(),
        DeviceError::WriteTimeout { written: 0 }
    );
    assert!(!pad.is_connected());
    state.lock().unwrap().write_steps.clear();
    pad.disconnect().unwrap();
}

#[test]
fn minimum_interval_is_enforced_and_drop_releases_buttons() {
    let state = easycon();
    {
        let mut pad = connect(EasyCon, &state).unwrap();
        pad.press(Button::A, &CancellationToken::new()).unwrap();
    }
    let locked = state.lock().unwrap();
    // Handshake, initial neutral, press, final neutral.
    assert_eq!(locked.writes.len(), 4);
    for pair in locked.writes[1..].windows(2) {
        assert!(pair[1].1.duration_since(pair[0].1) >= Duration::from_millis(30));
    }
    assert_eq!(locked.writes.last().unwrap().0, [0, 0, 1, 8, 4, 2, 1, 0x80]);
    assert_eq!(locked.dropped, 1);
}

#[test]
fn new_connection_has_a_different_session_identity() {
    let mut first = connect(EasyCon, &easycon()).unwrap();
    let mut second = connect(EasyCon, &easycon()).unwrap();
    assert_ne!(first.info().session, second.info().session);
    first.disconnect().unwrap();
    second.disconnect().unwrap();
}

#[test]
fn pokeldn_probe_recovers_from_noise_bad_checksum_and_split_reply() {
    let state = pokeldn();
    {
        let mut locked = state.lock().unwrap();
        locked.query_reply_after = None;
        locked.reads.extend([
            ReadStep::Bytes(vec![0, 0xa5, 0x5a, 1, 0, 0x82, 0]),
            ReadStep::Bytes(vec![0xa5]),
            ReadStep::Bytes(vec![0x5a, 6]),
            ReadStep::Bytes(vec![0, 0x82, 0, 0, 0, 0, 0, 0x82]),
        ]);
    }
    let mut pad = DeviceSession::connect_with(
        Pokeldn,
        "fake",
        options(),
        &CancellationToken::new(),
        |_, baud, lines| {
            assert_eq!(baud, 921_600);
            assert!(!lines.dtr_on_open);
            Ok(FakeTransport(state.clone()))
        },
    )
    .unwrap();
    pad.disconnect().unwrap();
}

#[test]
fn pokeldn_boot_delay_triggers_periodic_status_query() {
    let state = pokeldn();
    state.lock().unwrap().query_reply_after = Some(2);
    let started = Instant::now();
    let mut pad = DeviceSession::connect_with(
        Pokeldn,
        "fake",
        ConnectOptions {
            handshake_timeout: Some(Duration::from_secs(1)),
            ..options()
        },
        &CancellationToken::new(),
        |_, _, _| Ok(FakeTransport(state.clone())),
    )
    .unwrap();
    assert!(started.elapsed() >= Duration::from_millis(300));
    assert_eq!(state.lock().unwrap().query_count, 2);
    pad.disconnect().unwrap();
}

#[test]
fn pokeldn_rejection_is_an_error_and_ack_loss_faults_session() {
    for code in [Some(-1), Some(-2), None] {
        let state = pokeldn();
        let mut pad = connect(Pokeldn, &state).unwrap();
        state.lock().unwrap().report_result = code;
        let error = pad.press(Button::A, &CancellationToken::new()).unwrap_err();
        match code {
            Some(code) => assert_eq!(error, DeviceError::ReportRejected { code }),
            None => assert_eq!(error, DeviceError::AcknowledgementTimeout),
        }
        assert!(!pad.is_connected());
        assert_eq!(pad.state(), None);
        state.lock().unwrap().report_result = Some(0);
        pad.disconnect().unwrap();
    }
}

#[test]
fn invalid_report_never_reaches_serial_port() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    let before = wire(&state);
    assert!(matches!(
        pad.apply_report(
            Report {
                buttons: 0x8000,
                ..Report::default()
            },
            &CancellationToken::new()
        ),
        Err(DeviceError::InvalidArgument(_))
    ));
    assert_eq!(wire(&state), before);
    assert!(pad.is_connected());
    pad.disconnect().unwrap();
}

#[test]
fn physical_read_error_is_retained_as_connection_attempt_failure() {
    let state = easycon();
    state.lock().unwrap().reads = [ReadStep::Fail(io::ErrorKind::BrokenPipe)].into();
    let error = DeviceSession::connect_with(
        EasyCon,
        "fake",
        ConnectOptions {
            baud_rate: Some(115_200),
            ..options()
        },
        &CancellationToken::new(),
        |_, _, _| Ok(FakeTransport(state.clone())),
    )
    .err()
    .unwrap();
    assert!(
        matches!(error, DeviceError::ConnectionFailed { attempts } if matches!(attempts[0].error, DeviceError::Io { kind: io::ErrorKind::BrokenPipe, .. }))
    );
    assert_eq!(state.lock().unwrap().dropped, 1);
}

#[test]
fn pokeldn_ack_wait_handles_status_interleaving_and_fragmented_result() {
    let state = pokeldn();
    let mut pad = connect(Pokeldn, &state).unwrap();
    {
        let mut locked = state.lock().unwrap();
        locked.report_result = None;
        let mut bad = easycon_device::backends::pokeldn::frame(0x81, &[0]).unwrap();
        *bad.last_mut().unwrap() ^= 1;
        locked.reads.extend([
            ReadStep::Bytes(bad),
            ReadStep::Bytes(
                easycon_device::backends::pokeldn::frame(0x82, &[1, 0, 0, 0, 0]).unwrap(),
            ),
            ReadStep::Bytes(vec![0xa5, 0x5a, 2]),
            ReadStep::Bytes(vec![0, 0x81, 0, 0x81]),
        ]);
    }
    assert_eq!(
        pad.press(Button::A, &CancellationToken::new())
            .unwrap()
            .delivery,
        Delivery::Accepted
    );
    state.lock().unwrap().report_result = Some(0);
    pad.disconnect().unwrap();
}

#[test]
fn malformed_report_result_is_not_accepted() {
    let state = pokeldn();
    let mut pad = connect(Pokeldn, &state).unwrap();
    {
        let mut locked = state.lock().unwrap();
        locked.report_result = None;
        locked.reads.push_back(ReadStep::Bytes(
            easycon_device::backends::pokeldn::frame(0x81, &[]).unwrap(),
        ));
    }
    assert!(matches!(
        pad.press(Button::A, &CancellationToken::new()),
        Err(DeviceError::MalformedResponse(_))
    ));
    state.lock().unwrap().report_result = Some(0);
    pad.disconnect().unwrap();
}

#[test]
fn cancellation_after_first_ldn_byte_still_resolves_firmware_acceptance() {
    let state = pokeldn();
    let mut pad = connect(Pokeldn, &state).unwrap();
    let token = CancellationToken::new();
    state
        .lock()
        .unwrap()
        .write_steps
        .push_back(WriteStep::CancelAfter(1, token.clone()));
    assert_eq!(
        pad.press(Button::A, &token).unwrap().delivery,
        Delivery::Accepted
    );
    assert!(token.is_cancelled());
    assert_eq!(pad.state().unwrap().buttons, Button::A.mask());
    pad.disconnect().unwrap();
}

#[test]
fn failed_initial_neutral_never_exposes_ready_session_and_releases_port() {
    let state = pokeldn();
    state.lock().unwrap().report_result = None;
    let result = connect(Pokeldn, &state);
    assert!(
        matches!(result, Err(DeviceError::CleanupFailed { previous: Some(previous), .. }) if *previous == DeviceError::AcknowledgementTimeout)
    );
    assert_eq!(state.lock().unwrap().dropped, 1);
}

#[test]
fn interrupted_and_temporarily_blocked_partial_writes_resume_at_correct_offset() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    let before = wire(&state).len();
    state.lock().unwrap().write_steps.extend([
        WriteStep::Fail(io::ErrorKind::Interrupted),
        WriteStep::Limit(2),
        WriteStep::Fail(io::ErrorKind::WouldBlock),
        WriteStep::Limit(3),
    ]);
    pad.press(Button::A, &CancellationToken::new()).unwrap();
    assert_eq!(&wire(&state)[before..], &[0, 1, 1, 8, 4, 2, 1, 0x80]);
    pad.disconnect().unwrap();
}

#[test]
fn malformed_status_does_not_establish_connection() {
    let state = pokeldn();
    {
        let mut locked = state.lock().unwrap();
        locked.query_reply_after = None;
        locked.reads.push_back(ReadStep::Bytes(
            easycon_device::backends::pokeldn::frame(0x82, &[]).unwrap(),
        ));
    }
    let error = connect(Pokeldn, &state).err().unwrap();
    assert!(
        matches!(error, DeviceError::ConnectionFailed { attempts } if matches!(attempts[0].error, DeviceError::MalformedResponse(_)))
    );
    assert_eq!(state.lock().unwrap().dropped, 1);
}

type ControlAction = fn(
    &mut dyn Gamepad,
    &CancellationToken,
) -> easycon_device::Result<easycon_device::WriteReceipt>;

fn control_actions() -> [ControlAction; 7] {
    [
        |pad, token| pad.press_buttons(&[Button::A, Button::ZL, Button::A], token),
        |pad, token| pad.release_buttons(&[Button::B, Button::A, Button::B], token),
        |pad, token| pad.set_hat(Hat::UpRight, token),
        |pad, token| pad.set_stick(StickId::Left, Stick { x: 0, y: 255 }, token),
        |pad, token| pad.set_stick(StickId::Right, Stick { x: 255, y: 0 }, token),
        |pad, token| pad.center_stick(StickId::Left, token),
        |pad, token| pad.center_stick(StickId::Right, token),
    ]
}

/// Read what reached the transport, normalized to buttons BE / HAT / LX / LY / RX / RY.
fn sent_reports(state: &Shared, backend: &str) -> Vec<[u8; 7]> {
    let bytes = wire(state);
    if backend == "easycon" {
        assert_eq!(&bytes[..3], &[0xa5, 0xa5, 0x81]);
        let (packets, remainder) = bytes[3..].as_chunks::<8>();
        assert!(remainder.is_empty());
        packets
            .iter()
            .map(|packet| {
                let bits = packet
                    .iter()
                    .fold(0u64, |bits, byte| (bits << 7) | u64::from(byte & 0x7f));
                bits.to_be_bytes()[1..].try_into().unwrap()
            })
            .collect()
    } else {
        let mut decoder = easycon_device::backends::pokeldn::FrameDecoder::default();
        bytes
            .into_iter()
            .filter_map(|byte| decoder.push(byte))
            .filter(|frame| frame.kind == 1)
            .map(|frame| {
                let p = frame.payload;
                assert_eq!(p.len(), 8);
                assert_eq!(p[7], 0);
                [p[1], p[0], p[2], p[3], p[4], p[5], p[6]]
            })
            .collect()
    }
}

fn exercise_controls(mut pad: Box<dyn Gamepad>, state: &Shared, delivery: Delivery) {
    let token = CancellationToken::new();
    pad.apply_report(
        Report {
            buttons: Button::B.mask(),
            hat: Hat::DownLeft,
            left: Stick { x: 12, y: 34 },
            right: Stick { x: 56, y: 78 },
        },
        &token,
    )
    .unwrap();
    for (index, action) in control_actions().into_iter().enumerate() {
        let receipt = action(pad.as_mut(), &token).unwrap();
        assert_eq!(receipt.sequence, index as u64 + 3);
        assert_eq!(receipt.delivery, delivery);
        assert_eq!(receipt.session, pad.info().session);
    }
    assert_eq!(pad.state().unwrap().buttons, Button::ZL.mask());
    assert_eq!(pad.state().unwrap().hat, Hat::UpRight);
    assert_eq!(pad.state().unwrap().left, Stick::default());
    assert_eq!(pad.state().unwrap().right, Stick::default());
    pad.disconnect().unwrap();
    assert_eq!(
        sent_reports(state, pad.info().backend),
        [
            [0, 0, 8, 128, 128, 128, 128],
            [0, 2, 5, 12, 34, 56, 78],
            [0, 0x46, 5, 12, 34, 56, 78],
            [0, 0x40, 5, 12, 34, 56, 78],
            [0, 0x40, 1, 12, 34, 56, 78],
            [0, 0x40, 1, 0, 255, 56, 78],
            [0, 0x40, 1, 0, 255, 255, 0],
            [0, 0x40, 1, 128, 128, 255, 0],
            [0, 0x40, 1, 128, 128, 128, 128],
            [0, 0, 8, 128, 128, 128, 128],
        ]
    );
    for action in control_actions() {
        assert_eq!(
            action(pad.as_mut(), &token).unwrap_err(),
            DeviceError::Closed
        );
    }
}

#[test]
fn easycon_atomic_controls_preserve_unselected_fields_in_transmitted_reports() {
    let state = easycon();
    exercise_controls(
        Box::new(connect(EasyCon, &state).unwrap()),
        &state,
        Delivery::Written,
    );
}

#[test]
fn pokeldn_atomic_controls_preserve_unselected_fields_in_accepted_reports() {
    let state = pokeldn();
    exercise_controls(
        Box::new(connect(Pokeldn, &state).unwrap()),
        &state,
        Delivery::Accepted,
    );
}

#[test]
fn every_hat_direction_and_neutral_reaches_both_backends() {
    let token = CancellationToken::new();
    let easy = easycon();
    let ldn = pokeldn();
    let pads: [(Box<dyn Gamepad>, &Shared); 2] = [
        (Box::new(connect(EasyCon, &easy).unwrap()), &easy),
        (Box::new(connect(Pokeldn, &ldn).unwrap()), &ldn),
    ];
    for (mut pad, state) in pads {
        for hat in 0..=8 {
            pad.set_hat(Hat::try_from(hat).unwrap(), &token).unwrap();
        }
        let reports = sent_reports(state, pad.info().backend);
        assert_eq!(reports.len(), 10);
        for (hat, report) in reports[1..].iter().enumerate() {
            assert_eq!(*report, [0, 0, hat as u8, 128, 128, 128, 128]);
        }
        pad.disconnect().unwrap();
    }
    assert!(Hat::try_from(9).is_err());
    assert!(Hat::try_from(255).is_err());
}

#[test]
fn repeated_or_empty_button_selections_each_send_one_report_without_reference_counts() {
    let state = easycon();
    let mut pad = connect(EasyCon, &state).unwrap();
    let token = CancellationToken::new();
    pad.press_buttons(&[Button::A, Button::B], &token).unwrap();
    pad.press_buttons(&[Button::A], &token).unwrap();
    pad.press_buttons(&[], &token).unwrap();
    pad.release_buttons(&[Button::A], &token).unwrap();
    pad.release_buttons(&[Button::A], &token).unwrap();
    pad.release_buttons(&[], &token).unwrap();
    let reports = sent_reports(&state, "easycon");
    assert_eq!(reports.len(), 7);
    assert_eq!(
        reports.iter().map(|report| report[1]).collect::<Vec<_>>(),
        [0, 6, 6, 6, 2, 2, 2]
    );
    pad.disconnect().unwrap();
}

#[test]
fn cancelled_atomic_controls_do_not_write_or_change_state_or_sequence() {
    let easy = easycon();
    let ldn = pokeldn();
    let pads: [(Box<dyn Gamepad>, &Shared); 2] = [
        (Box::new(connect(EasyCon, &easy).unwrap()), &easy),
        (Box::new(connect(Pokeldn, &ldn).unwrap()), &ldn),
    ];
    let cancelled = CancellationToken::new();
    cancelled.cancel();
    for (mut pad, state) in pads {
        let before = wire(state);
        for action in control_actions() {
            assert_eq!(
                action(pad.as_mut(), &cancelled).unwrap_err(),
                DeviceError::Cancelled
            );
            assert_eq!(pad.state(), Some(Report::default()));
            assert_eq!(wire(state), before);
            assert!(pad.is_connected());
        }
        assert_eq!(
            pad.set_hat(Hat::Left, &CancellationToken::new())
                .unwrap()
                .sequence,
            2
        );
        pad.disconnect().unwrap();
    }
}

#[test]
fn atomic_controls_cannot_resume_after_partial_write_or_firmware_rejection() {
    let easy = easycon();
    let ldn = pokeldn();
    let pads: [(Box<dyn Gamepad>, &Shared); 2] = [
        (Box::new(connect(EasyCon, &easy).unwrap()), &easy),
        (Box::new(connect(Pokeldn, &ldn).unwrap()), &ldn),
    ];
    let token = CancellationToken::new();
    for (mut pad, state) in pads {
        if pad.info().backend == "easycon" {
            state.lock().unwrap().write_steps.extend([
                WriteStep::Limit(2),
                WriteStep::Fail(io::ErrorKind::BrokenPipe),
            ]);
        } else {
            state.lock().unwrap().report_result = Some(-2);
        }
        let error = pad
            .set_stick(StickId::Left, Stick { x: 255, y: 0 }, &token)
            .unwrap_err();
        assert!(!pad.is_connected());
        assert_eq!(pad.state(), None);
        let before = wire(state);
        for action in control_actions() {
            assert_eq!(action(pad.as_mut(), &token).unwrap_err(), error);
            assert_eq!(wire(state), before);
            assert_eq!(pad.state(), None);
        }
        state.lock().unwrap().report_result = Some(0);
        pad.disconnect().unwrap();
    }
}
