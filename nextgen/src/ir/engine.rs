use super::{
    ExprError, Flow, Node, RunControl, RunStopReason, Type, ValidatedDocument, ValidationError,
    Value,
    primitive::{self, PrimitiveInvokeError},
};
use crate::{
    primitives::{PrimitiveError, PrimitiveId, Recovery},
    run::{CleanupReport, RunBindings, RunFailure, RunOutcome, RunScope},
};
use std::{
    cell::{Cell, RefCell},
    collections::{BTreeMap, BTreeSet},
    future::Future,
    pin::Pin,
    rc::Rc,
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering},
    },
};
use thiserror::Error;
use tokio::time::Instant;
use tokio_util::sync::CancellationToken;

mod combinators;

static NEXT_RUN: AtomicU64 = AtomicU64::new(1);
type NodeResult = Result<Value, Arc<FlowError>>;
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FlowErrorKind {
    Expression,
    InvalidArgument,
    Primitive,
    Cancelled,
    CleanupFailed,
    Internal,
    Timeout,
    MaxRetriesExceeded,
    MaxIterationsExceeded,
    BudgetExceeded,
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CancelReason {
    UserStop,
    Shutdown,
    Aborted,
    DeadlineExceeded,
    SiblingCompleted,
    BudgetExceeded,
    ProviderCancelled,
}
impl From<RunStopReason> for CancelReason {
    fn from(reason: RunStopReason) -> Self {
        match reason {
            RunStopReason::UserStop => Self::UserStop,
            RunStopReason::Shutdown => Self::Shutdown,
            RunStopReason::Aborted => Self::Aborted,
        }
    }
}
#[derive(Clone, Debug, Error)]
#[error("{kind:?} at {node_path}: {message}")]
pub struct FlowError {
    pub kind: FlowErrorKind,
    pub node_id: Option<String>,
    pub node_path: String,
    pub message: String,
    pub expression: Option<ExprError>,
    pub primitive: Option<Arc<PrimitiveError>>,
    pub primitive_call: Option<(String, u32)>,
    /// A delivered/computed candidate observed at a cancellation checkpoint.
    pub candidate: Option<Value>,
    pub invocation_id: Option<u64>,
    pub cancel_reason: Option<CancelReason>,
    pub cause: Option<Arc<FlowError>>,
    pub suppressed: Vec<Arc<FlowError>>,
}
impl FlowError {
    pub fn recovery(&self) -> Recovery {
        match self.kind {
            FlowErrorKind::Cancelled | FlowErrorKind::BudgetExceeded => Recovery::Cancelled,
            FlowErrorKind::CleanupFailed | FlowErrorKind::Internal => Recovery::RunFatal,
            FlowErrorKind::Primitive => self
                .primitive
                .as_ref()
                .map_or(Recovery::RunFatal, |e| e.recovery()),
            FlowErrorKind::Expression
            | FlowErrorKind::InvalidArgument
            | FlowErrorKind::Timeout
            | FlowErrorKind::MaxRetriesExceeded
            | FlowErrorKind::MaxIterationsExceeded => Recovery::Recoverable,
        }
    }
    fn new(
        kind: FlowErrorKind,
        node: Option<&Node>,
        path: &str,
        message: impl Into<String>,
    ) -> Self {
        Self {
            kind,
            node_id: node.map(|n| n.node_id.clone()),
            node_path: path.into(),
            message: message.into(),
            expression: None,
            primitive: None,
            primitive_call: None,
            candidate: None,
            invocation_id: None,
            cancel_reason: match kind {
                FlowErrorKind::Cancelled => Some(CancelReason::UserStop),
                FlowErrorKind::BudgetExceeded => Some(CancelReason::BudgetExceeded),
                _ => None,
            },
            cause: None,
            suppressed: vec![],
        }
    }
}
#[derive(Debug)]
pub enum FlowOutcome {
    Success(Value),
    Failure(Arc<FlowError>),
    Cancelled(Arc<FlowError>),
}
/// Trace retains error metadata, never a candidate's image/audio payload.
#[derive(Debug)]
pub struct TraceError {
    pub kind: FlowErrorKind,
    pub origin_node_id: Option<String>,
    pub message: String,
    pub expression: Option<ExprError>,
    pub primitive: Option<Arc<PrimitiveError>>,
    pub primitive_call: Option<(String, u32)>,
    pub candidate_type: Option<Type>,
    pub invocation_id: Option<u64>,
    pub origin: &'static str,
    pub cancel_reason: Option<CancelReason>,
    pub cause: Option<Box<TraceError>>,
    pub suppressed: Vec<TraceError>,
}
impl From<&FlowError> for TraceError {
    fn from(error: &FlowError) -> Self {
        Self {
            kind: error.kind,
            origin_node_id: error.node_id.clone(),
            message: error.message.clone(),
            expression: error.expression.clone(),
            primitive: error.primitive.clone(),
            primitive_call: error.primitive_call.clone(),
            candidate_type: error.candidate.as_ref().map(Value::ty),
            invocation_id: error.invocation_id,
            origin: error.origin(),
            cancel_reason: error.cancel_reason,
            cause: error.cause.as_deref().map(|e| Box::new(Self::from(e))),
            suppressed: error.suppressed.iter().map(|e| Self::from(&**e)).collect(),
        }
    }
}
#[derive(Debug)]
pub enum TraceKind {
    ScopeCancellation { reason: CancelReason },
    RetryScheduled { attempt: u64, delay_ms: u64 },
    BranchAccepted { branch_index: usize, success: bool },
    Limits { iterations: u64, call_depth: usize },
    RunStarted,
    NodeStarted,
    NodeSucceeded { output: Type },
    NodeFailed { error: TraceError },
    NodeCancelled { error: TraceError },
    UserLog { message: String },
    CancellationRequested { reason: CancelReason },
    CleanupCompleted { resources: usize, failed: bool },
    RunFinished { kind: &'static str },
}
#[derive(Debug)]
pub struct TraceEvent {
    pub run_id: u64,
    pub event_seq: u64,
    pub elapsed_ms: u64,
    pub node_id: Option<String>,
    pub node_path: Option<String>,
    pub invocation_id: Option<u64>,
    pub parent_invocation_id: Option<u64>,
    pub kind: TraceKind,
    pub context: Vec<TraceContext>,
}
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum TraceContext {
    Branch(usize),
    Attempt(u64),
    Iteration(u64),
}
#[derive(Clone, Copy, Debug)]
pub struct ExecutionLimits {
    pub iterations: u64,
    pub call_depth: usize,
}
impl Default for ExecutionLimits {
    fn default() -> Self {
        Self {
            iterations: 1_000_000,
            call_depth: 128,
        }
    }
}
#[derive(Debug)]
#[must_use = "inspect outcome and cleanup before reporting completion"]
pub struct Execution {
    pub run_id: u64,
    pub outcome: FlowOutcome,
    pub cleanup: CleanupReport,
    pub child_cleanup: Vec<CleanupReport>,
    pub prior: Vec<FlowOutcome>,
    pub trace: Vec<TraceEvent>,
}
impl ValidatedDocument {
    /// Preflight runtime values before opening physical resources.
    pub fn validate_params(&self, params: &BTreeMap<String, Value>) -> Result<(), ValidationError> {
        let definition = &self.document().definitions[&self.document().entry];
        if params.len() != definition.params.len()
            || params.iter().any(|(name, v)| {
                !v.valid()
                    || !self.owns(v)
                    || !definition
                        .params
                        .get(name)
                        .is_some_and(|t| t.accepts(&v.ty()))
            })
        {
            return Err(ValidationError::new(
                "params",
                "runtime arguments must exactly match entry signature and contain valid typed values",
            ));
        }
        Ok(())
    }
    /// Parameter checks precede resource enrollment. This local future creates no tasks.
    /// Hosts must run blocking primitive providers on a worker they own and join.
    pub async fn execute(
        &self,
        bindings: RunBindings<'_>,
        params: BTreeMap<String, Value>,
        parent: &CancellationToken,
    ) -> Result<Execution, ValidationError> {
        self.execute_with_limits(bindings, params, parent, ExecutionLimits::default())
            .await
    }
    pub async fn execute_with_limits(
        &self,
        bindings: RunBindings<'_>,
        params: BTreeMap<String, Value>,
        parent: &CancellationToken,
        limits: ExecutionLimits,
    ) -> Result<Execution, ValidationError> {
        self.execute_inner(bindings, params, parent, None, limits)
            .await
    }

    /// Preserve the host's stop reason through nested combinators and run cleanup.
    pub async fn execute_controlled(
        &self,
        bindings: RunBindings<'_>,
        params: BTreeMap<String, Value>,
        control: &RunControl,
    ) -> Result<Execution, ValidationError> {
        self.execute_controlled_with_limits(bindings, params, control, ExecutionLimits::default())
            .await
    }

    pub async fn execute_controlled_with_limits(
        &self,
        bindings: RunBindings<'_>,
        params: BTreeMap<String, Value>,
        control: &RunControl,
        limits: ExecutionLimits,
    ) -> Result<Execution, ValidationError> {
        self.execute_inner(
            bindings,
            params,
            control.token(),
            Some(control.clone()),
            limits,
        )
        .await
    }

    async fn execute_inner(
        &self,
        bindings: RunBindings<'_>,
        params: BTreeMap<String, Value>,
        parent: &CancellationToken,
        control: Option<RunControl>,
        limits: ExecutionLimits,
    ) -> Result<Execution, ValidationError> {
        let limits = ExecutionLimits {
            iterations: limits.iterations.min(1_000_000),
            call_depth: limits.call_depth.min(128),
        };
        self.validate_params(&params)?;
        let definition = &self.document().definitions[&self.document().entry];
        let run_id = NEXT_RUN.fetch_add(1, Ordering::Relaxed);
        let run = tokio::sync::Mutex::new(RunScope::new(bindings, parent));
        let root = parent.child_token();
        let state = Rc::new(RefCell::new(State {
            trace: vec![],
            next_invocation: 1,
            parents: BTreeMap::new(),
            cancellation_traced: false,
            remaining: limits.iterations,
            limits,
            fatal: None,
            budget: None,
            cleanup: vec![],
        }));
        let queue = Rc::new(RefCell::new(Vec::new()));
        let scheduler = QueueGuard(queue.clone());
        let engine = Engine {
            queue: queue.clone(),
            run: &run,
            document: self,
            run_id,
            start: Instant::now(),
            state: state.clone(),
            token: root.clone(),
            root,
            external: parent.clone(),
            control: control.clone(),
            deadline: None,
            depth: 0,
            context: vec![],
            leases: vec![],
            cleanup_node: None,
            cancel_reason: CancelReason::ProviderCancelled,
            cancel_traced: Rc::new(Cell::new(false)),
        };
        engine.event(None, None, None, TraceKind::RunStarted);
        engine.event(
            None,
            None,
            None,
            TraceKind::Limits {
                iterations: limits.iterations,
                call_depth: limits.call_depth,
            },
        );
        let mut env = vec![params];
        let mut result = {
            let entry = engine.node(
                &definition.body,
                &mut env,
                format!("definitions.{}.body", self.document().entry),
                None,
            );
            tokio::pin!(entry);
            std::future::poll_fn(|cx| {
                if let std::task::Poll::Ready(result) = entry.as_mut().poll(cx) {
                    return std::task::Poll::Ready(result);
                }
                // Never poll a child through its parent's stack or hold the queue borrow while polling.
                let mut jobs = std::mem::take(&mut *queue.borrow_mut());
                for mut job in jobs.drain(..) {
                    if job.as_mut().poll(cx).is_pending() {
                        queue.borrow_mut().push(job);
                    }
                }
                std::task::Poll::Pending
            })
            .await
        };
        assert!(
            queue.borrow().is_empty(),
            "entry finished before child jobs drained"
        );
        drop(scheduler);
        if !result
            .as_ref()
            .err()
            .is_some_and(|e| e.recovery() == Recovery::RunFatal)
            && let Some(error) = state.borrow().fatal.clone()
        {
            result = Err(error);
        }
        if !result.as_ref().err().is_some_and(|e| {
            e.recovery() == Recovery::RunFatal || e.kind == FlowErrorKind::BudgetExceeded
        }) && let Some(error) = state.borrow().budget.clone()
        {
            result = Err(error);
        }
        let origin = result.as_ref().err().cloned();
        let start = engine.start;
        drop(engine);
        drop(queue);
        let mut state = Rc::try_unwrap(state)
            .ok()
            .expect("all child futures drained")
            .into_inner();
        let mut trace = std::mem::take(&mut state.trace);
        let run = run.into_inner();
        let candidate = RunOutcome::Success(result);
        let completion = run.finish(candidate);
        let reason = control
            .as_ref()
            .and_then(RunControl::reason)
            .map(CancelReason::from)
            .unwrap_or(CancelReason::UserStop);
        let external_reason = parent.is_cancelled().then_some(reason);
        let mut outcome = map_outcome(completion.outcome, origin.as_ref(), external_reason);
        if !completion.cleanup.has_failures()
            && let Some(error) = origin
                .as_ref()
                .filter(|e| e.recovery() == Recovery::RunFatal)
        {
            outcome = FlowOutcome::Failure(error.clone());
        }
        let mut prior = completion
            .prior
            .into_iter()
            .map(|o| map_outcome(o, origin.as_ref(), external_reason))
            .collect::<Vec<_>>();
        if let FlowOutcome::Failure(error) | FlowOutcome::Cancelled(error) = &mut outcome {
            if error.kind == FlowErrorKind::CleanupFailed {
                let failure = Arc::make_mut(error);
                failure.primitive =
                    completion
                        .cleanup
                        .entries
                        .iter()
                        .find_map(|entry| match &entry.state {
                            crate::run::CleanupState::ReleaseUnconfirmed { error } => {
                                Some(error.clone())
                            }
                            _ => None,
                        });
                failure.cause = origin.clone();
            }
            if error.candidate.is_none()
                && let Some(value) = prior.iter().find_map(|prior| match prior {
                    FlowOutcome::Success(v) => Some(v),
                    _ => None,
                })
            {
                Arc::make_mut(error).candidate = Some(value.clone());
            }
        }
        // A latched primitive failure may already be the unchanged node error, not a second cause.
        if let FlowOutcome::Failure(error) | FlowOutcome::Cancelled(error) = &outcome {
            prior.retain(|p| !matches!(p,FlowOutcome::Failure(e) | FlowOutcome::Cancelled(e) if Arc::ptr_eq(e,error)));
        }
        let elapsed_ms = || u64::try_from(start.elapsed().as_millis()).unwrap_or(u64::MAX);
        let requested_reason = external_reason.unwrap_or_else(|| match &outcome {
            FlowOutcome::Cancelled(error) => error.cancel_reason.unwrap_or(reason),
            _ => reason,
        });
        if (parent.is_cancelled() || matches!(outcome, FlowOutcome::Cancelled(_)))
            && !trace
                .iter()
                .any(|e| matches!(e.kind, TraceKind::CancellationRequested { reason } if reason == requested_reason))
        {
            trace.push(TraceEvent {
                run_id,
                event_seq: trace.len() as u64 + 1,
                elapsed_ms: elapsed_ms(),
                node_id: None,
                node_path: None,
                invocation_id: None,
                parent_invocation_id: None,
                context: vec![],
                kind: TraceKind::CancellationRequested { reason: requested_reason },
            });
        }
        trace.push(TraceEvent {
            run_id,
            event_seq: trace.len() as u64 + 1,
            elapsed_ms: elapsed_ms(),
            node_id: None,
            node_path: None,
            invocation_id: None,
            parent_invocation_id: None,
            context: vec![],
            kind: TraceKind::CleanupCompleted {
                resources: completion.cleanup.entries.len(),
                failed: completion.cleanup.has_failures(),
            },
        });
        // The entry owns run cleanup: publish its single terminal event only after arbitration.
        if let Some(root) = trace.iter().find(|event| {
            matches!(event.kind, TraceKind::NodeStarted) && event.parent_invocation_id.is_none()
        }) {
            let terminal = TraceEvent {
                run_id,
                event_seq: trace.len() as u64 + 1,
                elapsed_ms: elapsed_ms(),
                node_id: root.node_id.clone(),
                node_path: root.node_path.clone(),
                invocation_id: root.invocation_id,
                parent_invocation_id: None,
                context: vec![],
                kind: match &outcome {
                    FlowOutcome::Success(value) => TraceKind::NodeSucceeded { output: value.ty() },
                    FlowOutcome::Failure(error) => TraceKind::NodeFailed {
                        error: TraceError::from(&**error),
                    },
                    FlowOutcome::Cancelled(error) => TraceKind::NodeCancelled {
                        error: TraceError::from(&**error),
                    },
                },
            };
            trace.push(terminal);
        }
        trace.push(TraceEvent {
            run_id,
            event_seq: trace.len() as u64 + 1,
            elapsed_ms: elapsed_ms(),
            node_id: None,
            node_path: None,
            invocation_id: None,
            parent_invocation_id: None,
            context: vec![],
            kind: TraceKind::RunFinished {
                kind: match &outcome {
                    FlowOutcome::Success(_) => "success",
                    FlowOutcome::Failure(_) => "failure",
                    FlowOutcome::Cancelled(_) => "cancelled",
                },
            },
        });
        Ok(Execution {
            run_id,
            outcome,
            cleanup: completion.cleanup,
            child_cleanup: state.cleanup,
            prior,
            trace,
        })
    }
}
fn map_outcome(
    outcome: RunOutcome<NodeResult>,
    origin: Option<&Arc<FlowError>>,
    external_reason: Option<CancelReason>,
) -> FlowOutcome {
    match outcome {
        RunOutcome::Success(Ok(value)) => FlowOutcome::Success(value),
        RunOutcome::Success(Err(error)) => {
            if error.recovery() == Recovery::Cancelled {
                FlowOutcome::Cancelled(error)
            } else {
                FlowOutcome::Failure(error)
            }
        }
        RunOutcome::Cancelled => FlowOutcome::Cancelled(
            origin
                .filter(|e| {
                    e.recovery() == Recovery::Cancelled
                        && external_reason.is_none_or(|r| e.cancel_reason == Some(r))
                })
                .cloned()
                .unwrap_or_else(|| {
                    let mut error = FlowError::new(
                        FlowErrorKind::Cancelled,
                        None,
                        "run",
                        "run cancelled before terminal publication",
                    );
                    error.cancel_reason = Some(external_reason.unwrap_or(CancelReason::UserStop));
                    error.cause = origin.cloned();
                    Arc::new(error)
                }),
        ),
        RunOutcome::Failure(RunFailure::Primitive(error)) => {
            if let Some(origin) =
                origin.filter(|o| o.primitive.as_ref().is_some_and(|p| Arc::ptr_eq(p, &error)))
            {
                return FlowOutcome::Failure(origin.clone());
            }
            let mut failure =
                FlowError::new(FlowErrorKind::Primitive, None, "run", error.to_string());
            failure.primitive = Some(error);
            FlowOutcome::Failure(Arc::new(failure))
        }
        RunOutcome::Failure(RunFailure::CleanupFailed) => {
            FlowOutcome::Failure(Arc::new(FlowError::new(
                FlowErrorKind::CleanupFailed,
                None,
                "run",
                "resource release is unconfirmed; see cleanup report",
            )))
        }
    }
}
struct State {
    trace: Vec<TraceEvent>,
    next_invocation: u64,
    parents: BTreeMap<u64, Option<u64>>,
    cancellation_traced: bool,
    remaining: u64,
    limits: ExecutionLimits,
    fatal: Option<Arc<FlowError>>,
    budget: Option<Arc<FlowError>>,
    cleanup: Vec<CleanupReport>,
}
type Job<'a> = Pin<Box<dyn Future<Output = ()> + 'a>>;
struct QueueGuard<'a>(Rc<RefCell<Vec<Job<'a>>>>);
impl Drop for QueueGuard<'_> {
    fn drop(&mut self) {
        self.0.borrow_mut().clear();
    }
}
#[derive(Clone)]
struct Engine<'run, 'providers> {
    run: &'run tokio::sync::Mutex<RunScope<'providers>>,
    queue: Rc<RefCell<Vec<Job<'run>>>>,
    document: &'run ValidatedDocument,
    run_id: u64,
    start: Instant,
    state: Rc<RefCell<State>>,
    token: CancellationToken,
    root: CancellationToken,
    external: CancellationToken,
    control: Option<RunControl>,
    deadline: Option<Instant>,
    depth: usize,
    context: Vec<TraceContext>,
    leases: Vec<Rc<RefCell<BTreeSet<String>>>>,
    cleanup_node: Option<String>,
    cancel_reason: CancelReason,
    cancel_traced: Rc<Cell<bool>>,
}
impl<'run, 'providers: 'run> Engine<'run, 'providers> {
    fn event(
        &self,
        node: Option<(&Node, &str)>,
        invocation: Option<u64>,
        parent: Option<u64>,
        kind: TraceKind,
    ) {
        let mut state = self.state.borrow_mut();
        let parent =
            parent.or_else(|| invocation.and_then(|id| state.parents.get(&id).copied().flatten()));
        if let Some(id) = invocation {
            match kind {
                TraceKind::NodeStarted => {
                    state.parents.insert(id, parent);
                }
                TraceKind::NodeSucceeded { .. }
                | TraceKind::NodeFailed { .. }
                | TraceKind::NodeCancelled { .. } => {
                    state.parents.remove(&id);
                }
                _ => {}
            }
        }
        let event_seq = state.trace.len() as u64 + 1;
        state.trace.push(TraceEvent {
            run_id: self.run_id,
            event_seq,
            elapsed_ms: u64::try_from(self.start.elapsed().as_millis()).unwrap_or(u64::MAX),
            node_id: node.map(|(n, _)| n.node_id.clone()),
            node_path: node.map(|(_, p)| p.into()),
            invocation_id: invocation,
            parent_invocation_id: parent,
            kind,
            context: self.context.clone(),
        });
    }
    fn cancelled(&self, node: &Node, path: &str) -> Option<Arc<FlowError>> {
        if let Some(error) = self
            .state
            .borrow()
            .fatal
            .clone()
            .or_else(|| self.state.borrow().budget.clone())
        {
            return Some(error);
        }
        if !self.token.is_cancelled() && self.deadline.is_none_or(|d| Instant::now() < d) {
            return None;
        }
        let reason = if self.external.is_cancelled() {
            self.control
                .as_ref()
                .and_then(RunControl::reason)
                .map(CancelReason::from)
                .unwrap_or(CancelReason::UserStop)
        } else if self.root.is_cancelled() {
            CancelReason::ProviderCancelled
        } else if self.deadline.is_some_and(|d| Instant::now() >= d) {
            CancelReason::DeadlineExceeded
        } else {
            self.cancel_reason
        };
        if self.root.is_cancelled() && !self.state.borrow().cancellation_traced {
            self.event(
                None,
                None,
                None,
                TraceKind::CancellationRequested { reason },
            );
            self.state.borrow_mut().cancellation_traced = true;
        }
        if !self.cancel_traced.replace(true) {
            self.event(
                Some((node, path)),
                None,
                None,
                TraceKind::ScopeCancellation { reason },
            );
        }
        let mut error = FlowError::new(
            FlowErrorKind::Cancelled,
            Some(node),
            path,
            format!("scope cancelled: {reason:?}"),
        );
        error.cancel_reason = Some(reason);
        Some(Arc::new(error))
    }

    fn expr(
        &self,
        expr: &super::Expr,
        env: &[BTreeMap<String, Value>],
        node: &Node,
        path: &str,
    ) -> NodeResult {
        expr.eval_with(env, path, &self.document.refs)
            .map_err(|error| {
                let mut failure = FlowError::new(
                    FlowErrorKind::Expression,
                    Some(node),
                    path,
                    error.to_string(),
                );
                failure.expression = Some(error);
                Arc::new(failure)
            })
    }
    fn node<'a>(
        &'a self,
        node: &'run Node,
        env: &'a mut Vec<BTreeMap<String, Value>>,
        path: String,
        parent: Option<u64>,
    ) -> Pin<Box<dyn Future<Output = NodeResult> + 'a>> {
        Box::pin(async move {
            let engine = self.clone();
            let mut scope = std::mem::take(env);
            let (send, receive) = tokio::sync::oneshot::channel();
            self.queue.borrow_mut().push(Box::pin(async move {
                let result = engine.node_inline(node, &mut scope, path, parent).await;
                let _ = send.send((result, scope));
            }));
            // Enqueuing is synchronous, so the driver must be polled once to start this job.
            std::future::poll_fn(|cx| {
                cx.waker().wake_by_ref();
                std::task::Poll::Ready(())
            })
            .await;
            let (result, scope) = receive
                .await
                .expect("scheduler owns every begun node until completion");
            *env = scope;
            result
        })
    }
    fn node_inline<'a>(
        &'a self,
        node: &'run Node,
        env: &'a mut Vec<BTreeMap<String, Value>>,
        path: String,
        parent: Option<u64>,
    ) -> Pin<Box<dyn Future<Output = NodeResult> + 'a>> {
        Box::pin(async move {
            if let Some(error) = self.cancelled(node, &path) {
                return Err(error);
            }
            let invocation = {
                let mut state = self.state.borrow_mut();
                let id = state.next_invocation;
                state.next_invocation += 1;
                id
            };
            self.event(
                Some((node, &path)),
                Some(invocation),
                parent,
                TraceKind::NodeStarted,
            );
            // A cooperative yield also lets external cancellation stop long, purely sequential IR.
            tokio::task::yield_now().await;
            let mut result = if let Some(error) = self.cancelled(node, &path) {
                Err(error)
            } else {
                self.body(node, env, &path, invocation, parent).await
            };
            if self.cleanup_node.as_deref() == Some(node.node_id.as_str()) {
                result = self.release(node, &path, invocation, parent, result).await;
            }
            // Fatal cleanup wins; otherwise cancellation arbitrates both success and failure.
            if !result
                .as_ref()
                .err()
                .is_some_and(|e| e.recovery() == Recovery::RunFatal)
                && let Some(cancelled) = self.cancelled(node, &path)
            {
                result = Err(match result {
                    Ok(value) => {
                        let mut e = (*cancelled).clone();
                        e.candidate = Some(value);
                        Arc::new(e)
                    }
                    Err(error)
                        if Arc::ptr_eq(&error, &cancelled)
                            || (error.recovery() == Recovery::Cancelled
                                && error.cancel_reason == cancelled.cancel_reason) =>
                    {
                        error
                    }
                    Err(error) => FlowError::with_context(cancelled, error),
                });
            }
            if let Err(error) = &mut result {
                if error.invocation_id.is_none() {
                    Arc::make_mut(error).invocation_id = Some(invocation);
                }
                if error.recovery() == Recovery::RunFatal {
                    self.latch(error.clone());
                }
            } else if let Ok(value) = &result
                && let Some(name) = &node.result_bind
            {
                env.last_mut()
                    .expect("runtime scope")
                    .insert(name.clone(), value.clone());
            }
            let kind = match &result {
                Ok(value) => TraceKind::NodeSucceeded { output: value.ty() },
                Err(error) if error.recovery() == Recovery::Cancelled => TraceKind::NodeCancelled {
                    error: TraceError::from(&**error),
                },
                Err(error) => TraceKind::NodeFailed {
                    error: TraceError::from(&**error),
                },
            };
            if parent.is_some() {
                self.event(Some((node, &path)), Some(invocation), parent, kind);
            }
            result
        })
    }
    async fn body(
        &self,
        node: &'run Node,
        env: &mut Vec<BTreeMap<String, Value>>,
        path: &str,
        invocation: u64,
        parent: Option<u64>,
    ) -> NodeResult {
        match &node.flow {
            Flow::Eval { expr } => self.expr(expr, env, node, &format!("{path}.expr")),
            Flow::Primitive {
                name,
                version,
                args,
            } => {
                let mut values = BTreeMap::new();
                for (i, arg) in args.iter().enumerate() {
                    let value =
                        self.expr(&arg.expr, env, node, &format!("{path}.args[{i}].expr"))?;
                    values.insert(arg.name.clone(), value);
                }
                if let Some(error) = self.cancelled(node, path) {
                    return Err(error);
                }
                let id = PrimitiveId::lookup(name, *version).expect("validated primitive");
                for lease in &self.leases {
                    if id.contract().capability == Some(crate::primitives::Capability::Gamepad)
                        && let Some(Value::Source(target)) = values.get("target")
                    {
                        lease.borrow_mut().insert(target.binding.clone());
                    }
                }
                // Sleep has no provider state; never hold the provider mutex across a timer.
                let invoked = if id == PrimitiveId::Sleep {
                    match values.get("duration_ms") {
                        Some(Value::Int(ms)) => crate::meta::sleep(*ms, &self.token)
                            .await
                            .map(|()| crate::primitives::PrimitiveOutput::Unit)
                            .map_err(|e| PrimitiveInvokeError::Primitive(Arc::new(e.into()))),
                        _ => unreachable!("validated sleep"),
                    }
                } else {
                    tokio::select! { biased;
                        _=self.token.cancelled()=>Err(PrimitiveInvokeError::Primitive(Arc::new(PrimitiveError::Cancelled))),
                        mut run=self.run.lock()=>primitive::execute(id,&values,&mut run,&self.token).await,
                    }
                };
                let result = invoked.map_err(|error| {
                    let (kind, message, expr, primitive) = match error {
                        PrimitiveInvokeError::Expr(e) => (
                            if e.kind == super::ExprErrorKind::InvalidArgument {
                                FlowErrorKind::InvalidArgument
                            } else {
                                FlowErrorKind::Expression
                            },
                            e.to_string(),
                            Some(e),
                            None,
                        ),
                        PrimitiveInvokeError::Primitive(e) => (
                            if e.recovery() == Recovery::Cancelled {
                                FlowErrorKind::Cancelled
                            } else {
                                FlowErrorKind::Primitive
                            },
                            e.to_string(),
                            None,
                            Some(e),
                        ),
                    };
                    let mut failure = FlowError::new(kind, Some(node), path, message);
                    failure.expression = expr;
                    if kind == FlowErrorKind::Cancelled {
                        failure.cancel_reason = Some(CancelReason::ProviderCancelled);
                    }
                    failure.primitive = primitive;
                    failure.primitive_call = Some((name.clone(), *version));
                    Arc::new(failure)
                })?;
                if id == PrimitiveId::Log {
                    self.event(
                        Some((node, path)),
                        Some(invocation),
                        parent,
                        TraceKind::UserLog {
                            message: match values.get("message") {
                                Some(Value::Str(v)) => v.clone(),
                                _ => String::new(),
                            },
                        },
                    );
                }
                primitive::output(result).map_err(|e| {
                    let failure =
                        FlowError::new(FlowErrorKind::Internal, Some(node), path, e.to_string());
                    Arc::new(failure)
                })
            }
            Flow::Seq { steps } => {
                env.push(BTreeMap::new());
                let result = async {
                    let mut output = Value::Unit;
                    for (i, step) in steps.iter().enumerate() {
                        output = self
                            .node(step, env, format!("{path}.steps[{i}]"), Some(invocation))
                            .await?;
                    }
                    Ok(output)
                }
                .await;
                env.pop();
                result
            }
            Flow::Bind { name, expr, next } => {
                let value = self.expr(expr, env, node, &format!("{path}.expr"))?;
                env.last_mut()
                    .expect("runtime scope")
                    .insert(name.clone(), value);
                self.node(next, env, format!("{path}.next"), Some(invocation))
                    .await
            }
            Flow::Choose { cond, then, else_ } => {
                let cond = self.expr(cond, env, node, &format!("{path}.cond"))?;
                let selected = if matches!(cond, Value::Bool(true)) {
                    Some((&**then, "then"))
                } else {
                    else_.as_deref().map(|n| (n, "else"))
                };
                if let Some((child, suffix)) = selected {
                    env.push(BTreeMap::new());
                    let result = self
                        .node(child, env, format!("{path}.{suffix}"), Some(invocation))
                        .await;
                    env.pop();
                    result
                } else {
                    Ok(Value::Unit)
                }
            }
            Flow::Par { .. }
            | Flow::Retry { .. }
            | Flow::Timeout { .. }
            | Flow::LoopUntil { .. }
            | Flow::Fallback { .. }
            | Flow::Catch { .. }
            | Flow::SubflowCall { .. } => self.control(node, env, path, invocation).await,
            Flow::Pipe { source, sink } => {
                let value = self
                    .node(source, env, format!("{path}.source"), Some(invocation))
                    .await?;
                env.push(BTreeMap::from([("$prev".into(), value)]));
                let result = self
                    .node(sink, env, format!("{path}.sink"), Some(invocation))
                    .await;
                env.pop();
                result
            }
        }
    }
}
