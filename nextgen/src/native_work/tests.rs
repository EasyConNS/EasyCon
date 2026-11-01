use super::*;
use std::{cell::Cell, future::Future, rc::Rc, sync::Mutex, task::Poll};

struct LocalState {
    count: Rc<Cell<u32>>,
    owner: thread::ThreadId,
    dropped: Arc<Mutex<Option<thread::ThreadId>>>,
}
impl Drop for LocalState {
    fn drop(&mut self) {
        *self.dropped.lock().unwrap() = Some(thread::current().id());
    }
}

#[tokio::test]
async fn non_send_state_is_created_reused_and_destroyed_on_its_worker() {
    let dropped = Arc::new(Mutex::new(None));
    let observed = dropped.clone();
    let parent = CancellationToken::new();
    let mut worker = NativeWorker::start(
        move |_| {
            Ok::<_, &'static str>(LocalState {
                count: Rc::new(Cell::new(0)),
                owner: thread::current().id(),
                dropped: observed,
            })
        },
        &parent,
    )
    .await
    .unwrap();
    let owner = worker
        .call(&parent, |state, _| {
            assert_eq!(state.owner, thread::current().id());
            state.count.set(state.count.get() + 1);
            Ok::<_, &'static str>(state.owner)
        })
        .await
        .unwrap();
    assert_ne!(owner, thread::current().id());
    assert_eq!(
        worker
            .call_blocking(&parent, |state, _| {
                assert_eq!(state.owner, thread::current().id());
                Ok::<_, &'static str>(state.count.get())
            })
            .unwrap(),
        1
    );
    worker.shutdown().await.unwrap();
    assert_eq!(*dropped.lock().unwrap(), Some(owner));
}

#[tokio::test]
async fn cancellation_keeps_the_runtime_responsive_and_drains_before_reuse() {
    let lifetime = CancellationToken::new();
    let request = CancellationToken::new();
    let mut worker = NativeWorker::start(|_| Ok::<_, &'static str>(()), &lifetime)
        .await
        .unwrap();
    let (started_tx, started_rx) = oneshot::channel();
    let (release_tx, release_rx) = mpsc::channel();
    let finished = Arc::new(AtomicBool::new(false));
    let done = finished.clone();
    let work = worker.call(&request, move |_, token| {
        let _ = started_tx.send(());
        release_rx.recv_timeout(Duration::from_secs(2)).unwrap();
        done.store(true, Ordering::SeqCst);
        Ok::<_, &'static str>(token.is_cancelled())
    });
    let control = async {
        started_rx.await.unwrap();
        request.cancel();
        tokio::time::sleep(Duration::from_millis(5)).await;
        assert!(!finished.load(Ordering::SeqCst));
        release_tx.send(()).unwrap();
    };
    let (result, ()) = tokio::join!(work, control);
    // Generic work preserves the actual result; Pure recognition adapters discard cancelled results.
    assert!(result.unwrap());
    assert!(finished.load(Ordering::SeqCst));
    assert_eq!(
        worker
            .call(&CancellationToken::new(), |_, _| Ok::<_, &'static str>(42))
            .await
            .unwrap(),
        42
    );
    worker.shutdown().await.unwrap();
}

#[tokio::test]
async fn abandoned_jobs_are_bounded_cancelled_and_joined_by_shutdown() {
    let dropped = Arc::new(Mutex::new(None));
    let observed = dropped.clone();
    let parent = CancellationToken::new();
    let mut worker = NativeWorker::start(
        move |_| {
            Ok::<_, &'static str>(LocalState {
                count: Rc::new(Cell::new(0)),
                owner: thread::current().id(),
                dropped: observed,
            })
        },
        &parent,
    )
    .await
    .unwrap();
    let (started_tx, started_rx) = oneshot::channel();
    let (release_tx, release_rx) = mpsc::channel();
    let cancelled = Arc::new(AtomicBool::new(false));
    let saw_cancel = cancelled.clone();
    let mut first = Box::pin(worker.call(&parent, move |_, token| {
        let _ = started_tx.send(());
        release_rx.recv_timeout(Duration::from_secs(2)).unwrap();
        saw_cancel.store(token.is_cancelled(), Ordering::SeqCst);
        Ok::<_, &'static str>(())
    }));
    tokio::select! {
        _ = started_rx => {},
        result = &mut first => panic!("finished before release: {result:?}"),
    }
    drop(first);
    let mut queued = Box::pin(worker.call(&parent, |_, _| -> Result<(), &'static str> {
        panic!("abandoned queued job must never execute")
    }));
    std::future::poll_fn(|cx| {
        assert!(queued.as_mut().poll(cx).is_pending());
        Poll::Ready(())
    })
    .await;
    drop(queued);
    let third_token = CancellationToken::new();
    let mut third = Box::pin(
        worker.call(&third_token, |_, _| -> Result<(), &'static str> {
            panic!("cancelled admission must never execute")
        }),
    );
    std::future::poll_fn(|cx| {
        assert!(third.as_mut().poll(cx).is_pending());
        Poll::Ready(())
    })
    .await;
    third_token.cancel();
    assert!(matches!(
        third.await,
        Err(NativeWorkError::Worker(WorkerError::Cancelled))
    ));
    let shutdown = worker.shutdown();
    let release = async {
        tokio::time::sleep(Duration::from_millis(5)).await;
        assert!(dropped.lock().unwrap().is_none());
        release_tx.send(()).unwrap();
    };
    let (result, ()) = tokio::join!(shutdown, release);
    result.unwrap();
    assert!(cancelled.load(Ordering::SeqCst));
    assert!(dropped.lock().unwrap().is_some());
}

