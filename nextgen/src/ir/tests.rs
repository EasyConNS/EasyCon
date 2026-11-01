use super::*;
use crate::{
    SampleId, SampleTime, SessionId,
    audio::AudioWindow,
    primitives::{PrimitiveId, Recovery},
    run::{CleanupState, RunBindings},
    vision::Image,
};
use easycon_device::{Button, Delivery, DeviceError, Gamepad, Report, SessionInfo, WriteReceipt};
use std::{
    collections::BTreeMap,
    sync::{Arc, Mutex},
    time::Instant,
};
use tokio_util::sync::CancellationToken;

fn eval(id: &str, expr: Expr) -> Node {
    Node::new(id, Flow::Eval { expr })
}
fn seq(id: &str, steps: Vec<Node>) -> Node {
    Node::new(id, Flow::Seq { steps })
}
fn named(name: &str, expr: Expr) -> NamedExpr {
    NamedExpr {
        name: name.into(),
        expr,
    }
}
fn call(id: &str, name: &str, args: Vec<NamedExpr>) -> Node {
    Node::new(
        id,
        Flow::Primitive {
            name: name.into(),
            version: 1,
            args,
        },
    )
}
fn binary(op: BinaryOp, left: Expr, right: Expr) -> Expr {
    Expr::Binary {
        op,
        left: Box::new(left),
        right: Box::new(right),
    }
}
fn field(base: Expr, name: &str) -> Expr {
    Expr::Field {
        base: Box::new(base),
        name: name.into(),
    }
}
fn function(name: &str, args: Vec<Expr>) -> Expr {
    Expr::Call {
        name: name.into(),
        version: 1,
        args,
    }
}
fn literal(value: Literal) -> Expr {
    Expr::Literal { value }
}
fn string(value: &str) -> Expr {
    literal(Literal::Str(value.into()))
}
fn float(value: f64) -> Expr {
    literal(Literal::Float(Finite::new(value).unwrap()))
}
fn source(kind: SourceKind, binding: &str) -> Expr {
    literal(Literal::Source(SourceRef {
        kind,
        binding: binding.into(),
    }))
}
fn symbol(kind: SymbolKind, name: &str) -> Expr {
    literal(Literal::Symbol(Symbol::new(kind, name).unwrap()))
}
fn document(body: Node, output: Type) -> Document {
    Document {
        ir_version: IR_VERSION.into(),
        entry: "main".into(),
        definitions: BTreeMap::from([(
            "main".into(),
            Definition {
                params: BTreeMap::new(),
                output,
                body,
            },
        )]),
    }
}
async fn execute(document: Document) -> Execution {
    document
        .validate()
        .unwrap()
        .execute(
            RunBindings::default(),
            BTreeMap::new(),
            &CancellationToken::new(),
        )
        .await
        .unwrap()
}
fn success_int(execution: &Execution, value: i64) {
    assert!(
        matches!(execution.outcome,FlowOutcome::Success(Value::Int(v)) if v==value),
        "{:?}",
        execution.outcome
    );
}

