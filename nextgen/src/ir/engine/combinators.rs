use super::*;
use crate::ir::{Backoff, Expr, ParMode, control};
use std::{future::poll_fn, task::Poll, time::Duration};

impl<'run, 'providers: 'run> Engine<'run, 'providers> {
    pub(super) fn latch(&self, error: Arc<FlowError>) {
        let mut state = self.state.borrow_mut();
        if state.fatal.is_none() {
            state.fatal = Some(error);
        }
        self.root.cancel();
    }
    fn budget(&self, node: &Node, path: &str, message: &str, invocation: u64) -> Arc<FlowError> {
        let mut state = self.state.borrow_mut();
        let error = state
            .budget
            .get_or_insert_with(|| {
                let mut error =
                    FlowError::new(FlowErrorKind::BudgetExceeded, Some(node), path, message);
                error.invocation_id = Some(invocation);
                Arc::new(error)
            })
            .clone();
        self.root.cancel();
        error
    }
    fn owned(&self, node: &Node) -> Self {
        let mut child = self.clone();
        child.token = self.token.child_token();
        child.cancel_traced = Rc::new(Cell::new(false));
        child.cleanup_node = Some(node.node_id.clone());
        child.leases.push(Rc::new(RefCell::new(BTreeSet::new())));
        child
    }
    pub(super) async fn release(
        &self,
        node: &Node,
        path: &str,
        invocation: u64,
        parent: Option<u64>,
        result: NodeResult,
    ) -> NodeResult {
        let names = self.leases.last().expect("owned scope").borrow().clone();
        if names.is_empty() {
            return result;
        }
        let report = self.run.lock().await.release_pads(&names);
        self.event(
            Some((node, path)),
            Some(invocation),
            parent,
            TraceKind::CleanupCompleted {
                resources: report.entries.len(),
                failed: report.has_failures(),
            },
        );
        let failed = report.has_failures();
        let release_error = report.entries.iter().find_map(|entry| match &entry.state {
            crate::run::CleanupState::ReleaseUnconfirmed { error } => Some(error.clone()),
            _ => None,
        });
        self.state.borrow_mut().cleanup.push(report);
        if failed {
            let mut error = FlowError::new(
                FlowErrorKind::CleanupFailed,
                Some(node),
                path,
                "child pad state release unconfirmed; see child_cleanup",
            );
            match result {
                Ok(value) => error.candidate = Some(value),
                Err(cause) => error.cause = Some(cause),
            }
            error.invocation_id = Some(invocation);
            error.primitive = release_error;
            let error = Arc::new(error);
            self.latch(error.clone());
            Err(error)
        } else {
            result
        }
    }
    fn integer(
        &self,
        e: &Expr,
        minimum: i64,
        env: &[BTreeMap<String, Value>],
        node: &Node,
        path: &str,
    ) -> Result<u64, Arc<FlowError>> {
        match self.expr(e, env, node, path)? {
            Value::Int(v) if v >= minimum => Ok(v as u64),
            _ => Err(self.argument(node, path, format!("integer must be >= {minimum}"))),
        }
    }
    fn argument(&self, node: &Node, path: &str, message: impl Into<String>) -> Arc<FlowError> {
        let e = ExprError::new(super::super::ExprErrorKind::InvalidArgument, path, message);
        let mut error = FlowError::new(FlowErrorKind::Expression, Some(node), path, e.to_string());
        error.expression = Some(e);
        Arc::new(error)
    }
    async fn isolated(
        &self,
        node: &'run Node,
        env: &[BTreeMap<String, Value>],
        path: String,
        parent: u64,
    ) -> NodeResult {
        let child = self.owned(node);
        let mut env = env.to_vec();
        env.push(BTreeMap::new());
        child.node(node, &mut env, path, Some(parent)).await
    }
    async fn wait(&self, ms: u64, node: &Node, path: &str) -> Result<(), Arc<FlowError>> {
        if let Some(e) = self.cancelled(node, path) {
            return Err(e);
        }
        let result = crate::meta::sleep(ms as i64, &self.token).await;
        if let Some(error) = self.cancelled(node, path) {
            return Err(error);
        }
        result.map_err(|e| self.argument(node, path, e.to_string()))
    }

    pub(super) async fn control(
        &self,
        node: &'run Node,
        env: &[BTreeMap<String, Value>],
        path: &str,
        invocation: u64,
    ) -> NodeResult {
        match &node.flow {
            Flow::SubflowCall { target, args } => {
                let Value::FlowRef(target) =
                    self.expr(target, env, node, &format!("{path}.target"))?
                else {
                    return Err(self.argument(node, path, "target must be FlowRef"));
                };
                let mut params = BTreeMap::new();
                for arg in args {
                    params.insert(
                        arg.name.clone(),
                        self.expr(&arg.expr, env, node, &format!("{path}.args.{}", arg.name))?,
                    );
                }
                if self.depth >= self.state.borrow().limits.call_depth {
                    return Err(self.budget(
                        node,
                        path,
                        "subflow call depth exhausted",
                        invocation,
                    ));
                }
                let definition = &self.document.document().definitions[&target.definition];
                let mut child = self.owned(&definition.body);
                child.depth += 1;
                child
                    .node(
                        &definition.body,
                        &mut vec![params],
                        format!("definitions.{}.body", target.definition),
                        Some(invocation),
                    )
                    .await
            }
            Flow::LoopUntil {
                body,
                cond,
                max_iters,
                state_init,
                state_next,
            } => {
                let max = match max_iters {
                    Some(e) => Some(self.integer(e, 1, env, node, &format!("{path}.max_iters"))?),
                    None => None,
                };
                let mut state = match state_init {
                    Some(e) => self.expr(e, env, node, &format!("{path}.state_init"))?,
                    None => Value::Unit,
                };
                for iteration in 1u64.. {
                    if let Some(e) = self.cancelled(node, path) {
                        return Err(e);
                    }
                    if self.state.borrow().remaining == 0 {
                        return Err(self.budget(
                            node,
                            path,
                            "shared iteration budget exhausted",
                            invocation,
                        ));
                    }
                    self.state.borrow_mut().remaining -= 1;
                    let mut scope = env.to_vec();
                    scope.push(BTreeMap::from([
                        ("$state".into(), state.clone()),
                        ("$iteration".into(), Value::Int(iteration as i64)),
                    ]));
                    let mut child = self.owned(body);
                    child.context.push(TraceContext::Iteration(iteration));
                    let output = child
                        .node(body, &mut scope, format!("{path}.body"), Some(invocation))
                        .await?;
                    // Body bindings belong to its iteration, never to condition/state expressions.
                    let mut scope = env.to_vec();
                    scope.push(BTreeMap::from([
                        ("$state".into(), state.clone()),
                        ("$iteration".into(), Value::Int(iteration as i64)),
                        ("$result".into(), output.clone()),
                    ]));
                    if matches!(
                        self.expr(cond, &scope, node, &format!("{path}.cond"))?,
                        Value::Bool(true)
                    ) {
                        return Ok(output);
                    }
                    if max == Some(iteration) {
                        return Err(Arc::new(FlowError::new(
                            FlowErrorKind::MaxIterationsExceeded,
                            Some(node),
                            path,
                            "loop iteration limit reached",
                        )));
                    }
                    if let Some(next) = state_next {
                        state = self.expr(next, &scope, node, &format!("{path}.state_next"))?;
                    }
                    tokio::task::yield_now().await;
                }
                unreachable!("iteration budget is finite")
            }
            Flow::Retry {
                body,
                max,
                retry_on,
                backoff,
            } => {
                let max = self.integer(max, 1, env, node, &format!("{path}.max"))?;
                let retry_on = control::retry_kinds(&self.expr(
                    retry_on,
                    env,
                    node,
                    &format!("{path}.retry_on"),
                )?)
                .map_err(|e| self.argument(node, path, e))?;
                let (mut delay, factor, cap, mut exact) = match backoff {
                    Backoff::None => (0.0, 1.0, 0, Some(0)),
                    Backoff::Fixed { ms } => {
                        let ms = self.integer(ms, 0, env, node, &format!("{path}.backoff.ms"))?;
                        (ms as f64, 1.0, ms, Some(ms))
                    }
                    Backoff::Exponential {
                        base_ms,
                        factor,
                        max_ms,
                    } => {
                        let base = self.integer(
                            base_ms,
                            0,
                            env,
                            node,
                            &format!("{path}.backoff.base_ms"),
                        )?;
                        let cap =
                            self.integer(max_ms, 0, env, node, &format!("{path}.backoff.max_ms"))?;
                        let Value::Float(f) =
                            self.expr(factor, env, node, &format!("{path}.backoff.factor"))?
                        else {
                            return Err(self.argument(node, path, "factor must be Float"));
                        };
                        if base > cap || f.get() < 1.0 {
                            return Err(self.argument(node, path, "invalid exponential backoff"));
                        }
                        (base as f64, f.get(), cap, Some(base))
                    }
                };
                let mut failures = vec![];
                for attempt in 1..=max {
                    if let Some(e) = self.cancelled(node, path) {
                        return Err(e);
                    }
                    let mut child = self.owned(body);
                    child.context.push(TraceContext::Attempt(attempt));
                    let mut scope = env.to_vec();
                    scope.push(BTreeMap::new());
                    let error = match child
                        .node(body, &mut scope, format!("{path}.body"), Some(invocation))
                        .await
                    {
                        Ok(v) => return Ok(v),
                        Err(e) => e,
                    };
                    if error.recovery() != Recovery::Recoverable
                        || !retry_on.contains(&error.kind_name())
                    {
                        return Err(error);
                    }
                    if attempt == max {
                        let mut exhausted = FlowError::new(
                            FlowErrorKind::MaxRetriesExceeded,
                            Some(node),
                            path,
                            "total retry attempts exhausted",
                        );
                        exhausted.cause = Some(error);
                        exhausted.suppressed = failures;
                        return Err(Arc::new(exhausted));
                    }
                    failures.push(error);
                    let ms = exact.unwrap_or_else(|| (delay.floor() as u64).min(cap));
                    self.event(
                        Some((node, path)),
                        Some(invocation),
                        None,
                        TraceKind::RetryScheduled {
                            attempt: attempt + 1,
                            delay_ms: ms,
                        },
                    );
                    self.wait(ms, node, path).await?;
                    delay = (delay * factor).min(cap as f64);
                    if matches!(backoff, Backoff::Exponential { .. }) && factor != 1.0 {
                        exact = if delay >= cap as f64 { Some(cap) } else { None };
                    }
                }
                unreachable!("max is positive")
            }
            Flow::Fallback { primary, secondary } => {
                match self
                    .isolated(primary, env, format!("{path}.primary"), invocation)
                    .await
                {
                    Ok(v) => Ok(v),
                    Err(error) if error.recovery() == Recovery::Recoverable => {
                        if let Some(e) = self.cancelled(node, path) {
                            return Err(e);
                        }
                        self.isolated(secondary, env, format!("{path}.secondary"), invocation)
                            .await
                            .map_err(|e| FlowError::with_context(e, error))
                    }
                    Err(e) => Err(e),
                }
            }
            Flow::Catch {
                body,
                bind_error,
                handler,
            } => {
                match self
                    .isolated(body, env, format!("{path}.body"), invocation)
                    .await
                {
                    Ok(v) => Ok(v),
                    Err(error) if error.recovery() == Recovery::Recoverable => {
                        if let Some(e) = self.cancelled(node, path) {
                            return Err(e);
                        }
                        let mut scope = env.to_vec();
                        scope.push(BTreeMap::new());
                        if let Some(name) = bind_error {
                            scope
                                .last_mut()
                                .unwrap()
                                .insert(name.clone(), Value::ErrorRecord(error.clone()));
                        }
                        let child = self.owned(handler);
                        child
                            .node(
                                handler,
                                &mut scope,
                                format!("{path}.handler"),
                                Some(invocation),
                            )
                            .await
                            .map_err(|e| FlowError::with_context(e, error))
                    }
                    Err(e) => Err(e),
                }
            }
            Flow::Timeout { duration_ms, body } => {
                let ms = self.integer(duration_ms, 0, env, node, &format!("{path}.duration_ms"))?;
                if let Some(e) = self.cancelled(node, path) {
                    return Err(e);
                }
                if ms == 0 {
                    return Err(Arc::new(FlowError::new(
                        FlowErrorKind::Timeout,
                        Some(node),
                        path,
                        "zero duration timeout",
                    )));
                }
                let local = Instant::now()
                    .checked_add(Duration::from_millis(ms))
                    .ok_or_else(|| {
                        self.argument(node, path, "deadline exceeds monotonic clock range")
                    })?;
                let inherited = self.deadline.is_some_and(|d| d <= local);
                let deadline = self.deadline.map_or(local, |d| d.min(local));
                let mut child = self.owned(body);
                child.deadline = Some(deadline);
                child.cancel_reason = CancelReason::DeadlineExceeded;
                let token = child.token.clone();
                let mut scope = env.to_vec();
                scope.push(BTreeMap::new());
                let future = child.node(body, &mut scope, format!("{path}.body"), Some(invocation));
                tokio::pin!(future);
                let result = tokio::select! { biased;
                    _=self.token.cancelled()=>{ token.cancel(); future.await },
                    _=tokio::time::sleep_until(deadline)=>{ token.cancel(); future.await },
                    result=&mut future=>result,
                };
                if let Err(e) = &result
                    && e.recovery() == Recovery::RunFatal
                {
                    return result;
                }
                if let Some(e) = self.cancelled(node, path) {
                    return Err(match result {
                        Err(cause) => FlowError::with_context(e, cause),
                        Ok(value) => {
                            let mut e = (*e).clone();
                            e.candidate = Some(value);
                            Arc::new(e)
                        }
                    });
                }
                if Instant::now() >= deadline {
                    if inherited {
                        return result;
                    }
                    let mut error = FlowError::new(
                        FlowErrorKind::Timeout,
                        Some(node),
                        path,
                        "local deadline exceeded; child drained",
                    );
                    match result {
                        Ok(v) => error.candidate = Some(v),
                        Err(e) => error.cause = Some(e),
                    }
                    return Err(Arc::new(error));
                }
                result
            }
            Flow::Par { mode, branches } => {
                self.parallel(node, env, path, invocation, *mode, branches)
                    .await
            }
            _ => unreachable!("control dispatch"),
        }
    }
    async fn parallel(
        &self,
        node: &'run Node,
        env: &[BTreeMap<String, Value>],
        path: &str,
        invocation: u64,
        mode: ParMode,
        branches: &'run [Node],
    ) -> NodeResult {
        let tokens: Vec<_> = branches.iter().map(|_| self.token.child_token()).collect();
        let mut futures: Vec<_> = branches
            .iter()
            .enumerate()
            .map(|(i, branch)| {
                let mut child = self.owned(branch);
                child.token = tokens[i].clone();
                child.cancel_reason = CancelReason::SiblingCompleted;
                child.context.push(TraceContext::Branch(i));
                let mut scope = env.to_vec();
                scope.push(BTreeMap::new());
                let path = format!("{path}.branches[{i}]");
                Some(Box::pin(async move {
                    child.node(branch, &mut scope, path, Some(invocation)).await
                })
                    as Pin<Box<dyn Future<Output = NodeResult> + '_>>)
            })
            .collect();
        let mut results: Vec<Option<NodeResult>> = vec![None; branches.len()];
        let mut remaining = branches.len();
        let mut candidate: Option<NodeResult> = None;
        let mut accepted = vec![];
        while remaining > 0 {
            let batch = poll_fn(|cx| {
                let mut ready = vec![];
                for (i, slot) in futures.iter_mut().enumerate() {
                    if let Some(f) = slot
                        && let Poll::Ready(result) = f.as_mut().poll(cx)
                    {
                        ready.push((i, result));
                        *slot = None;
                    }
                }
                if ready.is_empty() {
                    Poll::Pending
                } else {
                    Poll::Ready(ready)
                }
            })
            .await;
            for (i, result) in batch {
                remaining -= 1;
                self.event(
                    Some((node, path)),
                    Some(invocation),
                    None,
                    TraceKind::BranchAccepted {
                        branch_index: i,
                        success: result.is_ok(),
                    },
                );
                let fatal = result
                    .as_ref()
                    .err()
                    .is_some_and(|e| e.recovery() == Recovery::RunFatal);
                if fatal
                    || (candidate.is_none()
                        && (mode == ParMode::Race || (mode == ParMode::Any && result.is_ok())))
                {
                    candidate = Some(result.clone());
                    for token in &tokens {
                        token.cancel();
                    }
                }
                accepted.push(i);
                results[i] = Some(result);
            }
        }
        let failures: Vec<_> = results
            .iter()
            .filter_map(|r| r.as_ref().unwrap().as_ref().err().cloned())
            .collect();
        if let Some(primary) = self.cancelled(node, path) {
            let mut error = (*primary).clone();
            error.suppressed.extend(failures.into_iter().filter(|e| {
                !Arc::ptr_eq(e, &primary)
                    && (e.invocation_id != primary.invocation_id || e.kind != primary.kind)
            }));
            if let Some(Ok(value)) = candidate {
                error.candidate = Some(value);
            }
            return Err(Arc::new(error));
        }
        if let Some(candidate) = candidate {
            return candidate.map_err(|e| {
                let mut error = (*e).clone();
                error
                    .suppressed
                    .extend(failures.into_iter().filter(|f| !Arc::ptr_eq(f, &e)));
                Arc::new(error)
            });
        }
        if !failures.is_empty() {
            let primary = if mode == ParMode::Any {
                results[*accepted.last().unwrap()]
                    .as_ref()
                    .unwrap()
                    .as_ref()
                    .unwrap_err()
                    .clone()
            } else {
                failures[0].clone()
            };
            let mut error = (*primary).clone();
            error
                .suppressed
                .extend(failures.into_iter().filter(|e| !Arc::ptr_eq(e, &primary)));
            return Err(Arc::new(error));
        }
        Ok(Value::Tuple(
            results.into_iter().map(|r| r.unwrap().unwrap()).collect(),
        ))
    }
}
