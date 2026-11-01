use super::*;
use std::time::Duration;

fn fixture(channels: u16, capacity: usize) -> (Arc<AudioShared>, AudioIngress) {
    let shared = AudioShared::new(
        "fake-audio".into(),
        AudioFormat {
            sample_rate: 1_000,
            channels,
            sample_format: "f32".into(),
        },
        capacity,
    )
    .unwrap();
    let ingress = AudioIngress {
        shared: shared.clone(),
        next_frame: 0,
    };
    (shared, ingress)
}

fn time() -> SampleTime {
    SampleTime {
        received_ns: 123,
        source_ns: Some(0),
    }
}

async fn read(reader: &mut AudioReader, frames: usize) -> Result<Arc<AudioWindow>> {
    tokio::time::timeout(
        Duration::from_secs(1),
        reader.read_window(frames, &CancellationToken::new()),
    )
    .await
    .expect("audio reader did not wake")
}

#[tokio::test]
async fn normalizes_integer_samples_and_preserves_identity_and_time() {
    let (shared, mut input) = fixture(2, 8);
    let mut reader = shared.subscribe().unwrap();
    input.push(&[i16::MIN, 0, 16_384, i16::MAX], time());
    let window = read(&mut reader, 2).await.unwrap();
    assert_eq!(window.frames(), 2);
    assert_eq!(&window.samples[..3], &[-1.0, 0.0, 0.5]);
    assert!(window.samples[3] > 0.999);
    assert_eq!(window.id, shared.id);
    assert_eq!(window.time, time());
    assert_eq!(window.sample_rate, 1_000);
    assert_eq!(window.channels, 2);
    let mut unsigned = shared.subscribe().unwrap();
    input.push(&[0u8, 128, 255, 128], time());
    let window = read(&mut unsigned, 1).await.unwrap();
    assert_eq!(&*window.samples, &[-1.0, 0.0]);
}

#[tokio::test]
async fn consecutive_windows_span_callbacks_and_wrap_without_competing() {
    let (shared, mut input) = fixture(1, 4);
    let mut first = shared.subscribe().unwrap();
    let mut second = shared.subscribe().unwrap();
    input.push(&[0.1f32, 0.2], time());
    let original = read(&mut first, 2).await.unwrap();
    assert_eq!(&*read(&mut second, 2).await.unwrap().samples, &[0.1, 0.2]);
    input.push(
        &[0.3f32],
        SampleTime {
            received_ns: 456,
            source_ns: Some(2_000_000),
        },
    );
    input.push(
        &[0.4f32, 0.5],
        SampleTime {
            received_ns: 789,
            source_ns: Some(3_000_000),
        },
    );
    let window = read(&mut first, 3).await.unwrap();
    assert_eq!(window.id.sequence, 2);
    assert_eq!(&*window.samples, &[0.3, 0.4, 0.5]);
    assert_eq!(window.time.source_ns, Some(2_000_000));
    assert_eq!(
        &*read(&mut second, 3).await.unwrap().samples,
        &*window.samples
    );
    assert_eq!(&*original.samples, &[0.1, 0.2]);
}

#[tokio::test]
async fn slow_reader_gets_gap_while_fast_reader_continues() {
    let (shared, mut input) = fixture(1, 3);
    let mut slow = shared.subscribe().unwrap();
    let mut fast = shared.subscribe().unwrap();
    input.push(&[0.1f32, 0.2], time());
    read(&mut fast, 2).await.unwrap();
    input.push(&[0.3f32, 0.4], time());
    assert_eq!(
        read(&mut slow, 1).await.unwrap_err(),
        CaptureError::AudioGap {
            expected: 0,
            oldest: 1
        }
    );
    assert_eq!(&*read(&mut fast, 2).await.unwrap().samples, &[0.3, 0.4]);
}

#[tokio::test]
async fn callback_contention_is_explicit_and_new_subscription_recovers() {
    let (shared, mut input) = fixture(1, 8);
    let mut reader = shared.subscribe().unwrap();
    let guard = shared.state.lock().unwrap();
    input.push(&[0.1f32, 0.2], time());
    drop(guard);
    assert_eq!(shared.dropped_frames.load(Ordering::Relaxed), 2);
    assert!(matches!(
        read(&mut reader, 1).await,
        Err(CaptureError::AudioGap { .. })
    ));
    input.push(&[0.3f32], time());
    let mut recovered = shared.subscribe().unwrap();
    input.push(&[0.4f32], time());
    let window = read(&mut recovered, 1).await.unwrap();
    assert_eq!(window.id.sequence, 3);
    assert_eq!(&*window.samples, &[0.4]);
    assert!(matches!(
        read(&mut reader, 1).await,
        Err(CaptureError::AudioGap { .. })
    ));
}

