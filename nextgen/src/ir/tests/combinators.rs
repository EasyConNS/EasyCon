use super::*;
use crate::ir::{Backoff, ExecutionLimits, ParMode, TraceContext};
use std::time::Duration;

fn sleep(id: &str, ms: i64) -> Node {
    call(id, "sleep", vec![named("duration_ms", Expr::int(ms))])
}
fn timeout(id: &str, ms: i64, body: Node) -> Node {
    Node::new(
        id,
        Flow::Timeout {
            duration_ms: Expr::int(ms),
            body: Box::new(body),
        },
    )
}
fn flow_ref(name: &str) -> Expr {
    Expr::FlowRef {
        definition: name.into(),
    }
}
fn subcall(id: &str, target: Expr, args: Vec<NamedExpr>) -> Node {
    Node::new(id, Flow::SubflowCall { target, args })
}
fn retry(id: &str, body: Node, max: i64, kinds: &[&str], backoff: Backoff) -> Node {
    Node::new(
        id,
        Flow::Retry {
            body: Box::new(body),
            max: Expr::int(max),
            retry_on: Expr::List {
                element: Type::Str,
                items: kinds.iter().map(|s| string(s)).collect(),
            },
            backoff,
        },
    )
}
fn par(mode: ParMode, branches: Vec<Node>) -> Node {
    Node::new("parallel", Flow::Par { mode, branches })
}
fn loop_until(id: &str, body: Node, cond: Expr, max: Option<i64>) -> Node {
    Node::new(
        id,
        Flow::LoopUntil {
            body: Box::new(body),
            cond,
            max_iters: max.map(Expr::int),
            state_init: None,
            state_next: None,
        },
    )
}
fn failure(result: &Execution) -> &Arc<FlowError> {
    match &result.outcome {
        FlowOutcome::Failure(e) | FlowOutcome::Cancelled(e) => e,
        other => panic!("expected failure, got {other:?}"),
    }
}
fn assert_pairs(result: &Execution) {
    let mut starts = BTreeMap::new();
    let mut ends = BTreeMap::new();
    for e in &result.trace {
        match &e.kind {
            TraceKind::NodeStarted => {
                assert!(
                    starts
                        .insert(e.invocation_id.unwrap(), e.node_id.clone())
                        .is_none()
                );
            }
            TraceKind::NodeSucceeded { .. }
            | TraceKind::NodeFailed { .. }
            | TraceKind::NodeCancelled { .. } => {
                assert!(
                    ends.insert(e.invocation_id.unwrap(), e.node_id.clone())
                        .is_none()
                );
            }
            _ => {}
        }
    }
    assert_eq!(starts, ends);
    assert!(
        result
            .trace
            .windows(2)
            .all(|w| w[1].event_seq == w[0].event_seq + 1)
    );
}
fn missing_capture(id: &str) -> Node {
    call(
        id,
        "capture",
        vec![
            named("source", source(SourceKind::Video, "missing")),
            named(
                "after",
                Expr::None {
                    element: Type::SampleId,
                },
            ),
        ],
    )
}

