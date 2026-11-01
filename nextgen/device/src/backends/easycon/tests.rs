use super::*;
use crate::{Button, Hat, Stick};

#[test]
fn known_wire_vectors_match_existing_easycon_reports() {
    let vectors = [
        (
            Report::default(),
            [0x00, 0x00, 0x01, 0x08, 0x04, 0x02, 0x01, 0x80],
        ),
        (
            Report {
                buttons: Button::A.mask(),
                ..Report::default()
            },
            [0x00, 0x01, 0x01, 0x08, 0x04, 0x02, 0x01, 0x80],
        ),
        (
            Report {
                buttons: 0x2084,
                hat: Hat::Right,
                left: Stick { x: 0, y: 255 },
                right: Stick { x: 1, y: 254 },
            },
            [0x10, 0x21, 0x00, 0x20, 0x07, 0x7c, 0x03, 0xfe],
        ),
    ];
    for (report, expected) in vectors {
        assert_eq!(encode(&report).unwrap(), expected);
    }
}

#[test]
fn every_button_and_axis_boundary_preserves_all_payload_bits() {
    for bit in 0..14 {
        for value in [0, 1, 127, 128, 254, 255] {
            let report = Report {
                buttons: 1 << bit,
                hat: Hat::UpLeft,
                left: Stick {
                    x: value,
                    y: 255 - value,
                },
                right: Stick {
                    x: 255 - value,
                    y: value,
                },
            };
            let packet = encode(&report).unwrap();
            assert!(packet[..7].iter().all(|byte| byte & 0x80 == 0));
            assert_ne!(packet[7] & 0x80, 0);
            let reconstructed = packet
                .iter()
                .fold(0u64, |bits, byte| (bits << 7) | u64::from(byte & 0x7f));
            assert_eq!(
                &reconstructed.to_be_bytes()[1..],
                &payload(&report).unwrap()
            );
        }
    }
    assert!(
        encode(&Report {
            buttons: 0x4000,
            ..Report::default()
        })
        .is_err()
    );
    assert!(
        encode(&Report {
            buttons: 0x8000,
            ..Report::default()
        })
        .is_err()
    );
}