#[tokio::test]
async fn partial_window_waits_without_consuming_then_wakes() {
    let (shared, mut input) = fixture(1, 8);
    let mut reader = shared.subscribe().unwrap();
    input.push(&[0.1f32], time());
    let token = CancellationToken::new();
    assert!(
        tokio::time::timeout(Duration::from_millis(10), reader.read_window(2, &token))
            .await
            .is_err()
    );
    let (window, ()) = tokio::join!(read(&mut reader, 2), async {
        tokio::task::yield_now().await;
        input.push(&[0.2f32], time());
    });
    assert_eq!(&*window.unwrap().samples, &[0.1, 0.2]);
}

#[tokio::test]
async fn cancellation_does_not_consume_cursor_or_stop_other_readers() {
    let (shared, mut input) = fixture(1, 8);
    let mut reader = shared.subscribe().unwrap();
    let mut other = shared.subscribe().unwrap();
    let token = CancellationToken::new();
    let (result, ()) = tokio::join!(reader.read_window(1, &token), async {
        tokio::task::yield_now().await;
        token.cancel();
    });
    assert_eq!(result.unwrap_err(), CaptureError::Cancelled);
    input.push(&[0.5f32], time());
    assert_eq!(read(&mut reader, 1).await.unwrap().id.sequence, 0);
    assert_eq!(read(&mut other, 1).await.unwrap().id.sequence, 0);
}

#[tokio::test]
async fn terminal_error_wakes_waiter_and_hides_buffered_data() {
    let (shared, mut input) = fixture(1, 8);
    let mut reader = shared.subscribe().unwrap();
    let (result, ()) = tokio::join!(read(&mut reader, 1), async {
        tokio::task::yield_now().await;
        shared.fail(CaptureError::Stopped);
    });
    assert_eq!(result.unwrap_err(), CaptureError::Stopped);
    input.push(&[0.5f32], time());
    assert_eq!(
        read(&mut reader, 1).await.unwrap_err(),
        CaptureError::Stopped
    );
    assert!(matches!(shared.subscribe(), Err(CaptureError::Stopped)));
    shared.fail(CaptureError::WorkerPanicked);
    assert_eq!(
        read(&mut reader, 1).await.unwrap_err(),
        CaptureError::Stopped
    );
}

#[tokio::test]
async fn invalid_windows_and_malformed_samples_are_rejected() {
    let (shared, mut input) = fixture(2, 4);
    let mut reader = shared.subscribe().unwrap();
    for frames in [0, 5] {
        assert!(matches!(
            read(&mut reader, frames).await,
            Err(CaptureError::InvalidArgument(_))
        ));
    }
    input.push(&[0.0f32], time());
    assert!(matches!(
        read(&mut reader, 1).await,
        Err(CaptureError::InvalidData(_))
    ));
    let (shared, mut input) = fixture(1, 4);
    let mut reader = shared.subscribe().unwrap();
    input.push(&[f32::NAN], time());
    assert!(matches!(
        read(&mut reader, 1).await,
        Err(CaptureError::InvalidData(_))
    ));
}

#[test]
fn timestamp_offsets_and_ring_bounds_are_checked() {
    let mut ring = AudioRing::new(1, 4).unwrap();
    ring.push(0, &[0.1f32, 0.2], time(), 1_000).unwrap();
    assert_eq!(ring.window(1, 1).unwrap().1.source_ns, Some(1_000_000));
    assert!(ring.window(3, 1).is_none());
    assert!(AudioRing::new(0, 4).is_err());
    assert!(AudioRing::new(2, usize::MAX).is_err());
}

#[test]
fn rms_is_pure_and_rejects_invalid_data() {
    let (shared, _) = fixture(1, 4);
    let mut window = AudioWindow {
        id: shared.id.clone(),
        time: time(),
        sample_rate: 1_000,
        channels: 1,
        samples: vec![0.5, -0.5].into(),
    };
    assert_eq!(window.rms().unwrap(), 0.5);
    for samples in [vec![], vec![f32::INFINITY], vec![f32::NAN]] {
        window.samples = samples.into();
        assert!(window.rms().is_err());
    }
    window.samples = vec![0.5].into();
    window.channels = 2;
    assert!(window.rms().is_err());
    window.channels = 0;
    assert_eq!(window.frames(), 0);
    assert!(window.rms().is_err());
}