#[tokio::test]
async fn all_combinators_roundtrip_with_explicit_control_parameters() {
    let body = loop_until(
        "loop",
        retry(
            "retry",
            timeout(
                "timeout",
                100,
                par(ParMode::All, vec![sleep("one", 0), sleep("two", 0)]),
            ),
            2,
            &["Timeout"],
            Backoff::Exponential {
                base_ms: Expr::int(0),
                factor: float(2.0),
                max_ms: Expr::int(5),
            },
        ),
        Expr::boolean(true),
        Some(1),
    );
    let doc = document(body, Type::Tuple(vec![Type::Unit, Type::Unit]));
    let json = doc.to_json().unwrap();
    assert_eq!(doc, Document::from_json(&json).unwrap());
    let result = execute(doc).await;
    assert!(matches!(
        result.outcome,
        FlowOutcome::Success(Value::Tuple(_))
    ));
    assert_pairs(&result);
}
#[tokio::test]
async fn first_class_flow_refs_pass_return_and_call_without_captures() {
    let signature = FlowSignature {
        params: BTreeMap::from([("number".into(), Type::Int)]),
        output: Box::new(Type::Int),
        effects: Effects::default(),
    };
    let mut doc = document(
        seq(
            "main",
            vec![
                Node {
                    node_id: "get".into(),
                    result_bind: Some("f".into()),
                    flow: Flow::SubflowCall {
                        target: flow_ref("factory"),
                        args: vec![],
                    },
                },
                subcall(
                    "invoke",
                    Expr::var("f"),
                    vec![named("number", Expr::int(41))],
                ),
            ],
        ),
        Type::Int,
    );
    doc.definitions.insert(
        "factory".into(),
        Definition {
            params: BTreeMap::new(),
            output: Type::FlowRef(signature),
            body: eval("make", flow_ref("increment")),
        },
    );
    doc.definitions.insert(
        "increment".into(),
        Definition {
            params: BTreeMap::from([("number".into(), Type::Int)]),
            output: Type::Int,
            body: eval(
                "add",
                binary(BinaryOp::Add, Expr::var("number"), Expr::int(1)),
            ),
        },
    );
    let result = execute(doc).await;
    success_int(&result, 42);
    assert_pairs(&result);
    let mut leaking = document(subcall("call", flow_ref("bad"), vec![]), Type::Int);
    leaking.definitions.insert(
        "bad".into(),
        Definition {
            params: BTreeMap::new(),
            output: Type::Int,
            body: eval("read", Expr::var("caller_local")),
        },
    );
    assert!(leaking.validate().is_err());
}
#[tokio::test]
async fn foreign_refs_and_effect_bound_violations_reject_before_pad_enrollment() {
    let foreign = document(eval("foreign", Expr::int(1)), Type::Int)
        .validate()
        .unwrap();
    let mut doc = document(subcall("run", Expr::var("flow"), vec![]), Type::Int);
    doc.definitions.get_mut("main").unwrap().params.insert(
        "flow".into(),
        Type::FlowRef(FlowSignature {
            params: BTreeMap::new(),
            output: Box::new(Type::Int),
            effects: Effects::default(),
        }),
    );
    let checked = doc.validate().unwrap();
    let mut pad = Pad::new();
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    assert!(
        checked
            .execute(
                bindings,
                BTreeMap::from([(
                    "flow".into(),
                    Value::FlowRef(foreign.flow_ref("main").unwrap())
                )]),
                &CancellationToken::new()
            )
            .await
            .is_err()
    );
    assert!(records.lock().unwrap().is_empty());
    let mut doc = document(subcall("root", Expr::var("f"), vec![]), Type::Unit);
    doc.definitions.get_mut("main").unwrap().params.insert(
        "f".into(),
        Type::FlowRef(FlowSignature {
            params: BTreeMap::new(),
            output: Box::new(Type::Unit),
            effects: Effects::default(),
        }),
    );
    doc.definitions.insert(
        "effect".into(),
        Definition {
            params: BTreeMap::new(),
            output: Type::Unit,
            body: seq(
                "effect_body",
                vec![
                    missing_capture("capture"),
                    eval("unit", literal(Literal::Unit)),
                ],
            ),
        },
    );
    let checked = doc.validate().unwrap();
    assert!(
        checked
            .execute(
                RunBindings::default(),
                BTreeMap::from([(
                    "f".into(),
                    Value::FlowRef(checked.flow_ref("effect").unwrap())
                )]),
                &CancellationToken::new()
            )
            .await
            .is_err()
    );
}
#[test]
fn par_checks_indirect_effects_and_dynamic_reference_bounds() {
    let mut doc = document(
        par(
            ParMode::All,
            vec![subcall("invoke", flow_ref("middle"), vec![])],
        ),
        Type::Tuple(vec![Type::Frame]),
    );
    doc.definitions.insert(
        "middle".into(),
        Definition {
            params: BTreeMap::new(),
            output: Type::Frame,
            body: subcall("indirect", flow_ref("leaf"), vec![]),
        },
    );
    doc.definitions.insert(
        "leaf".into(),
        Definition {
            params: BTreeMap::new(),
            output: Type::Frame,
            body: missing_capture("hidden"),
        },
    );
    assert!(doc.validate().unwrap_err().message.contains("Pure/Meta"));
    let mut doc = document(
        par(
            ParMode::All,
            vec![subcall("dynamic", Expr::var("f"), vec![])],
        ),
        Type::Tuple(vec![Type::Unit]),
    );
    doc.definitions.get_mut("main").unwrap().params.insert(
        "f".into(),
        Type::FlowRef(FlowSignature {
            params: BTreeMap::new(),
            output: Box::new(Type::Unit),
            effects: Effects {
                effectful: true,
                meta: false,
            },
        }),
    );
    assert!(doc.validate().is_err());
}
#[tokio::test(start_paused = true)]
async fn par_all_overlaps_timers_and_returns_heterogeneous_ordered_tuple() {
    let start = tokio::time::Instant::now();
    let result = execute(document(
        par(
            ParMode::All,
            vec![
                seq(
                    "a",
                    vec![sleep("a_wait", 100), eval("a_value", Expr::int(1))],
                ),
                seq(
                    "b",
                    vec![sleep("b_wait", 200), eval("b_value", string("two"))],
                ),
            ],
        ),
        Type::Tuple(vec![Type::Int, Type::Str]),
    ))
    .await;
    assert_eq!(start.elapsed(), Duration::from_millis(200));
    assert!(
        matches!(&result.outcome,FlowOutcome::Success(Value::Tuple(v)) if matches!(v.as_slice(),[Value::Int(1),Value::Str(s)] if s=="two"))
    );
    assert_pairs(&result);
    let empty = execute(document(par(ParMode::All, vec![]), Type::Tuple(vec![]))).await;
    assert!(matches!(empty.outcome,FlowOutcome::Success(Value::Tuple(v)) if v.is_empty()));
    for mode in [ParMode::Any, ParMode::Race] {
        assert!(document(par(mode, vec![]), Type::Unit).validate().is_err());
    }
}
#[tokio::test(start_paused = true)]
async fn par_any_and_race_cancel_and_drain_loser_with_matching_trace_pairs() {
    for mode in [ParMode::Any, ParMode::Race] {
        let start = tokio::time::Instant::now();
        let result = execute(document(
            par(
                mode,
                vec![
                    seq(
                        "fast",
                        vec![sleep("fast_wait", 20), eval("win", Expr::int(1))],
                    ),
                    seq(
                        "slow",
                        vec![
                            sleep("slow_wait", 10_000),
                            eval("unreachable", Expr::int(2)),
                        ],
                    ),
                ],
            ),
            Type::Int,
        ))
        .await;
        success_int(&result, 1);
        assert_eq!(start.elapsed(), Duration::from_millis(20));
        assert_pairs(&result);
        assert!(
            result
                .trace
                .iter()
                .any(|e| e.node_id.as_deref() == Some("slow_wait")
                    && matches!(e.kind, TraceKind::NodeCancelled { .. }))
        );
        assert!(
            !result
                .trace
                .iter()
                .any(|e| e.node_id.as_deref() == Some("unreachable"))
        );
    }
}
#[tokio::test(start_paused = true)]
async fn par_all_uses_declaration_error_any_uses_last_accepted_and_race_first() {
    for (mode, expected, time) in [
        (ParMode::All, "slow_error", 100),
        (ParMode::Any, "slow_error", 100),
        (ParMode::Race, "fast_error", 10),
    ] {
        let branch = |prefix: &str, ms, id: &str| {
            seq(
                prefix,
                vec![
                    sleep(&format!("{prefix}_wait"), ms),
                    eval(id, function("unwrap", vec![Expr::var("none")])),
                ],
            )
        };
        let output = if mode == ParMode::All {
            Type::Tuple(vec![Type::Int, Type::Int])
        } else {
            Type::Int
        };
        let mut doc = document(
            par(
                mode,
                vec![
                    branch("slow", 100, "slow_error"),
                    branch("fast", 10, "fast_error"),
                ],
            ),
            output,
        );
        doc.definitions
            .get_mut("main")
            .unwrap()
            .params
            .insert("none".into(), Type::Option(Box::new(Type::Int)));
        let start = tokio::time::Instant::now();
        let result = doc
            .validate()
            .unwrap()
            .execute(
                RunBindings::default(),
                BTreeMap::from([(
                    "none".into(),
                    Value::Option {
                        element: Type::Int,
                        value: None,
                    },
                )]),
                &CancellationToken::new(),
            )
            .await
            .unwrap();
        assert_eq!(failure(&result).node_id.as_deref(), Some(expected));
        assert_eq!(start.elapsed(), Duration::from_millis(time));
        assert_eq!(failure(&result).suppressed.len(), 1);
        assert_pairs(&result);
    }
}
#[tokio::test]
async fn par_same_batch_prefers_declaration_index() {
    let result = execute(document(
        par(
            ParMode::Race,
            vec![eval("first", Expr::int(1)), eval("second", Expr::int(2))],
        ),
        Type::Int,
    ))
    .await;
    success_int(&result, 1);
    assert_pairs(&result);
}
#[tokio::test(start_paused = true)]
async fn timeout_cancels_sleep_zero_does_not_start_and_nested_deadlines_do_not_double_wrap() {
    let result = execute(document(
        timeout("outer", 50, timeout("inner", 500, sleep("wait", 10_000))),
        Type::Unit,
    ))
    .await;
    assert_eq!(failure(&result).kind, FlowErrorKind::Timeout);
    assert_eq!(failure(&result).node_id.as_deref(), Some("outer"));
    assert_eq!(
        failure(&result).cause.as_ref().unwrap().recovery(),
        Recovery::Cancelled
    );
    assert_pairs(&result);
    let result = execute(document(
        timeout("zero", 0, sleep("not_started", 10)),
        Type::Unit,
    ))
    .await;
    assert_eq!(failure(&result).kind, FlowErrorKind::Timeout);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("not_started"))
    );
}
#[tokio::test(start_paused = true)]
async fn parent_cancellation_wins_timeout_and_never_enters_recovery() {
    let timeout = timeout("time", 50, sleep("wait", 10_000));
    let fallback = Node::new(
        "fallback",
        Flow::Fallback {
            primary: Box::new(timeout),
            secondary: Box::new(sleep("must_skip", 0)),
        },
    );
    let doc = document(
        retry("retry", fallback, 3, &["Timeout"], Backoff::None),
        Type::Unit,
    )
    .validate()
    .unwrap();
    let parent = CancellationToken::new();
    let cancel = async {
        tokio::time::sleep(Duration::from_millis(50)).await;
        parent.cancel();
    };
    let (result, ()) = tokio::join!(
        doc.execute(RunBindings::default(), BTreeMap::new(), &parent),
        cancel
    );
    let result = result.unwrap();
    assert!(matches!(result.outcome, FlowOutcome::Cancelled(_)));
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("must_skip"))
    );
    assert_pairs(&result);
}
#[tokio::test]
async fn loop_state_last_allowed_iteration_and_bindings_are_scoped() {
    let mut body = loop_until(
        "loop",
        eval(
            "sum",
            binary(BinaryOp::Add, Expr::var("$state"), Expr::var("$iteration")),
        ),
        binary(BinaryOp::Ge, Expr::var("$result"), Expr::int(6)),
        Some(3),
    );
    if let Flow::LoopUntil {
        state_init,
        state_next,
        ..
    } = &mut body.flow
    {
        *state_init = Some(Expr::int(0));
        *state_next = Some(Expr::var("$result"));
    }
    let result = execute(document(body, Type::Int)).await;
    success_int(&result, 6);
    assert_pairs(&result);
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| e.node_id.as_deref() == Some("sum")
                && e.context.contains(&TraceContext::Iteration(3)))
            .count(),
        2
    );
    let result = execute(document(
        loop_until(
            "limit",
            eval("step", Expr::int(1)),
            Expr::boolean(false),
            Some(1),
        ),
        Type::Int,
    ))
    .await;
    assert_eq!(failure(&result).kind, FlowErrorKind::MaxIterationsExceeded);
    let leak = loop_until(
        "leak",
        Node::new(
            "bind",
            Flow::Bind {
                name: "secret".into(),
                expr: Expr::int(1),
                next: Box::new(eval("local", Expr::int(1))),
            },
        ),
        binary(BinaryOp::Eq, Expr::var("secret"), Expr::int(1)),
        Some(1),
    );
    assert!(document(leak, Type::Int).validate().is_err());
}
#[tokio::test]
async fn nested_loops_and_recursive_calls_share_uncatchable_budgets() {
    let inner = loop_until(
        "inner",
        eval("step", Expr::int(1)),
        Expr::boolean(true),
        None,
    );
    let outer = loop_until("outer", inner, Expr::boolean(false), None);
    let catch = Node::new(
        "catch",
        Flow::Catch {
            body: Box::new(outer),
            bind_error: None,
            handler: Box::new(eval("skip", Expr::int(99))),
        },
    );
    let doc = document(catch, Type::Int).validate().unwrap();
    let result = doc
        .execute_with_limits(
            RunBindings::default(),
            BTreeMap::new(),
            &CancellationToken::new(),
            ExecutionLimits {
                iterations: 3,
                call_depth: 3,
            },
        )
        .await
        .unwrap();
    assert_eq!(failure(&result).kind, FlowErrorKind::BudgetExceeded);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("skip"))
    );
    assert_pairs(&result);
    let doc = document(subcall("recurse", flow_ref("main"), vec![]), Type::Int)
        .validate()
        .unwrap();
    let result = doc
        .execute_with_limits(
            RunBindings::default(),
            BTreeMap::new(),
            &CancellationToken::new(),
            ExecutionLimits {
                iterations: 3,
                call_depth: 3,
            },
        )
        .await
        .unwrap();
    assert_eq!(failure(&result).kind, FlowErrorKind::BudgetExceeded);
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| matches!(e.kind, TraceKind::NodeStarted))
            .count(),
        4
    );
    assert_pairs(&result);
}
#[tokio::test]
async fn fallback_preserves_primary_error_and_catch_exposes_typed_error_chain() {
    let fallback = Node::new(
        "fallback",
        Flow::Fallback {
            primary: Box::new(missing_capture("primary")),
            secondary: Box::new(missing_capture("secondary")),
        },
    );
    let body = Node::new(
        "catch",
        Flow::Catch {
            body: Box::new(seq(
                "body",
                vec![fallback, eval("never", string("success"))],
            )),
            bind_error: Some("error".into()),
            handler: Box::new(eval("kind", field(Expr::var("error"), "kind"))),
        },
    );
    let result = execute(document(body, Type::Str)).await;
    assert!(
        matches!(result.outcome,FlowOutcome::Success(Value::Str(v)) if v=="CapabilityUnavailable")
    );
    let result = execute(document(
        Node::new(
            "fallback",
            Flow::Fallback {
                primary: Box::new(missing_capture("primary")),
                secondary: Box::new(missing_capture("secondary")),
            },
        ),
        Type::Frame,
    ))
    .await;
    assert_eq!(
        failure(&result).suppressed[0].node_id.as_deref(),
        Some("primary")
    );
    assert_pairs(&result);
}
#[tokio::test(start_paused = true)]
async fn retry_second_attempt_success_and_none_backoff_yields() {
    struct Flaky {
        calls: usize,
    }
    impl crate::primitives::VideoInput for Flaky {
        fn capture<'a>(
            &'a mut self,
            _: Option<&'a SampleId>,
            _: &'a CancellationToken,
        ) -> crate::primitives::CaptureFuture<'a, Arc<crate::video::VideoFrame>> {
            Box::pin(async move {
                self.calls += 1;
                if self.calls == 1 {
                    Err(crate::CaptureError::Stopped)
                } else {
                    Ok(Arc::new(crate::video::VideoFrame {
                        id: SampleId {
                            source: "fake".into(),
                            session: SessionId(1),
                            sequence: 1,
                        },
                        time: SampleTime {
                            received_ns: 0,
                            source_ns: None,
                        },
                        format: crate::video::VideoFormat {
                            width: 1,
                            height: 1,
                            frames_per_second: 30,
                            encoding: "RGB3".into(),
                        },
                        rgb: vec![0; 3].into(),
                    }))
                }
            })
        }
    }
    let doc = document(
        retry(
            "retry",
            missing_capture("capture"),
            2,
            &["SourceUnavailable"],
            Backoff::None,
        ),
        Type::Frame,
    )
    .validate()
    .unwrap();
    let mut bindings = RunBindings::default();
    bindings
        .videos
        .insert("missing".into(), Box::new(Flaky { calls: 0 }));
    let result = doc
        .execute(bindings, BTreeMap::new(), &CancellationToken::new())
        .await
        .unwrap();
    assert!(matches!(
        result.outcome,
        FlowOutcome::Success(Value::Frame(_))
    ));
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| matches!(e.kind, TraceKind::RetryScheduled { .. }))
            .count(),
        1
    );
    assert_pairs(&result);
}
#[tokio::test(start_paused = true)]
async fn retry_total_attempts_top_level_kinds_and_saturated_backoff() {
    for (max, kinds, expected, attempts) in [
        (
            1,
            vec!["CapabilityUnavailable"],
            FlowErrorKind::MaxRetriesExceeded,
            1,
        ),
        (4, vec!["Timeout"], FlowErrorKind::Primitive, 1),
        (
            4,
            vec!["CapabilityUnavailable"],
            FlowErrorKind::MaxRetriesExceeded,
            4,
        ),
    ] {
        let result = execute(document(
            retry(
                "retry",
                missing_capture("missing"),
                max,
                &kinds,
                Backoff::Exponential {
                    base_ms: Expr::int(2),
                    factor: float(f64::MAX),
                    max_ms: Expr::int(5),
                },
            ),
            Type::Frame,
        ))
        .await;
        assert_eq!(failure(&result).kind, expected);
        assert_eq!(
            result
                .trace
                .iter()
                .filter(|e| e.node_id.as_deref() == Some("missing")
                    && matches!(e.kind, TraceKind::NodeStarted))
                .count(),
            attempts
        );
        if attempts == 4 {
            assert_eq!(
                result
                    .trace
                    .iter()
                    .filter_map(|e| match e.kind {
                        TraceKind::RetryScheduled { delay_ms, .. } => Some(delay_ms),
                        _ => None,
                    })
                    .collect::<Vec<_>>(),
                vec![2, 5, 5]
            );
            assert_eq!(failure(&result).suppressed.len(), 3);
        }
        assert_pairs(&result);
    }
    assert!(
        document(
            retry("bad", sleep("body", 0), 0, &[], Backoff::None),
            Type::Unit
        )
        .validate()
        .is_err()
    );
    assert!(
        document(
            retry(
                "bad",
                sleep("body", 0),
                1,
                &["CleanupFailed"],
                Backoff::None
            ),
            Type::Unit
        )
        .validate()
        .is_err()
    );
}
#[tokio::test]
async fn recovery_cleans_pressed_state_before_secondary_and_cleanup_failure_bypasses_handler() {
    for fail_reset in [false, true] {
        let body = seq(
            "primary",
            vec![
                pad_action("press", "pad_press"),
                missing_capture("missing"),
                eval("unit", literal(Literal::Unit)),
            ],
        );
        let handler = seq(
            "handler",
            vec![
                pad_action("again", "pad_press"),
                eval("unit2", literal(Literal::Unit)),
            ],
        );
        let doc = document(
            Node::new(
                "catch",
                Flow::Catch {
                    body: Box::new(body),
                    bind_error: None,
                    handler: Box::new(handler),
                },
            ),
            Type::Unit,
        )
        .validate()
        .unwrap();
        let mut pad = Pad::new();
        pad.fail_reset = fail_reset;
        let records = pad.records.clone();
        let mut bindings = RunBindings::default();
        bindings.pads.insert("switch".into(), &mut pad);
        let result = doc
            .execute(bindings, BTreeMap::new(), &CancellationToken::new())
            .await
            .unwrap();
        assert_pairs(&result);
        if fail_reset {
            assert_eq!(failure(&result).kind, FlowErrorKind::CleanupFailed);
            assert!(
                !result
                    .trace
                    .iter()
                    .any(|e| e.node_id.as_deref() == Some("handler"))
            );
            assert!(result.child_cleanup[0].has_failures());
        } else {
            assert!(matches!(result.outcome, FlowOutcome::Success(Value::Unit)));
            assert_eq!(
                records
                    .lock()
                    .unwrap()
                    .iter()
                    .map(|r| r.buttons)
                    .collect::<Vec<_>>(),
                vec![Button::A.mask(), 0, Button::A.mask(), 0, 0]
            );
        }
    }
}
#[tokio::test(start_paused = true)]
async fn observe_probes_immediately_waits_only_after_none_and_propagates_errors() {
    let probe = eval(
        "probe",
        Expr::Some {
            value: Box::new(Expr::int(7)),
        },
    );
    let start = tokio::time::Instant::now();
    let result = execute(document(
        observe(
            "observe",
            probe,
            Type::Int,
            Expr::int(100),
            Some(Expr::int(500)),
        ),
        Type::Int,
    ))
    .await;
    success_int(&result, 7);
    assert_eq!(start.elapsed(), Duration::ZERO);
    assert_pairs(&result);
    let probe = eval("none_probe", Expr::None { element: Type::Int });
    let start = tokio::time::Instant::now();
    let result = execute(document(
        observe("none", probe, Type::Int, Expr::int(10), Some(Expr::int(25))),
        Type::Int,
    ))
    .await;
    assert_eq!(failure(&result).kind, FlowErrorKind::Timeout);
    assert_eq!(start.elapsed(), Duration::from_millis(25));
    assert_pairs(&result);
    let probe = seq(
        "probe_seq",
        vec![
            missing_capture("missing"),
            eval(
                "some",
                Expr::Some {
                    value: Box::new(Expr::int(1)),
                },
            ),
        ],
    );
    let result = execute(document(
        observe("error", probe, Type::Int, Expr::int(0), None),
        Type::Int,
    ))
    .await;
    assert_eq!(failure(&result).kind_name(), "CapabilityUnavailable");
    assert_pairs(&result);
}