#[tokio::test]
async fn initialization_errors_and_panics_have_distinct_typed_outcomes() {
    let parent = CancellationToken::new();
    let error = NativeWorker::<()>::start(|_| Err("model missing"), &parent).await;
    assert!(matches!(
        error,
        Err(NativeWorkError::Operation("model missing"))
    ));
    let panic =
        NativeWorker::<()>::start(|_| -> Result<(), &'static str> { panic!("init") }, &parent)
            .await;
    assert!(matches!(
        panic,
        Err(NativeWorkError::Worker(WorkerError::Panicked))
    ));
    parent.cancel();
    let skipped = NativeWorker::<()>::start(
        |_| -> Result<(), &'static str> { panic!("must not start") },
        &parent,
    )
    .await;
    assert!(matches!(
        skipped,
        Err(NativeWorkError::Worker(WorkerError::Cancelled))
    ));
}

#[tokio::test]
async fn cancellation_during_initialization_waits_for_native_state_destruction() {
    let parent = CancellationToken::new();
    let dropped = Arc::new(Mutex::new(None));
    let observed = dropped.clone();
    let (started_tx, started_rx) = oneshot::channel();
    let (release_tx, release_rx) = mpsc::channel();
    let initialize = NativeWorker::start(
        move |token| {
            let _ = started_tx.send(());
            release_rx.recv_timeout(Duration::from_secs(2)).unwrap();
            assert!(token.is_cancelled());
            Ok::<_, &'static str>(LocalState {
                count: Rc::new(Cell::new(0)),
                owner: thread::current().id(),
                dropped: observed,
            })
        },
        &parent,
    );
    let cancel = async {
        started_rx.await.unwrap();
        parent.cancel();
        tokio::time::sleep(Duration::from_millis(5)).await;
        assert!(dropped.lock().unwrap().is_none());
        release_tx.send(()).unwrap();
    };
    let (result, ()) = tokio::join!(initialize, cancel);
    assert!(matches!(
        result,
        Err(NativeWorkError::Worker(WorkerError::Cancelled))
    ));
    assert!(dropped.lock().unwrap().is_some());
}

#[tokio::test]
async fn lifetime_cancellation_reaches_active_work_and_rejects_new_requests() {
    let lifetime = CancellationToken::new();
    let request = CancellationToken::new();
    let mut worker = NativeWorker::start(|_| Ok::<_, &'static str>(()), &lifetime)
        .await
        .unwrap();
    let (started_tx, started_rx) = oneshot::channel();
    let (release_tx, release_rx) = mpsc::channel();
    let work = worker.call(&request, move |_, token| {
        let _ = started_tx.send(());
        release_rx.recv_timeout(Duration::from_secs(2)).unwrap();
        Ok::<_, &'static str>(token.is_cancelled())
    });
    let stop = async {
        started_rx.await.unwrap();
        lifetime.cancel();
        tokio::time::sleep(Duration::from_millis(5)).await;
        release_tx.send(()).unwrap();
    };
    let (result, ()) = tokio::join!(work, stop);
    assert!(result.unwrap());
    assert!(!request.is_cancelled());
    let next = worker
        .call(&request, |_, _| -> Result<(), &'static str> {
            panic!("stopped worker must not accept requests")
        })
        .await;
    assert!(matches!(
        next,
        Err(NativeWorkError::Worker(WorkerError::Cancelled))
    ));
    worker.shutdown().await.unwrap();
}

#[tokio::test]
async fn a_panicked_job_poison_closes_the_worker_and_shutdown_reports_it() {
    let parent = CancellationToken::new();
    let mut worker = NativeWorker::start(|_| Ok::<_, &'static str>(()), &parent)
        .await
        .unwrap();
    let result = worker
        .call(&parent, |_, _| -> Result<(), &'static str> {
            panic!("native wrapper")
        })
        .await;
    assert!(matches!(
        result,
        Err(NativeWorkError::Worker(WorkerError::Panicked))
    ));
    let next = worker.call(&parent, |_, _| Ok::<_, &'static str>(())).await;
    assert!(matches!(
        next,
        Err(NativeWorkError::Worker(WorkerError::Panicked))
    ));
    assert!(matches!(
        worker.shutdown().await,
        Err(WorkerError::Panicked)
    ));
}

#[tokio::test]
async fn dropping_the_owner_joins_abandoned_work_instead_of_detaching_it() {
    let parent = CancellationToken::new();
    let finished = Arc::new(AtomicBool::new(false));
    let done = finished.clone();
    let mut worker = NativeWorker::start(|_| Ok::<_, &'static str>(()), &parent)
        .await
        .unwrap();
    let (started_tx, started_rx) = oneshot::channel();
    let (release_tx, release_rx) = mpsc::channel();
    let mut request = Box::pin(worker.call(&parent, move |_, _| {
        let _ = started_tx.send(());
        release_rx.recv_timeout(Duration::from_secs(2)).unwrap();
        done.store(true, Ordering::SeqCst);
        Ok::<_, &'static str>(())
    }));
    tokio::select! { _ = started_rx => {}, _ = &mut request => panic!("early completion") }
    drop(request);
    let release = thread::spawn(move || {
        release_tx.send(()).unwrap();
    });
    drop(worker);
    release.join().unwrap();
    assert!(finished.load(Ordering::SeqCst));
}
