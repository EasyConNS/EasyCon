//! Owned native worker: thread-local state, one queued job, and explicit joined shutdown.

use std::{
    panic::{AssertUnwindSafe, catch_unwind},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
        mpsc::{self, SyncSender, TrySendError},
    },
    thread::{self, JoinHandle},
    time::Duration,
};

use thiserror::Error;
use tokio::sync::{Notify, oneshot};
use tokio_util::sync::CancellationToken;

#[cfg(test)]
mod tests;

#[derive(Debug, Error)]
pub enum WorkerError {
    #[error("native work cancelled")]
    Cancelled,
    #[error("native worker stopped")]
    Stopped,
    #[error("native worker panicked")]
    Panicked,
    #[error("cannot create native worker: {0}")]
    Spawn(#[source] std::io::Error),
}

#[derive(Debug, Error)]
pub enum NativeWorkError<E> {
    #[error(transparent)]
    Worker(#[from] WorkerError),
    #[error("native operation: {0}")]
    Operation(E),
}

type Job<T> = Box<dyn FnOnce(&mut T) -> bool + Send>;

struct CancelOnDrop(CancellationToken);
impl Drop for CancelOnDrop {
    fn drop(&mut self) {
        self.0.cancel();
    }
}

struct Exited {
    closed: CancellationToken,
    space: Arc<Notify>,
    finished: Option<oneshot::Sender<()>>,
}
impl Drop for Exited {
    fn drop(&mut self) {
        self.closed.cancel();
        self.space.notify_waiters();
        // The receiver may have been dropped during synchronous fallback shutdown.
        if let Some(finished) = self.finished.take() {
            let _ = finished.send(());
        }
    }
}

/// T is constructed, used and dropped on one dedicated thread; T need not be Send.
/// Only owned job inputs and outputs cross threads. Dropping a request cancels it;
/// dropping this owner closes the queue and joins, possibly blocking the dropping thread.
#[must_use = "call shutdown().await to destroy native state and join the worker"]
pub struct NativeWorker<T: 'static> {
    sender: Option<SyncSender<Job<T>>>,
    space: Arc<Notify>,
    lifetime: CancellationToken,
    closed: CancellationToken,
    panicked: Arc<AtomicBool>,
    finished: Option<oneshot::Receiver<()>>,
    thread: Option<JoinHandle<()>>,
}

impl<T: 'static> NativeWorker<T> {
    /// The supplied token bounds this worker's lifetime, independently of per-job tokens.
    /// Initialization is native work too: cancellation waits for the factory to return.
    pub async fn start<F, E>(
        factory: F,
        parent: &CancellationToken,
    ) -> Result<Self, NativeWorkError<E>>
    where
        F: FnOnce(&CancellationToken) -> Result<T, E> + Send + 'static,
        E: Send + 'static,
    {
        if parent.is_cancelled() {
            return Err(WorkerError::Cancelled.into());
        }
        let (sender, receiver) = mpsc::sync_channel::<Job<T>>(1);
        let (ready_tx, ready_rx) = oneshot::channel();
        let (finished_tx, finished_rx) = oneshot::channel();
        let space = Arc::new(Notify::new());
        let lifetime = parent.child_token();
        let closed = CancellationToken::new();
        let panicked = Arc::new(AtomicBool::new(false));
        let (worker_space, worker_lifetime, worker_panicked) =
            (space.clone(), lifetime.clone(), panicked.clone());
        let exited = Exited {
            closed: closed.clone(),
            space: space.clone(),
            finished: Some(finished_tx),
        };
        let thread = thread::Builder::new()
            .name("easycon-native".into())
            .spawn(move || {
                let _exited = exited;
                let outcome = catch_unwind(AssertUnwindSafe(move || {
                    if worker_lifetime.is_cancelled() {
                        let _ = ready_tx.send(Err(WorkerError::Cancelled.into()));
                        return;
                    }
                    let mut state = match factory(&worker_lifetime) {
                        Ok(state) => state,
                        Err(error) => {
                            let _ = ready_tx.send(Err(NativeWorkError::Operation(error)));
                            return;
                        }
                    };
                    if worker_lifetime.is_cancelled() {
                        let _ = ready_tx.send(Err(WorkerError::Cancelled.into()));
                        return;
                    }
                    if ready_tx.send(Ok(())).is_err() {
                        return;
                    }
                    while let Ok(job) = receiver.recv() {
                        worker_space.notify_one();
                        if worker_lifetime.is_cancelled() || !job(&mut state) {
                            break;
                        }
                    }
                    // State and queued closures are dropped before Exited is signalled.
                }));
                if outcome.is_err() {
                    worker_panicked.store(true, Ordering::Release);
                }
            })
            .map_err(WorkerError::Spawn)?;
        let worker = Self {
            sender: Some(sender),
            space,
            lifetime,
            closed,
            panicked,
            finished: Some(finished_rx),
            thread: Some(thread),
        };
        let result = match ready_rx.await {
            Ok(result) => result,
            Err(_) => {
                worker.closed.cancelled().await;
                Err(worker.failure().into())
            }
        };
        if let Err(error) = result {
            worker.shutdown().await?;
            return Err(error);
        }
        // A cancellation just after the ready signal must also dispose of initialized state.
        if worker.lifetime.is_cancelled() {
            worker.shutdown().await?;
            return Err(WorkerError::Cancelled.into());
        }
        Ok(worker)
    }