#[tokio::test(start_paused = true)]
async fn timeout_drains_provider_that_ignores_cancellation_before_reporting_terminal() {
    struct Slow {
        finished: Arc<std::sync::atomic::AtomicBool>,
    }
    impl crate::primitives::VideoInput for Slow {
        fn capture<'a>(
            &'a mut self,
            _: Option<&'a SampleId>,
            token: &'a CancellationToken,
        ) -> crate::primitives::CaptureFuture<'a, Arc<crate::video::VideoFrame>> {
            Box::pin(async move {
                tokio::time::sleep(Duration::from_millis(100)).await;
                assert!(token.is_cancelled());
                self.finished
                    .store(true, std::sync::atomic::Ordering::SeqCst);
                Err(crate::CaptureError::Stopped)
            })
        }
    }
    let doc = document(timeout("timeout", 10, missing_capture("slow")), Type::Frame)
        .validate()
        .unwrap();
    let finished = Arc::new(std::sync::atomic::AtomicBool::new(false));
    let mut bindings = RunBindings::default();
    bindings.videos.insert(
        "missing".into(),
        Box::new(Slow {
            finished: finished.clone(),
        }),
    );
    let start = tokio::time::Instant::now();
    let result = doc
        .execute(bindings, BTreeMap::new(), &CancellationToken::new())
        .await
        .unwrap();
    assert_eq!(start.elapsed(), Duration::from_millis(100));
    assert!(finished.load(std::sync::atomic::Ordering::SeqCst));
    assert_eq!(failure(&result).kind, FlowErrorKind::Timeout);
    assert_pairs(&result);
    let cancelled = failure(&result).cause.as_ref().unwrap();
    assert_eq!(cancelled.recovery(), Recovery::Cancelled);
    assert!(cancelled.suppressed.iter().any(|e| e.primitive.is_some()));
}
#[tokio::test]
async fn blocking_provider_completion_after_deadline_is_timeout_even_before_timer_is_polled() {
    struct Blocking;
    impl crate::primitives::VideoInput for Blocking {
        fn capture<'a>(
            &'a mut self,
            _: Option<&'a SampleId>,
            _: &'a CancellationToken,
        ) -> crate::primitives::CaptureFuture<'a, Arc<crate::video::VideoFrame>> {
            Box::pin(async {
                std::thread::sleep(Duration::from_millis(15));
                Err(crate::CaptureError::Stopped)
            })
        }
    }
    let doc = document(
        timeout("timeout", 1, missing_capture("blocking")),
        Type::Frame,
    )
    .validate()
    .unwrap();
    let mut bindings = RunBindings::default();
    bindings.videos.insert("missing".into(), Box::new(Blocking));
    let result = doc
        .execute(bindings, BTreeMap::new(), &CancellationToken::new())
        .await
        .unwrap();
    assert_eq!(failure(&result).kind, FlowErrorKind::Timeout);
    assert_pairs(&result);
}
#[tokio::test]
async fn fatal_provider_failure_bypasses_catch_fallback_and_retry() {
    struct Fatal;
    impl crate::primitives::VideoInput for Fatal {
        fn capture<'a>(
            &'a mut self,
            _: Option<&'a SampleId>,
            _: &'a CancellationToken,
        ) -> crate::primitives::CaptureFuture<'a, Arc<crate::video::VideoFrame>> {
            Box::pin(async { Err(crate::CaptureError::Internal("fatal test provider")) })
        }
    }
    let fallback = Node::new(
        "fallback",
        Flow::Fallback {
            primary: Box::new(missing_capture("fatal")),
            secondary: Box::new(missing_capture("skip_secondary")),
        },
    );
    let catch = Node::new(
        "catch",
        Flow::Catch {
            body: Box::new(fallback),
            bind_error: None,
            handler: Box::new(missing_capture("skip_handler")),
        },
    );
    let doc = document(
        retry("retry", catch, 3, &["SourceUnavailable"], Backoff::None),
        Type::Frame,
    )
    .validate()
    .unwrap();
    let mut bindings = RunBindings::default();
    bindings.videos.insert("missing".into(), Box::new(Fatal));
    let result = doc
        .execute(bindings, BTreeMap::new(), &CancellationToken::new())
        .await
        .unwrap();
    assert_eq!(failure(&result).recovery(), Recovery::RunFatal);
    assert_eq!(failure(&result).node_id.as_deref(), Some("fatal"));
    assert_pairs(&result);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_ref().is_some_and(|n| n.starts_with("skip_")))
    );
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| e.node_id.as_deref() == Some("fatal")
                && matches!(e.kind, TraceKind::NodeStarted))
            .count(),
        1
    );
}
#[tokio::test(start_paused = true)]
async fn retry_backoff_and_parallel_budget_cancellation_cannot_be_recovered() {
    let doc = document(
        retry(
            "retry",
            missing_capture("capture"),
            3,
            &["CapabilityUnavailable"],
            Backoff::Fixed {
                ms: Expr::int(10_000),
            },
        ),
        Type::Frame,
    )
    .validate()
    .unwrap();
    let token = CancellationToken::new();
    let cancel = async {
        tokio::time::sleep(Duration::from_millis(50)).await;
        token.cancel();
    };
    let (result, ()) = tokio::join!(
        doc.execute(RunBindings::default(), BTreeMap::new(), &token),
        cancel
    );
    let result = result.unwrap();
    assert!(matches!(result.outcome, FlowOutcome::Cancelled(_)));
    assert_pairs(&result);
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| e.node_id.as_deref() == Some("capture")
                && matches!(e.kind, TraceKind::NodeStarted))
            .count(),
        1
    );
    let branches = vec![
        eval("candidate", Expr::int(42)),
        loop_until(
            "budget",
            eval("body", Expr::int(1)),
            Expr::boolean(false),
            None,
        ),
    ];
    let doc = document(par(ParMode::Any, branches), Type::Int)
        .validate()
        .unwrap();
    let result = doc
        .execute_with_limits(
            RunBindings::default(),
            BTreeMap::new(),
            &CancellationToken::new(),
            ExecutionLimits {
                iterations: 0,
                call_depth: 128,
            },
        )
        .await
        .unwrap();
    assert_eq!(failure(&result).kind, FlowErrorKind::BudgetExceeded);
    assert_pairs(&result);
}
#[tokio::test]
async fn recursive_effect_fixed_point_covers_mutual_calls_in_unselected_branches() {
    let main = subcall("entry", flow_ref("first"), vec![]);
    let mut doc = document(main, Type::Unit);
    doc.definitions.insert(
        "first".into(),
        Definition {
            params: BTreeMap::new(),
            output: Type::Unit,
            body: Node::new(
                "first_body",
                Flow::Choose {
                    cond: Expr::boolean(true),
                    then: Box::new(eval("finish", literal(Literal::Unit))),
                    else_: Some(Box::new(subcall("second_call", flow_ref("second"), vec![]))),
                },
            ),
        },
    );
    doc.definitions.insert(
        "second".into(),
        Definition {
            params: BTreeMap::new(),
            output: Type::Unit,
            body: seq(
                "second_body",
                vec![
                    missing_capture("capture"),
                    subcall("recur", flow_ref("first"), vec![]),
                ],
            ),
        },
    );
    let checked = doc.validate().unwrap();
    assert!(
        checked
            .flow_ref("first")
            .unwrap()
            .signature()
            .effects
            .effectful
    );
    assert!(checked.effects("entry").unwrap().effectful);
}
#[tokio::test]
async fn default_recursive_call_depth_stops_at_128_with_unique_invocations() {
    let doc = document(subcall("recursive", flow_ref("main"), vec![]), Type::Int)
        .validate()
        .unwrap();
    let result = doc
        .execute(
            RunBindings::default(),
            BTreeMap::new(),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    assert_eq!(failure(&result).kind, FlowErrorKind::BudgetExceeded);
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| matches!(e.kind, TraceKind::NodeStarted))
            .count(),
        129
    );
    assert_pairs(&result);
}