#[test]
fn json_preserves_integer_extremes_empty_values_and_normalizes_float_zero() {
    let expr = Expr::Tuple {
        items: vec![
            Expr::int(i64::MIN),
            Expr::int(i64::MAX),
            literal(Literal::Unit),
            Expr::None { element: Type::Int },
            Expr::List {
                element: Type::Int,
                items: vec![],
            },
            Expr::Tuple { items: vec![] },
            float(-0.0),
        ],
    };
    let output = expr.evaluate(&BTreeMap::new()).unwrap().ty();
    let doc = document(eval("values", expr), output);
    let json = doc.to_json().unwrap();
    assert!(json.contains("\"9223372036854775807\""));
    assert!(json.contains("\"-9223372036854775808\""));
    assert!(!json.contains("-0.0"));
    assert_eq!(Document::from_json(&json).unwrap(), doc);
    doc.validate().unwrap();
    assert!(Finite::new(f64::INFINITY).is_none());
    assert!(Finite::new(f64::NAN).is_none());
    assert!(serde_json::from_str::<Literal>(r#"{"type":"int","value":9007199254740993}"#).is_err());
    assert!(serde_json::from_str::<Literal>(r#"{"type":"int","value":"01"}"#).is_err());
}
#[test]
fn json_rejects_unknown_fields_nodes_symbols_and_duplicate_signatures() {
    let json = document(eval("value", Expr::int(1)), Type::Int)
        .to_json()
        .unwrap();
    assert!(
        Document::from_json(&json.replacen("\"ir_version\"", "\"extra\": 0, \"ir_version\"", 1))
            .is_err()
    );
    assert!(Document::from_json(&json.replace("\"eval\"", "\"timeout\"")).is_err());
    assert!(
        Document::from_json(&json.replace(
            "\"params\": {}",
            "\"params\": {\"x\": {\"type\": \"int\"}, \"x\": {\"type\": \"bool\"}}"
        ))
        .is_err()
    );
    assert!(serde_json::from_str::<Symbol>(r#"{"kind":"hat","name":"A"}"#).is_err());
    assert!(
        serde_json::from_str::<Type>(
            r#"{"type":"record","of":{"x":{"type":"int"},"x":{"type":"bool"}}}"#
        )
        .is_err()
    );
}
#[test]
fn checked_numeric_rules_and_explicit_conversions_cover_boundaries() {
    let vars = BTreeMap::new();
    for expr in [
        binary(BinaryOp::Add, Expr::int(i64::MAX), Expr::int(1)),
        binary(BinaryOp::Div, Expr::int(i64::MIN), Expr::int(-1)),
        binary(BinaryOp::Rem, Expr::int(i64::MIN), Expr::int(-1)),
        binary(BinaryOp::Div, Expr::int(1), Expr::int(0)),
        function("abs", vec![Expr::int(i64::MIN)]),
        binary(BinaryOp::Mul, float(f64::MAX), float(2.0)),
        Expr::Convert {
            to: NumericType::Int,
            value: Box::new(float(9223372036854775808.0)),
        },
    ] {
        assert_eq!(
            expr.evaluate(&vars).unwrap_err().kind,
            ExprErrorKind::NumericError
        );
    }
    assert!(matches!(
        binary(BinaryOp::Div, Expr::int(-7), Expr::int(3))
            .evaluate(&vars)
            .unwrap(),
        Value::Int(-2)
    ));
    assert!(matches!(
        binary(BinaryOp::Rem, Expr::int(-7), Expr::int(3))
            .evaluate(&vars)
            .unwrap(),
        Value::Int(-1)
    ));
    assert!(matches!(
        (Expr::Convert {
            to: NumericType::Int,
            value: Box::new(float(-1.9))
        })
        .evaluate(&vars)
        .unwrap(),
        Value::Int(-1)
    ));
    assert!(matches!(
        (Expr::Convert {
            to: NumericType::Int,
            value: Box::new(float(-9223372036854775808.0))
        })
        .evaluate(&vars)
        .unwrap(),
        Value::Int(i64::MIN)
    ));
    assert_eq!(
        binary(BinaryOp::Add, Expr::int(1), float(1.0))
            .evaluate(&vars)
            .unwrap_err()
            .kind,
        ExprErrorKind::TypeError
    );
}
#[test]
fn boolean_short_circuit_and_ordered_record_initialization_preserve_first_error() {
    let danger = binary(
        BinaryOp::Gt,
        binary(BinaryOp::Div, Expr::int(1), Expr::var("zero")),
        Expr::int(0),
    );
    let env = BTreeMap::from([("zero".into(), Value::Int(0))]);
    assert!(matches!(
        binary(BinaryOp::And, Expr::boolean(false), danger.clone())
            .evaluate(&env)
            .unwrap(),
        Value::Bool(false)
    ));
    assert!(matches!(
        binary(BinaryOp::Or, Expr::boolean(true), danger.clone())
            .evaluate(&env)
            .unwrap(),
        Value::Bool(true)
    ));
    let error = (Expr::Record {
        fields: vec![
            named(
                "z-first",
                function("unwrap", vec![Expr::None { element: Type::Int }]),
            ),
            named(
                "a-second",
                binary(BinaryOp::Div, Expr::int(1), Expr::var("zero")),
            ),
        ],
    })
    .evaluate(&env)
    .unwrap_err();
    assert_eq!(error.kind, ExprErrorKind::UnwrapNone);
    assert!(error.path.contains("fields[0]"));
}
#[test]
fn builtins_unicode_collections_options_and_reference_equality_are_typed() {
    let vars = BTreeMap::new();
    assert!(matches!(
        function("len", vec![string("é🦀e\u{301}")])
            .evaluate(&vars)
            .unwrap(),
        Value::Int(4)
    ));
    for (name, args) in [
        ("contains", vec![string("你好🦀"), string("好")]),
        ("starts_with", vec![string("你好"), string("你")]),
        ("ends_with", vec![string("你好"), string("好")]),
        ("is_none", vec![Expr::None { element: Type::Int }]),
        (
            "is_some",
            vec![Expr::Some {
                value: Box::new(Expr::int(2)),
            }],
        ),
    ] {
        assert!(matches!(
            function(name, args).evaluate(&vars).unwrap(),
            Value::Bool(true)
        ));
    }
    assert!(matches!(
        function("min", vec![Expr::int(7), Expr::int(-2), Expr::int(3)])
            .evaluate(&vars)
            .unwrap(),
        Value::Int(-2)
    ));
    assert!(
        matches!(function("max",vec![float(-2.0),float(3.0)]).evaluate(&vars).unwrap(),Value::Float(v) if v.get()==3.0)
    );
    let list = Expr::List {
        element: Type::Int,
        items: vec![Expr::int(1), Expr::int(2)],
    };
    assert!(matches!(
        binary(BinaryOp::In, Expr::int(2), list)
            .evaluate(&vars)
            .unwrap(),
        Value::Bool(true)
    ));
    assert_eq!(
        function("now_ms", vec![]).evaluate(&vars).unwrap_err().kind,
        ExprErrorKind::UnknownFunction
    );
    assert_eq!(
        function("min", vec![]).evaluate(&vars).unwrap_err().kind,
        ExprErrorKind::TypeError
    );
    let src = source(SourceKind::Video, "camera");
    assert_eq!(
        binary(BinaryOp::Eq, src.clone(), src)
            .evaluate(&vars)
            .unwrap_err()
            .kind,
        ExprErrorKind::TypeError
    );
}
#[tokio::test]
async fn empty_seq_and_last_result_have_distinct_types_and_trace_pairs() {
    let empty = execute(document(seq("empty", vec![]), Type::Unit)).await;
    assert!(matches!(empty.outcome, FlowOutcome::Success(Value::Unit)));
    let last = execute(document(
        seq(
            "seq",
            vec![eval("first", Expr::int(3)), eval("last", Expr::int(9))],
        ),
        Type::Int,
    ))
    .await;
    success_int(&last, 9);
    for (i, event) in last.trace.iter().enumerate() {
        assert_eq!(event.event_seq, i as u64 + 1);
        assert_eq!(event.run_id, last.run_id);
    }
    for id in ["seq", "first", "last"] {
        let events = last
            .trace
            .iter()
            .filter(|e| e.node_id.as_deref() == Some(id))
            .collect::<Vec<_>>();
        assert_eq!(events.len(), 2);
        assert!(matches!(events[0].kind, TraceKind::NodeStarted));
        assert!(matches!(events[1].kind, TraceKind::NodeSucceeded { .. }));
        assert_eq!(events[0].invocation_id, events[1].invocation_id);
    }
    assert!(matches!(
        last.trace.last().unwrap().kind,
        TraceKind::RunFinished { kind: "success" }
    ));
}
#[tokio::test]
async fn bind_and_result_bind_are_visible_to_later_siblings_but_nested_seq_is_local() {
    let mut first = Node::new(
        "bind",
        Flow::Bind {
            name: "x".into(),
            expr: Expr::int(10),
            next: Box::new(eval(
                "next",
                binary(BinaryOp::Add, Expr::var("x"), Expr::int(1)),
            )),
        },
    );
    first.result_bind = Some("y".into());
    let inner = seq(
        "inner",
        vec![Node::new(
            "shadow",
            Flow::Bind {
                name: "x".into(),
                expr: Expr::int(99),
                next: Box::new(eval("shadow-value", Expr::var("x"))),
            },
        )],
    );
    let result = execute(document(
        seq(
            "root",
            vec![
                first,
                inner,
                eval("sum", binary(BinaryOp::Add, Expr::var("x"), Expr::var("y"))),
            ],
        ),
        Type::Int,
    ))
    .await;
    success_int(&result, 21);
}
#[tokio::test]
async fn choose_only_runs_selected_branch_and_result_binding_exports_its_value() {
    let mut choose = Node::new(
        "choose",
        Flow::Choose {
            cond: Expr::boolean(false),
            then: Box::new(call("not-run", "clock_now", vec![])),
            else_: Some(Box::new(eval("selected", Expr::int(7)))),
        },
    );
    choose.result_bind = Some("chosen".into());
    let doc = document(
        seq("root", vec![choose, eval("out", Expr::var("chosen"))]),
        Type::Int,
    );
    let checked = doc.validate().unwrap();
    assert!(checked.effects("root").unwrap().meta);
    let result = checked
        .execute(
            RunBindings::default(),
            BTreeMap::new(),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    success_int(&result, 7);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("not-run"))
    );
}
#[tokio::test]
async fn nested_pipe_shadows_prev_and_restores_outer_value() {
    let nested = Node::new(
        "nested",
        Flow::Pipe {
            source: Box::new(eval("nested-source", Expr::int(20))),
            sink: Box::new(eval("nested-sink", Expr::var("$prev"))),
        },
    );
    let pipe = Node::new(
        "pipe",
        Flow::Pipe {
            source: Box::new(eval("source", Expr::int(10))),
            sink: Box::new(seq(
                "sink",
                vec![nested, eval("outer-prev", Expr::var("$prev"))],
            )),
        },
    );
    success_int(&execute(document(pipe, Type::Int)).await, 10);
}
#[test]
fn validation_rejects_scope_leaks_duplicates_reserved_names_and_type_mismatches() {
    let leaked = Node::new(
        "choice",
        Flow::Choose {
            cond: Expr::boolean(true),
            then: Box::new(Node::new(
                "local-bind",
                Flow::Bind {
                    name: "secret".into(),
                    expr: Expr::int(1),
                    next: Box::new(eval("local", literal(Literal::Unit))),
                },
            )),
            else_: None,
        },
    );
    let cases = vec![
        document(
            seq("root", vec![leaked, eval("leak", Expr::var("secret"))]),
            Type::Int,
        ),
        document(
            Node::new(
                "reserved",
                Flow::Bind {
                    name: "$prev".into(),
                    expr: Expr::int(1),
                    next: Box::new(eval("next", Expr::int(2))),
                },
            ),
            Type::Int,
        ),
        document(
            Node::new(
                "bad-cond",
                Flow::Choose {
                    cond: Expr::int(1),
                    then: Box::new(eval("then", literal(Literal::Unit))),
                    else_: None,
                },
            ),
            Type::Unit,
        ),
        document(
            Node::new(
                "bad-branch",
                Flow::Choose {
                    cond: Expr::boolean(true),
                    then: Box::new(eval("then", Expr::int(1))),
                    else_: None,
                },
            ),
            Type::Int,
        ),
        document(
            seq(
                "root",
                vec![
                    eval("duplicate", Expr::int(1)),
                    eval("duplicate", Expr::int(2)),
                ],
            ),
            Type::Int,
        ),
    ];
    for doc in cases {
        assert!(doc.validate().is_err());
    }
    let mut first = eval("one", Expr::int(1));
    first.result_bind = Some("x".into());
    let mut second = eval("two", Expr::int(2));
    second.result_bind = Some("x".into());
    assert!(
        document(seq("seq", vec![first, second]), Type::Int)
            .validate()
            .is_err()
    );
    let mut invalid = document(eval("version", Expr::int(1)), Type::Int);
    invalid.ir_version = "0.2".into();
    assert!(invalid.validate().is_err());
    assert!(
        document(eval("prev", Expr::var("$prev")), Type::Int)
            .validate()
            .is_err()
    );
}
#[test]
fn constants_and_primitive_contracts_are_checked_before_execution() {
    for node in [
        call("sleep", "sleep", vec![named("duration_ms", Expr::int(-1))]),
        call("args", "sleep", vec![]),
        call("unknown", "read_clock", vec![]),
        eval(
            "overflow",
            binary(BinaryOp::Add, Expr::int(i64::MAX), Expr::int(1)),
        ),
    ] {
        assert!(document(node, Type::Unit).validate().is_err());
    }
    let mut node = call("version", "sleep", vec![named("duration_ms", Expr::int(0))]);
    if let Flow::Primitive { version, .. } = &mut node.flow {
        *version = 2;
    }
    assert!(document(node, Type::Unit).validate().is_err());
    for id in PrimitiveId::ALL {
        for input in id.contract().inputs {
            let shape = if input.ty == "LogFields" {
                Type::Record(BTreeMap::new())
            } else if input.ty == "FrameOrImageRef" {
                Type::Frame
            } else {
                Type::Unit
            };
            assert!(
                primitive::input_type(input.ty, &shape).is_some(),
                "{:?}: {}",
                id,
                input.ty
            );
        }
    }
}
#[tokio::test]
async fn dynamic_expression_error_keeps_node_and_expression_path_and_stops_seq() {
    let mut doc = document(
        seq(
            "root",
            vec![
                eval(
                    "divide",
                    binary(BinaryOp::Div, Expr::int(1), Expr::var("divisor")),
                ),
                eval("unreachable", Expr::int(3)),
            ],
        ),
        Type::Int,
    );
    doc.definitions
        .get_mut("main")
        .unwrap()
        .params
        .insert("divisor".into(), Type::Int);
    let result = doc
        .validate()
        .unwrap()
        .execute(
            RunBindings::default(),
            BTreeMap::from([("divisor".into(), Value::Int(0))]),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    let FlowOutcome::Failure(error) = &result.outcome else {
        panic!()
    };
    assert_eq!(error.node_id.as_deref(), Some("divide"));
    assert_eq!(
        error.expression.as_ref().unwrap().kind,
        ExprErrorKind::NumericError
    );
    assert!(error.expression.as_ref().unwrap().path.ends_with(".expr"));
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("unreachable"))
    );
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| matches!(e.kind, TraceKind::NodeFailed { .. }))
            .count(),
        2
    );
}

struct Pad {
    info: SessionInfo,
    state: Report,
    records: Arc<Mutex<Vec<Report>>>,
    fail_write: bool,
    fail_reset: bool,
    cancel_on_write: Option<CancellationToken>,
}
impl Pad {
    fn new() -> Self {
        Self {
            info: SessionInfo {
                port: "fake".into(),
                backend: "fake",
                baud_rate: 115200,
                session: 7,
            },
            state: Report::default(),
            records: Arc::new(Mutex::new(vec![])),
            fail_write: false,
            fail_reset: false,
            cancel_on_write: None,
        }
    }
}
impl Gamepad for Pad {
    fn info(&self) -> &SessionInfo {
        &self.info
    }
    fn is_connected(&self) -> bool {
        true
    }
    fn state(&self) -> Option<Report> {
        Some(self.state)
    }
    fn apply_report(
        &mut self,
        report: Report,
        _: &CancellationToken,
    ) -> easycon_device::Result<WriteReceipt> {
        if self.fail_write {
            return Err(DeviceError::WorkerPanicked);
        }
        self.state = report;
        let mut records = self.records.lock().unwrap();
        records.push(report);
        if let Some(token) = &self.cancel_on_write {
            token.cancel();
        }
        Ok(WriteReceipt {
            session: 7,
            sequence: records.len() as u64,
            delivery: Delivery::Written,
            written_at: Instant::now(),
        })
    }
    fn reset(&mut self, token: &CancellationToken) -> easycon_device::Result<WriteReceipt> {
        assert!(!token.is_cancelled());
        if self.fail_reset {
            return Err(DeviceError::Closed);
        }
        self.apply_report(Report::default(), token)
    }
    fn disconnect(&mut self) -> easycon_device::Result<()> {
        panic!("borrowed connection must remain open")
    }
}
fn pad_action(id: &str, name: &str) -> Node {
    call(
        id,
        name,
        vec![
            named("target", source(SourceKind::Gamepad, "switch")),
            named(
                "buttons",
                Expr::List {
                    element: Type::Symbol(SymbolKind::Button),
                    items: vec![symbol(SymbolKind::Button, "A")],
                },
            ),
        ],
    )
}
#[tokio::test]
async fn press_wait_release_runs_through_ir_and_finishes_with_neutral_cleanup() {
    let doc = document(
        seq(
            "hold",
            vec![
                pad_action("press", "pad_press"),
                call("wait", "sleep", vec![named("duration_ms", Expr::int(0))]),
                pad_action("release", "pad_release"),
            ],
        ),
        Type::InputReceipt,
    )
    .validate()
    .unwrap();
    assert!(doc.effects("hold").unwrap().effectful);
    assert!(doc.effects("hold").unwrap().meta);
    let mut pad = Pad::new();
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    let result = doc
        .execute(bindings, BTreeMap::new(), &CancellationToken::new())
        .await
        .unwrap();
    assert!(matches!(
        result.outcome,
        FlowOutcome::Success(Value::InputReceipt(_))
    ));
    assert_eq!(
        records
            .lock()
            .unwrap()
            .iter()
            .map(|r| r.buttons)
            .collect::<Vec<_>>(),
        vec![Button::A.mask(), 0, 0]
    );
    assert!(matches!(
        result.cleanup.entries[0].state,
        CleanupState::Released { receipt: Some(_) }
    ));
}
#[tokio::test(start_paused = true)]
async fn parent_cancellation_interrupts_sleep_skips_next_action_and_resets_pad() {
    let doc = document(
        seq(
            "sequence",
            vec![
                pad_action("press", "pad_press"),
                call(
                    "wait",
                    "sleep",
                    vec![named("duration_ms", Expr::int(10_000))],
                ),
                pad_action("skip", "pad_release"),
            ],
        ),
        Type::InputReceipt,
    )
    .validate()
    .unwrap();
    let mut pad = Pad::new();
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    let parent = CancellationToken::new();
    let start = tokio::time::Instant::now();
    let (result, ()) = tokio::join!(doc.execute(bindings, BTreeMap::new(), &parent), async {
        tokio::time::sleep(std::time::Duration::from_millis(500)).await;
        parent.cancel();
    });
    let result = result.unwrap();
    assert!(matches!(result.outcome, FlowOutcome::Cancelled(_)));
    assert_eq!(start.elapsed().as_millis(), 500);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("skip"))
    );
    assert_eq!(records.lock().unwrap().last().unwrap().buttons, 0);
    assert!(
        result
            .trace
            .iter()
            .any(|e| matches!(e.kind, TraceKind::NodeCancelled { .. }))
    );
}
#[tokio::test]
async fn committed_input_is_retained_when_cancellation_wins_terminal_outcome() {
    let parent = CancellationToken::new();
    let mut pad = Pad::new();
    pad.cancel_on_write = Some(parent.clone());
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    let result = document(pad_action("press", "pad_press"), Type::InputReceipt)
        .validate()
        .unwrap()
        .execute(bindings, BTreeMap::new(), &parent)
        .await
        .unwrap();
    let FlowOutcome::Cancelled(error) = result.outcome else {
        panic!()
    };
    assert!(matches!(error.candidate, Some(Value::InputReceipt(_))));
    assert!(!result.cleanup.has_failures());
}
#[tokio::test]
async fn cleanup_failure_overrides_body_error_and_keeps_typed_original() {
    let mut pad = Pad::new();
    pad.fail_write = true;
    pad.fail_reset = true;
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    let result = document(
        seq(
            "root",
            vec![
                pad_action("fatal", "pad_press"),
                pad_action("skip", "pad_release"),
            ],
        ),
        Type::InputReceipt,
    )
    .validate()
    .unwrap()
    .execute(bindings, BTreeMap::new(), &CancellationToken::new())
    .await
    .unwrap();
    let FlowOutcome::Failure(error) = &result.outcome else {
        panic!()
    };
    assert_eq!(error.kind, FlowErrorKind::CleanupFailed);
    assert_eq!(error.recovery(), Recovery::RunFatal);
    assert!(result.cleanup.has_failures());
    let FlowOutcome::Failure(original) = &result.prior[0] else {
        panic!()
    };
    assert_eq!(original.node_id.as_deref(), Some("fatal"));
    assert_eq!(original.recovery(), Recovery::RunFatal);
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("skip"))
    );
}
#[tokio::test]
async fn runtime_parameter_validation_precedes_resource_enrollment() {
    let mut doc = document(eval("read", Expr::var("x")), Type::Int);
    doc.definitions
        .get_mut("main")
        .unwrap()
        .params
        .insert("x".into(), Type::Int);
    let mut pad = Pad::new();
    let records = pad.records.clone();
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    assert!(
        doc.validate()
            .unwrap()
            .execute(
                bindings,
                BTreeMap::from([("x".into(), Value::Bool(true))]),
                &CancellationToken::new()
            )
            .await
            .is_err()
    );
    assert!(records.lock().unwrap().is_empty());
    let bad = Value::List {
        element: Type::Int,
        items: vec![Value::Bool(false)],
    };
    assert!(!bad.valid());
}
#[tokio::test]
async fn explicit_image_and_audio_measurements_keep_structured_outputs() {
    let mut stats = call(
        "stats",
        "region_statistics",
        vec![
            named("image", Expr::var("image")),
            named(
                "region",
                Expr::None {
                    element: primitive::rect_type(),
                },
            ),
        ],
    );
    stats.result_bind = Some("stats".into());
    let mut doc = document(
        seq(
            "root",
            vec![stats, eval("mean", field(Expr::var("stats"), "rgb_mean"))],
        ),
        Type::Tuple(vec![Type::Float; 3]),
    );
    doc.definitions
        .get_mut("main")
        .unwrap()
        .params
        .insert("image".into(), Type::Image);
    let result = doc
        .validate()
        .unwrap()
        .execute(
            RunBindings::default(),
            BTreeMap::from([(
                "image".into(),
                Value::Image(Image::new(1, 1, vec![10, 20, 30]).unwrap()),
            )]),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    let FlowOutcome::Success(Value::Tuple(values)) = result.outcome else {
        panic!()
    };
    assert!(
        values
            .iter()
            .zip([10.0, 20.0, 30.0])
            .all(|(v, n)| matches!(v,Value::Float(v) if v.get()==n))
    );
    let mut doc = document(
        call(
            "rms",
            "audio_rms",
            vec![named("window", Expr::var("window"))],
        ),
        Type::Float,
    );
    doc.definitions
        .get_mut("main")
        .unwrap()
        .params
        .insert("window".into(), Type::AudioWindow);
    let window = AudioWindow {
        id: SampleId {
            source: "fake".into(),
            session: SessionId(1),
            sequence: 0,
        },
        time: SampleTime {
            received_ns: 0,
            source_ns: None,
        },
        sample_rate: 48_000,
        channels: 2,
        samples: vec![0.0; 8].into(),
    };
    let result = doc
        .validate()
        .unwrap()
        .execute(
            RunBindings::default(),
            BTreeMap::from([("window".into(), Value::AudioWindow(Arc::new(window)))]),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    assert!(matches!(result.outcome,FlowOutcome::Success(Value::Float(v)) if v.get()==0.0));
}

#[tokio::test]
async fn false_choose_without_else_returns_unit_and_has_no_branch_trace() {
    let node = Node::new(
        "choose",
        Flow::Choose {
            cond: Expr::boolean(false),
            then: Box::new(call(
                "skip",
                "sleep",
                vec![named("duration_ms", Expr::int(0))],
            )),
            else_: None,
        },
    );
    let result = execute(document(node, Type::Unit)).await;
    assert!(matches!(result.outcome, FlowOutcome::Success(Value::Unit)));
    assert!(
        !result
            .trace
            .iter()
            .any(|e| e.node_id.as_deref() == Some("skip"))
    );
}

struct Video(Arc<crate::video::VideoFrame>);
impl crate::primitives::VideoInput for Video {
    fn capture<'a>(
        &'a mut self,
        _: Option<&'a SampleId>,
        _: &'a CancellationToken,
    ) -> crate::primitives::CaptureFuture<'a, Arc<crate::video::VideoFrame>> {
        Box::pin(async move { Ok(self.0.clone()) })
    }
}
#[tokio::test]
async fn capture_image_conversion_and_field_selection_keep_sample_identity() {
    let frame = Arc::new(crate::video::VideoFrame {
        id: SampleId {
            source: "camera".into(),
            session: SessionId(5),
            sequence: 9,
        },
        time: SampleTime {
            received_ns: 27,
            source_ns: Some(11),
        },
        format: crate::video::VideoFormat {
            width: 1,
            height: 1,
            frames_per_second: 30,
            encoding: "RGB3".into(),
        },
        rgb: vec![10, 20, 30].into(),
    });
    let converted = function("image_from_frame", vec![Expr::var("frame")])
        .evaluate(&BTreeMap::from([(
            "frame".into(),
            Value::Frame(frame.clone()),
        )]))
        .unwrap();
    let Value::Image(image) = converted else {
        panic!()
    };
    assert_eq!(image.rgb().as_ptr(), frame.rgb.as_ptr());
    let mut capture = call(
        "capture",
        "capture",
        vec![
            named("source", source(SourceKind::Video, "camera")),
            named(
                "after",
                Expr::None {
                    element: Type::SampleId,
                },
            ),
        ],
    );
    capture.result_bind = Some("frame".into());
    let mut stats = call(
        "stats",
        "region_statistics",
        vec![
            named(
                "image",
                function("image_from_frame", vec![Expr::var("frame")]),
            ),
            named(
                "region",
                Expr::None {
                    element: primitive::rect_type(),
                },
            ),
        ],
    );
    stats.result_bind = Some("stats".into());
    let out = field(
        field(
            function("unwrap", vec![field(Expr::var("stats"), "source")]),
            "id",
        ),
        "sequence",
    );
    let doc = document(
        seq("root", vec![capture, stats, eval("identity", out)]),
        Type::Str,
    )
    .validate()
    .unwrap();
    let mut bindings = RunBindings::default();
    bindings
        .videos
        .insert("camera".into(), Box::new(Video(frame)));
    let result = doc
        .execute(bindings, BTreeMap::new(), &CancellationToken::new())
        .await
        .unwrap();
    assert!(matches!(result.outcome,FlowOutcome::Success(Value::Str(v)) if v=="9"));
    assert!(matches!(
        result.cleanup.entries[0].state,
        CleanupState::Released { receipt: None }
    ));
}
struct Matcher;
impl crate::vision::TemplateMatcher for Matcher {
    fn kind(&self) -> crate::vision::TemplateBackendKind {
        crate::vision::TemplateBackendKind::OpenCv5
    }
    fn match_template(
        &mut self,
        image: &Image,
        template: &Image,
        config: crate::vision::MatchConfig,
        _: &CancellationToken,
    ) -> crate::vision::Result<crate::vision::TemplateMatch> {
        Ok(crate::vision::TemplateMatch {
            source: image.source().cloned(),
            search: image.bounds(),
            bounds: crate::vision::Rect {
                x: 0,
                y: 0,
                width: template.width(),
                height: template.height(),
            },
            mode: config.mode,
            raw_score: 0.0,
            confidence: Some(1.0),
            implementation: "mock-fixed".into(),
        })
    }
}
struct Ocr(crate::vision::OcrBackendKind);
impl crate::vision::OcrBackend for Ocr {
    fn kind(&self) -> crate::vision::OcrBackendKind {
        self.0
    }
    fn recognize(
        &mut self,
        image: &Image,
        region: Option<crate::vision::Rect>,
        _: &CancellationToken,
    ) -> crate::vision::Result<crate::vision::OcrResult> {
        let selected = match region {
            Some(region) => image.crop(region)?,
            None => image.clone(),
        };
        let rect = selected.bounds();
        Ok(crate::vision::OcrResult {
            source: image.source().cloned(),
            region: rect,
            kind: self.kind(),
            text: "你好🦀".into(),
            boxes: vec![crate::vision::OcrBox {
                text: "你好🦀".into(),
                confidence: None,
                polygon: [
                    crate::vision::Point {
                        x: rect.x as f32,
                        y: rect.y as f32,
                    },
                    crate::vision::Point {
                        x: (rect.x + rect.width) as f32,
                        y: rect.y as f32,
                    },
                    crate::vision::Point {
                        x: (rect.x + rect.width) as f32,
                        y: (rect.y + rect.height) as f32,
                    },
                    crate::vision::Point {
                        x: rect.x as f32,
                        y: (rect.y + rect.height) as f32,
                    },
                ],
            }],
            profile: "mock-fixed".into(),
        })
    }
}
#[tokio::test]
async fn recognition_adapters_preserve_complete_records_and_schema() {
    for id in [PrimitiveId::MatchTemplate, PrimitiveId::Ocr] {
        let args = if id == PrimitiveId::MatchTemplate {
            vec![
                named("matcher", source(SourceKind::TemplateMatcher, "matcher")),
                named("image", Expr::var("image")),
                named("template", Expr::var("image")),
                named(
                    "config",
                    Expr::Record {
                        fields: vec![
                            named("mode", symbol(SymbolKind::TemplateMode, "SqDiffNormed")),
                            named(
                                "region",
                                Expr::None {
                                    element: primitive::rect_type(),
                                },
                            ),
                        ],
                    },
                ),
            ]
        } else {
            vec![
                named("kind", symbol(SymbolKind::OcrKind, "Tesseract")),
                named("engine", source(SourceKind::Ocr, "ocr")),
                named("image", Expr::var("image")),
                named(
                    "region",
                    Expr::None {
                        element: primitive::rect_type(),
                    },
                ),
            ]
        };
        let mut doc = document(
            call("recognize", id.contract().name, args),
            primitive::output_type(id),
        );
        doc.definitions
            .get_mut("main")
            .unwrap()
            .params
            .insert("image".into(), Type::Image);
        let mut matcher = Matcher;
        let mut ocr = Ocr(crate::vision::OcrBackendKind::Tesseract);
        let mut bindings = RunBindings::default();
        bindings.matchers.insert("matcher".into(), &mut matcher);
        bindings.ocr.insert("ocr".into(), &mut ocr);
        let result = doc
            .validate()
            .unwrap()
            .execute(
                bindings,
                BTreeMap::from([(
                    "image".into(),
                    Value::Image(Image::new(1, 1, vec![1, 2, 3]).unwrap()),
                )]),
                &CancellationToken::new(),
            )
            .await
            .unwrap();
        let FlowOutcome::Success(value) = result.outcome else {
            panic!("{:?}", result.outcome)
        };
        assert_eq!(value.ty(), primitive::output_type(id));
        assert!(value.valid());
        if id == PrimitiveId::MatchTemplate {
            assert!(matches!(
                value.field("confidence"),
                Some(Value::Option { value: Some(_), .. })
            ));
        } else {
            assert!(matches!(value.field("text"),Some(Value::Str(v)) if v=="你好🦀"));
        }
    }
}
#[tokio::test]
async fn capture_pipes_frames_directly_to_both_ocr_kinds_with_original_boxes() {
    use crate::vision::{OcrBackendKind, Rect};
    let frame = Arc::new(crate::video::VideoFrame {
        id: SampleId {
            source: "camera".into(),
            session: SessionId(5),
            sequence: 9,
        },
        time: SampleTime {
            received_ns: 27,
            source_ns: Some(11),
        },
        format: crate::video::VideoFormat {
            width: 6,
            height: 5,
            frames_per_second: 30,
            encoding: "RGB3".into(),
        },
        rgb: vec![0; 6 * 5 * 3].into(),
    });
    let roi = Rect {
        x: 1,
        y: 2,
        width: 3,
        height: 2,
    };
    for kind in [OcrBackendKind::Tesseract, OcrBackendKind::PpOcr] {
        let args = vec![
            named("kind", symbol(SymbolKind::OcrKind, &format!("{kind:?}"))),
            named("engine", source(SourceKind::Ocr, "ocr")),
            named("image", Expr::var("$prev")),
            named(
                "region",
                Expr::Some {
                    value: Box::new(Expr::Record {
                        fields: vec![
                            named("x", Expr::int(1)),
                            named("y", Expr::int(2)),
                            named("width", Expr::int(3)),
                            named("height", Expr::int(2)),
                        ],
                    }),
                },
            ),
        ];
        let body = Node::new(
            "capture-ocr",
            Flow::Pipe {
                source: Box::new(call(
                    "capture",
                    "capture",
                    vec![
                        named("source", source(SourceKind::Video, "camera")),
                        named(
                            "after",
                            Expr::None {
                                element: Type::SampleId,
                            },
                        ),
                    ],
                )),
                sink: Box::new(call("read", "ocr", args)),
            },
        );
        let doc = document(body, primitive::output_type(PrimitiveId::Ocr));
        let encoded = serde_json::to_string(&doc).unwrap();
        let decoded: Document = serde_json::from_str(&encoded).unwrap();
        assert_eq!(doc, decoded);
        let mut ocr = Ocr(kind);
        let mut bindings = RunBindings::default();
        bindings
            .videos
            .insert("camera".into(), Box::new(Video(frame.clone())));
        bindings.ocr.insert("ocr".into(), &mut ocr);
        let execution = decoded
            .validate()
            .unwrap()
            .execute(bindings, BTreeMap::new(), &CancellationToken::new())
            .await
            .unwrap();
        let FlowOutcome::Success(value) = execution.outcome else {
            panic!("{:?}", execution.outcome)
        };
        assert_eq!(value.ty(), primitive::output_type(PrimitiveId::Ocr));
        assert!(value.field("kind").unwrap().equal(&Value::Symbol(
            Symbol::new(SymbolKind::OcrKind, format!("{kind:?}")).unwrap()
        )));
        assert!(value.field("region").unwrap().equal(&metadata::rv(roi)));
        let Some(Value::Option {
            value: Some(origin),
            ..
        }) = value.field("source")
        else {
            panic!()
        };
        assert!(matches!(origin.field("id"), Some(Value::SampleId(id)) if id == frame.id));
        assert!(matches!(origin.field("width"), Some(Value::Int(6))));
        assert!(matches!(origin.field("height"), Some(Value::Int(5))));
        assert!(matches!(origin.field("received_ns"), Some(Value::Str(t)) if t == "27"));
        assert!(
            origin
                .field("source_ns")
                .unwrap()
                .equal(&metadata::opt(Type::Str, Some(Value::Str("11".into()))))
        );
        assert!(matches!(value.field("text"), Some(Value::Str(text)) if text == "你好🦀"));
        assert!(matches!(
            value.field("confidence"),
            Some(Value::Option { value: None, .. })
        ));
        let Some(Value::List { items, .. }) = value.field("boxes") else {
            panic!()
        };
        assert_eq!(items.len(), 1);
        assert!(
            items[0]
                .field("text")
                .unwrap()
                .equal(&value.field("text").unwrap())
        );
        let Some(Value::Tuple(points)) = items[0].field("polygon") else {
            panic!()
        };
        assert!(matches!(points[0].field("x"), Some(Value::Float(n)) if n.get() == 1.0));
        assert!(matches!(points[2].field("y"), Some(Value::Float(n)) if n.get() == 4.0));
    }
}