    fn failure(&self) -> WorkerError {
        if self.panicked.load(Ordering::Acquire) {
            WorkerError::Panicked
        } else if self.lifetime.is_cancelled() {
            WorkerError::Cancelled
        } else {
            WorkerError::Stopped
        }
    }

    pub fn is_cancelled(&self) -> bool {
        self.lifetime.is_cancelled()
    }

    fn job<F, R, E>(
        &self,
        token: &CancellationToken,
        operation: F,
        reply: impl FnOnce(Result<R, NativeWorkError<E>>) + Send + 'static,
    ) -> Job<T>
    where
        F: FnOnce(&mut T, &CancellationToken) -> Result<R, E> + Send + 'static,
        R: Send + 'static,
        E: Send + 'static,
    {
        let token = token.clone();
        let panicked = self.panicked.clone();
        Box::new(move |state| {
            if token.is_cancelled() {
                reply(Err(WorkerError::Cancelled.into()));
                return true;
            }
            match catch_unwind(AssertUnwindSafe(|| operation(state, &token))) {
                Ok(result) => {
                    reply(result.map_err(NativeWorkError::Operation));
                    true
                }
                Err(_) => {
                    panicked.store(true, Ordering::Release);
                    reply(Err(WorkerError::Panicked.into()));
                    false
                }
            }
        })
    }

    /// Cancellation after admission signals the job and waits for its actual response.
    /// An abandoned future cancels its job; the worker owner still holds and joins the thread.
    pub async fn call<F, R, E>(
        &mut self,
        parent: &CancellationToken,
        operation: F,
    ) -> Result<R, NativeWorkError<E>>
    where
        F: FnOnce(&mut T, &CancellationToken) -> Result<R, E> + Send + 'static,
        R: Send + 'static,
        E: Send + 'static,
    {
        let token = parent.child_token();
        let _cancel = CancelOnDrop(token.clone());
        let (reply, mut result) = oneshot::channel();
        let mut job = self.job(&token, operation, move |value| {
            let _ = reply.send(value);
        });
        loop {
            let available = self.space.notified();
            tokio::pin!(available);
            available.as_mut().enable();
            if token.is_cancelled() || self.lifetime.is_cancelled() {
                return Err(WorkerError::Cancelled.into());
            }
            if self.closed.is_cancelled() {
                return Err(self.failure().into());
            }
            match self.sender.as_ref().expect("live owner").try_send(job) {
                Ok(()) => break,
                Err(TrySendError::Disconnected(_)) => {
                    self.closed.cancelled().await;
                    return Err(self.failure().into());
                }
                Err(TrySendError::Full(returned)) => job = returned,
            }
            tokio::select! {
                _ = &mut available => {},
                _ = token.cancelled() => return Err(WorkerError::Cancelled.into()),
                _ = self.lifetime.cancelled() => return Err(WorkerError::Cancelled.into()),
                _ = self.closed.cancelled() => return Err(self.failure().into()),
            }
        }
        let response = tokio::select! {
            biased;
            value = &mut result => value,
            _ = token.cancelled() => { token.cancel(); result.await },
            _ = self.lifetime.cancelled() => { token.cancel(); result.await },
        };
        match response {
            Ok(value) => value,
            Err(_) => {
                self.closed.cancelled().await;
                Err(self.failure().into())
            }
        }
    }

    /// Synchronous API for hosts that intentionally block. Never used by the IR adapter.
    pub fn call_blocking<F, R, E>(
        &mut self,
        parent: &CancellationToken,
        operation: F,
    ) -> Result<R, NativeWorkError<E>>
    where
        F: FnOnce(&mut T, &CancellationToken) -> Result<R, E> + Send + 'static,
        R: Send + 'static,
        E: Send + 'static,
    {
        if parent.is_cancelled() || self.lifetime.is_cancelled() {
            return Err(WorkerError::Cancelled.into());
        }
        let token = parent.child_token();
        let _cancel = CancelOnDrop(token.clone());
        let (reply, result) = mpsc::channel();
        let job = self.job(&token, operation, move |value| {
            let _ = reply.send(value);
        });
        if self.sender.as_ref().expect("live owner").send(job).is_err() {
            self.join();
            return Err(self.failure().into());
        }
        loop {
            if parent.is_cancelled() || self.lifetime.is_cancelled() {
                token.cancel();
            }
            match result.recv_timeout(Duration::from_millis(10)) {
                Ok(value) => return value,
                Err(mpsc::RecvTimeoutError::Timeout) => {}
                Err(mpsc::RecvTimeoutError::Disconnected) => {
                    self.join();
                    return Err(self.failure().into());
                }
            }
        }
    }

    fn join(&mut self) {
        if let Some(thread) = self.thread.take()
            && thread.join().is_err()
        {
            self.panicked.store(true, Ordering::Release);
        }
    }

    /// Closes admission, signals cancellation, waits for state destruction, then joins.
    pub async fn shutdown(mut self) -> Result<(), WorkerError> {
        self.lifetime.cancel();
        self.sender.take();
        if let Some(finished) = self.finished.take() {
            let _ = finished.await;
        }
        self.join();
        if self.panicked.load(Ordering::Acquire) {
            Err(WorkerError::Panicked)
        } else {
            Ok(())
        }
    }
}

impl<T: 'static> Drop for NativeWorker<T> {
    fn drop(&mut self) {
        self.lifetime.cancel();
        self.sender.take();
        self.join();
    }
}
