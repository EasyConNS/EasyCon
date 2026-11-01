use std::sync::Mutex;

use super::*;

#[derive(Default)]
struct Recorder(Mutex<Vec<LogRecord>>);

impl LogSink for Recorder {
    fn emit(&self, record: LogRecord) -> std::result::Result<(), String> {
        self.0.lock().unwrap().push(record);
        Ok(())
    }
}

#[tokio::test(start_paused = true)]
async fn long_sleep_stops_at_cancellation_without_waiting_for_its_deadline() {
    let token = CancellationToken::new();
    let child = token.clone();
    let start = Instant::now();
    let waiter = tokio::spawn(async move { sleep(10_000, &child).await });
    tokio::time::sleep(Duration::from_millis(500)).await;
    token.cancel();
    assert_eq!(waiter.await.unwrap(), Err(MetaError::Cancelled));
    assert_eq!(start.elapsed(), Duration::from_millis(500));
}

#[tokio::test(start_paused = true)]
async fn sleep_validates_duration_and_does_not_wait_when_already_cancelled() {
    let token = CancellationToken::new();
    assert!(matches!(
        sleep(-1, &token).await,
        Err(MetaError::InvalidArgument(_))
    ));
    let start = Instant::now();
    sleep(123, &token).await.unwrap();
    assert_eq!(start.elapsed(), Duration::from_millis(123));
    token.cancel();
    assert_eq!(sleep(10_000, &token).await, Err(MetaError::Cancelled));
    assert_eq!(start.elapsed(), Duration::from_millis(123));
}

#[tokio::test(start_paused = true)]
async fn zero_sleep_yields_once_and_observes_cancellation() {
    use std::{
        future::Future,
        task::{Context, Poll, Waker},
    };

    let token = CancellationToken::new();
    let mut future = Box::pin(sleep(0, &token));
    let mut context = Context::from_waker(Waker::noop());
    assert_eq!(future.as_mut().poll(&mut context), Poll::Pending);
    future.await.unwrap();
    let mut future = Box::pin(sleep(0, &token));
    assert_eq!(future.as_mut().poll(&mut context), Poll::Pending);
    token.cancel();
    assert_eq!(future.await, Err(MetaError::Cancelled));
}

#[tokio::test(start_paused = true)]
async fn clock_clones_share_a_run_but_new_contexts_start_at_zero() {
    let sink = Arc::new(Recorder::default());
    let clock = MetaContext::new(sink.clone());
    let clone = clock.clone();
    let token = CancellationToken::new();
    tokio::time::advance(Duration::from_micros(123_999)).await;
    assert_eq!(clock.clock_now(&token), Ok(123));
    assert_eq!(clone.clock_now(&token), Ok(123));
    assert_eq!(MetaContext::new(sink).clock_now(&token), Ok(0));
    token.cancel();
    assert_eq!(clone.clock_now(&token), Err(MetaError::Cancelled));
}

#[tokio::test(start_paused = true)]
async fn log_delivers_typed_fields_once_with_a_run_timestamp() {
    let sink = Arc::new(Recorder::default());
    let context = MetaContext::new(sink.clone());
    let token = CancellationToken::new();
    let fields = BTreeMap::from([
        ("empty".into(), LogValue::Null),
        ("matched".into(), LogValue::Bool(true)),
        ("attempt".into(), LogValue::Int(2)),
        ("score".into(), LogValue::Float(0.75)),
        ("target".into(), LogValue::Str("按钮".into())),
    ]);
    tokio::time::advance(Duration::from_millis(42)).await;
    context
        .log(LogLevel::Info, "识别结果", fields.clone(), &token)
        .unwrap();
    assert_eq!(
        *sink.0.lock().unwrap(),
        vec![LogRecord {
            timestamp_ms: 42,
            level: LogLevel::Info,
            message: "识别结果".into(),
            fields,
        }]
    );
    for n in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        assert!(matches!(
            context.log(
                LogLevel::Info,
                "bad",
                BTreeMap::from([("score".into(), LogValue::Float(n)),]),
                &token
            ),
            Err(MetaError::InvalidArgument(_))
        ));
    }
    token.cancel();
    assert_eq!(
        context.log(LogLevel::Info, "cancelled", BTreeMap::new(), &token),
        Err(MetaError::Cancelled)
    );
    assert_eq!(sink.0.lock().unwrap().len(), 1);
}

#[test]
fn sink_failure_is_reported_once_even_if_it_also_cancels_the_run() {
    struct FailingSink(CancellationToken, Mutex<usize>);
    impl LogSink for FailingSink {
        fn emit(&self, _: LogRecord) -> std::result::Result<(), String> {
            *self.1.lock().unwrap() += 1;
            self.0.cancel();
            Err("disk full".into())
        }
    }
    let token = CancellationToken::new();
    let sink = Arc::new(FailingSink(token.clone(), Mutex::new(0)));
    let context = MetaContext::new(sink.clone());
    assert_eq!(
        context.log(LogLevel::Error, "once", BTreeMap::new(), &token),
        Err(MetaError::Sink("disk full".into()))
    );
    assert_eq!(*sink.1.lock().unwrap(), 1);
}

#[test]
fn cancellation_after_successful_delivery_does_not_hide_the_log() {
    struct CancellingSink(CancellationToken);
    impl LogSink for CancellingSink {
        fn emit(&self, _: LogRecord) -> std::result::Result<(), String> {
            self.0.cancel();
            Ok(())
        }
    }
    let token = CancellationToken::new();
    let context = MetaContext::new(Arc::new(CancellingSink(token.clone())));
    assert_eq!(
        context.log(LogLevel::Info, "delivered", BTreeMap::new(), &token),
        Ok(())
    );
    assert!(token.is_cancelled());
}
