use super::*;
use crate::{Hat, Stick};

#[test]
fn query_and_neutral_report_match_known_vectors() {
    assert_eq!(frame(0x02, &[]).unwrap(), [0xa5, 0x5a, 1, 0, 2, 2]);
    assert_eq!(
        encode(&Report::default()).unwrap(),
        [0xa5, 0x5a, 9, 0, 1, 0, 0, 8, 128, 128, 128, 128, 0, 9]
    );
    let report = Report {
        buttons: 0x2084,
        hat: Hat::Right,
        left: Stick { x: 0, y: 255 },
        right: Stick { x: 1, y: 254 },
    };
    assert_eq!(
        encode(&report).unwrap(),
        [0xa5, 0x5a, 9, 0, 1, 0x84, 0x20, 2, 0, 255, 1, 254, 0, 0xa5]
    );
}

#[test]
fn incremental_parser_handles_fragmented_headers_and_back_to_back_frames() {
    let mut decoder = FrameDecoder::default();
    let bytes = [
        vec![0, 0xfe, 0xa5],
        frame(0x82, &[1, 2, 3]).unwrap(),
        frame(0x81, &[7]).unwrap(),
    ]
    .concat();
    let frames: Vec<_> = bytes
        .into_iter()
        .filter_map(|byte| decoder.push(byte))
        .collect();
    assert_eq!(
        frames,
        [
            Frame {
                kind: 0x82,
                payload: vec![1, 2, 3]
            },
            Frame {
                kind: 0x81,
                payload: vec![7]
            }
        ]
    );
    assert!(decoder.bytes.is_empty());
}

#[test]
fn invalid_length_and_checksum_resynchronize_and_storage_stays_bounded() {
    let mut decoder = FrameDecoder::default();
    let mut invalid = frame(0x82, &[1, 2]).unwrap();
    *invalid.last_mut().unwrap() ^= 0x01;
    let bytes = [
        vec![0xa5, 0x5a, 0, 0, 0xa5, 0x5a, 0xff, 0xff],
        invalid,
        frame(0x82, &[42]).unwrap(),
    ]
    .concat();
    let frames: Vec<_> = bytes
        .into_iter()
        .filter_map(|byte| decoder.push(byte))
        .collect();
    assert_eq!(
        frames,
        [Frame {
            kind: 0x82,
            payload: vec![42]
        }]
    );
    for value in (0..=255u8).cycle().take(100_000) {
        decoder.push(value);
        assert!(decoder.bytes.len() <= MAX_BODY + 5);
    }
    assert!(frame(1, &[0; 600]).is_err());
    assert!(
        encode(&Report {
            buttons: 0x8000,
            ..Report::default()
        })
        .is_err()
    );
}

#[test]
fn maximum_frame_and_wrapping_checksum_round_trip() {
    let payload = vec![255; 599];
    let bytes = frame(0xff, &payload).unwrap();
    assert_eq!(&bytes[2..4], &[0x58, 0x02]);
    let mut decoder = FrameDecoder::default();
    let frames: Vec<_> = bytes
        .into_iter()
        .filter_map(|byte| decoder.push(byte))
        .collect();
    assert_eq!(frames, [Frame { kind: 255, payload }]);
}

#[test]
fn resynchronization_can_drain_multiple_already_buffered_frames() {
    let first = frame(0x81, &[0]).unwrap();
    let second = frame(0x82, &[42]).unwrap();
    // A plausible but corrupt outer frame delays recovery until both inner frames are buffered.
    let size = first.len() + second.len();
    let mut bytes = vec![0xa5, 0x5a, size as u8, 0, 0xff];
    bytes.extend(first);
    bytes.extend(second);
    let mut decoder = FrameDecoder::default();
    let mut frames = Vec::new();
    for byte in bytes {
        if let Some(frame) = decoder.push(byte) {
            frames.push(frame);
        }
    }
    while let Some(frame) = decoder.next_frame() {
        frames.push(frame);
    }
    assert_eq!(
        frames,
        [
            Frame {
                kind: 0x81,
                payload: vec![0]
            },
            Frame {
                kind: 0x82,
                payload: vec![42]
            }
        ]
    );
}