#[test]
fn ocr_requires_typed_kind_and_explicit_frame_or_image_pixels() {
    for (kind, image) in [
        (string("Tesseract"), Type::Image),
        (
            symbol(SymbolKind::OcrKind, "Tesseract"),
            Type::Source(SourceKind::Video),
        ),
        (symbol(SymbolKind::OcrKind, "PpOcr"), Type::Str),
    ] {
        let mut doc = document(
            call(
                "read",
                "ocr",
                vec![
                    named("kind", kind),
                    named("engine", source(SourceKind::Ocr, "ocr")),
                    named("image", Expr::var("image")),
                    named(
                        "region",
                        Expr::None {
                            element: primitive::rect_type(),
                        },
                    ),
                ],
            ),
            primitive::output_type(PrimitiveId::Ocr),
        );
        doc.definitions
            .get_mut("main")
            .unwrap()
            .params
            .insert("image".into(), image);
        assert!(doc.validate().is_err());
    }
    assert!(Symbol::new(SymbolKind::OcrKind, "unknown").is_none());
}

struct Logs(Arc<Mutex<Vec<crate::meta::LogRecord>>>);
impl crate::meta::LogSink for Logs {
    fn emit(&self, record: crate::meta::LogRecord) -> Result<(), String> {
        self.0.lock().unwrap().push(record);
        Ok(())
    }
}
#[tokio::test]
async fn clock_and_user_log_are_meta_and_do_not_replace_node_trace() {
    let mut clock = call("clock", "clock_now", vec![]);
    clock.result_bind = Some("now".into());
    let log = call(
        "log",
        "log",
        vec![
            named("level", symbol(SymbolKind::LogLevel, "Info")),
            named("message", string("hello")),
            named(
                "fields",
                Expr::Record {
                    fields: vec![
                        named("time", Expr::var("now")),
                        named("value", Expr::int(i64::MAX)),
                    ],
                },
            ),
        ],
    );
    let doc = document(seq("root", vec![clock, log]), Type::Unit)
        .validate()
        .unwrap();
    assert!(doc.effects("root").unwrap().meta);
    assert!(!doc.effects("root").unwrap().effectful);
    let logs = Arc::new(Mutex::new(vec![]));
    let meta = crate::meta::MetaContext::new(Arc::new(Logs(logs.clone())));
    let result = doc
        .execute(
            RunBindings {
                meta: Some(&meta),
                ..RunBindings::default()
            },
            BTreeMap::new(),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    assert!(matches!(result.outcome, FlowOutcome::Success(Value::Unit)));
    assert_eq!(logs.lock().unwrap().len(), 1);
    let logged = result
        .trace
        .iter()
        .find(|e| matches!(e.kind, TraceKind::UserLog { .. }))
        .unwrap();
    let start = result
        .trace
        .iter()
        .find(|e| e.node_id.as_deref() == Some("log") && matches!(e.kind, TraceKind::NodeStarted))
        .unwrap();
    assert_eq!(logged.parent_invocation_id, start.parent_invocation_id);
    assert_eq!(logged.invocation_id, start.invocation_id);
}
#[tokio::test]
async fn entry_terminal_is_after_cleanup_and_reflects_cleanup_failure() {
    let mut pad = Pad::new();
    pad.fail_reset = true;
    let mut bindings = RunBindings::default();
    bindings.pads.insert("switch".into(), &mut pad);
    let result = document(eval("root", Expr::int(1)), Type::Int)
        .validate()
        .unwrap()
        .execute(bindings, BTreeMap::new(), &CancellationToken::new())
        .await
        .unwrap();
    let terminal = result
        .trace
        .iter()
        .position(|e| {
            e.node_id.as_deref() == Some("root") && matches!(e.kind, TraceKind::NodeFailed { .. })
        })
        .unwrap();
    let cleanup = result
        .trace
        .iter()
        .position(|e| matches!(e.kind, TraceKind::CleanupCompleted { failed: true, .. }))
        .unwrap();
    assert!(cleanup < terminal);
    assert_eq!(
        result
            .trace
            .iter()
            .filter(|e| e.node_id.as_deref() == Some("root"))
            .count(),
        2
    );
    assert!(matches!(
        result.prior[0],
        FlowOutcome::Success(Value::Int(1))
    ));
}

#[test]
fn constant_bindings_and_node_results_are_checked_without_opening_capabilities() {
    let bound = Node::new(
        "bind",
        Flow::Bind {
            name: "duration".into(),
            expr: Expr::int(-1),
            next: Box::new(call(
                "sleep",
                "sleep",
                vec![named("duration_ms", Expr::var("duration"))],
            )),
        },
    );
    assert!(document(bound, Type::Unit).validate().is_err());
    let mut result = eval("value", Expr::int(-1));
    result.result_bind = Some("duration".into());
    assert!(
        document(
            seq(
                "root",
                vec![
                    result,
                    call(
                        "sleep",
                        "sleep",
                        vec![named("duration_ms", Expr::var("duration"))]
                    )
                ]
            ),
            Type::Unit
        )
        .validate()
        .is_err()
    );
    let pipe = Node::new(
        "pipe",
        Flow::Pipe {
            source: Box::new(eval("negative", Expr::int(-1))),
            sink: Box::new(call(
                "sleep",
                "sleep",
                vec![named("duration_ms", Expr::var("$prev"))],
            )),
        },
    );
    assert!(document(pipe, Type::Unit).validate().is_err());
}
#[tokio::test]
async fn unknown_inner_binding_does_not_reuse_parent_constant() {
    let outer = Node::new(
        "outer",
        Flow::Bind {
            name: "duration".into(),
            expr: Expr::int(-1),
            next: Box::new(eval("outer-value", Expr::var("duration"))),
        },
    );
    let inner = Node::new(
        "inner-bind",
        Flow::Bind {
            name: "duration".into(),
            expr: Expr::var("input"),
            next: Box::new(call(
                "sleep",
                "sleep",
                vec![named("duration_ms", Expr::var("duration"))],
            )),
        },
    );
    let mut doc = document(
        seq(
            "root",
            vec![
                outer,
                seq("inner", vec![inner]),
                eval("restore", Expr::var("duration")),
            ],
        ),
        Type::Int,
    );
    doc.definitions
        .get_mut("main")
        .unwrap()
        .params
        .insert("input".into(), Type::Int);
    let result = doc
        .validate()
        .unwrap()
        .execute(
            RunBindings::default(),
            BTreeMap::from([("input".into(), Value::Int(0))]),
            &CancellationToken::new(),
        )
        .await
        .unwrap();
    success_int(&result, -1);
}

mod combinators;
mod runtime_contract;