#[tokio::test(start_paused = true)]
async fn complete_json_example_returns_42_and_drains_slow_branch() {
    let doc = Document::from_json(include_str!("../../../examples/combinators.ir.json")).unwrap();
    let result = execute(doc).await;
    success_int(&result, 42);
    assert_pairs(&result);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("slow-result"))
    );
    assert!(result.trace.iter().any(|e| matches!(
        e.kind,
        TraceKind::ScopeCancellation {
            reason: CancelReason::SiblingCompleted
        }
    )));
}
#[tokio::test]
async fn observe_rejects_dynamic_negative_interval_before_first_probe() {
    let body = observe(
        "observe",
        eval(
            "must_skip",
            Expr::Some {
                value: Box::new(Expr::int(1)),
            },
        ),
        Type::Int,
        Expr::var("interval"),
        None,
    );
    let mut doc = document(body, Type::Int);
    doc.definitions
        .get_mut("main")
        .unwrap()
        .params
        .insert("interval".into(), Type::Int);
    let result = doc
        .validate()
        .unwrap()
        .execute(
            RunBindings::default(),
            BTreeMap::from([("interval".into(), Value::Int(-1))]),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    assert_eq!(failure(&result).recovery(), Recovery::Recoverable);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("must_skip"))
    );
    assert_pairs(&result);
}
