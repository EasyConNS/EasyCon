use super::*;
use crate::primitives::Recovery;
use std::{collections::BTreeSet, sync::Arc};

pub(super) fn error_type() -> Type {
    Type::ErrorRecord
}
const RECOVERABLE: &[&str] = &[
    "ExprError",
    "CapabilityUnavailable",
    "SourceUnavailable",
    "SourceChanged",
    "AudioGap",
    "InvalidArgument",
    "InvalidData",
    "AssetUnavailable",
    "RecognitionError",
    "TransportError",
    "LogError",
    "Timeout",
    "MaxRetriesExceeded",
    "MaxIterationsExceeded",
];
pub(super) fn retry_kinds(value: &Value) -> Result<BTreeSet<String>, &'static str> {
    let Value::List { items, .. } = value else {
        return Err("retry_on must be List<Str>");
    };
    items
        .iter()
        .map(|v| match v {
            Value::Str(s) if RECOVERABLE.contains(&s.as_str()) => Ok(s.clone()),
            _ => Err("retry_on contains an unknown or non-recoverable error kind"),
        })
        .collect()
}
impl FlowError {
    pub fn kind_name(&self) -> String {
        match self.kind {
            FlowErrorKind::Expression => "ExprError".into(),
            FlowErrorKind::Primitive => self
                .primitive
                .as_ref()
                .map_or_else(|| "InternalError".into(), |p| format!("{:?}", p.kind())),
            FlowErrorKind::Internal => "InternalError".into(),
            other => format!("{other:?}"),
        }
    }
    pub fn origin(&self) -> &'static str {
        match self.kind {
            FlowErrorKind::Expression => "Expr",
            FlowErrorKind::Primitive => "Primitive",
            FlowErrorKind::InvalidArgument if self.primitive_call.is_some() => "Primitive",
            _ => "Combinator",
        }
    }
    pub(super) fn field(&self, name: &str) -> Option<Value> {
        let optional = |ty, value: Option<Value>| Value::Option {
            element: ty,
            value: value.map(Box::new),
        };
        Some(match name {
            "kind" => Value::Str(self.kind_name()),
            "message" => Value::Str(self.message.clone()),
            "origin" => Value::Str(self.origin().into()),
            "recovery" => Value::Str(
                match self.recovery() {
                    Recovery::Recoverable => "Recoverable",
                    Recovery::RunFatal => "RunFatal",
                    Recovery::Cancelled => "Cancelled",
                }
                .into(),
            ),
            "node_id" => optional(Type::Str, self.node_id.clone().map(Value::Str)),
            "invocation_id" => optional(
                Type::Str,
                self.invocation_id.map(|v| Value::Str(v.to_string())),
            ),
            "cancel_reason" => optional(
                Type::Str,
                self.cancel_reason.map(|r| Value::Str(format!("{r:?}"))),
            ),
            "expression_kind" => optional(
                Type::Str,
                self.expression
                    .as_ref()
                    .map(|e| Value::Str(format!("{:?}", e.kind))),
            ),
            "expression_path" => optional(
                Type::Str,
                self.expression.as_ref().map(|e| Value::Str(e.path.clone())),
            ),
            "primitive_name" => optional(
                Type::Str,
                self.primitive_call
                    .as_ref()
                    .map(|(n, _)| Value::Str(n.clone())),
            ),
            "primitive_version" => optional(
                Type::Int,
                self.primitive_call
                    .as_ref()
                    .map(|(_, v)| Value::Int((*v).into())),
            ),
            "cause" => optional(
                Type::ErrorRecord,
                self.cause.clone().map(Value::ErrorRecord),
            ),
            "suppressed" => Value::List {
                element: Type::ErrorRecord,
                items: self
                    .suppressed
                    .iter()
                    .cloned()
                    .map(Value::ErrorRecord)
                    .collect(),
            },
            _ => return None,
        })
    }
    pub(super) fn with_context(error: Arc<Self>, original: Arc<Self>) -> Arc<Self> {
        let mut error = (*error).clone();
        error.suppressed.push(original);
        Arc::new(error)
    }
}

/// Standard Observe lowering: first probe immediately, wait only after None, unwrap on success.
/// `prefix` and every probe node ID must be document-wide unique.
pub fn observe(
    prefix: &str,
    probe: Node,
    element: Type,
    interval_ms: Expr,
    duration_ms: Option<Expr>,
) -> Node {
    let node = |suffix: &str, flow| Node::new(format!("{prefix}/{suffix}"), flow);
    let invoke = |name: &str, arg| Expr::Call {
        name: name.into(),
        version: 1,
        args: vec![arg],
    };
    let interval = format!("observe_interval_{prefix}");
    let mut iteration = node(
        "probe_pipe",
        Flow::Pipe {
            source: Box::new(probe),
            sink: Box::new(node(
                "wait_if_none",
                Flow::Choose {
                    cond: invoke("is_some", Expr::var("$prev")),
                    then: Box::new(node(
                        "present",
                        Flow::Eval {
                            expr: Expr::var("$prev"),
                        },
                    )),
                    else_: Some(Box::new(node(
                        "absent",
                        Flow::Seq {
                            steps: vec![
                                node(
                                    "sleep",
                                    Flow::Primitive {
                                        name: "sleep".into(),
                                        version: 1,
                                        args: vec![NamedExpr {
                                            name: "duration_ms".into(),
                                            expr: Expr::var(&interval),
                                        }],
                                    },
                                ),
                                node(
                                    "none",
                                    Flow::Eval {
                                        expr: Expr::None { element },
                                    },
                                ),
                            ],
                        },
                    ))),
                },
            )),
        },
    );
    iteration = node(
        "loop",
        Flow::LoopUntil {
            body: Box::new(iteration),
            cond: invoke("is_some", Expr::var("$result")),
            max_iters: None,
            state_init: None,
            state_next: None,
        },
    );
    iteration = node(
        "unwrap_pipe",
        Flow::Pipe {
            source: Box::new(iteration),
            sink: Box::new(node(
                "unwrap",
                Flow::Eval {
                    expr: invoke("unwrap", Expr::var("$prev")),
                },
            )),
        },
    );
    if let Some(duration_ms) = duration_ms {
        iteration = node(
            "timeout",
            Flow::Timeout {
                duration_ms,
                body: Box::new(iteration),
            },
        );
    }
    // Validate a dynamic negative interval before even a successful first probe.
    iteration = node(
        "validated",
        Flow::Seq {
            steps: vec![
                node(
                    "interval_guard",
                    Flow::Choose {
                        cond: Expr::Binary {
                            op: BinaryOp::Ge,
                            left: Box::new(Expr::var(&interval)),
                            right: Box::new(Expr::int(0)),
                        },
                        then: Box::new(node("interval_ok", Flow::Seq { steps: vec![] })),
                        else_: Some(Box::new(node(
                            "invalid_interval",
                            Flow::Primitive {
                                name: "sleep".into(),
                                version: 1,
                                args: vec![NamedExpr {
                                    name: "duration_ms".into(),
                                    expr: Expr::var(&interval),
                                }],
                            },
                        ))),
                    },
                ),
                iteration,
            ],
        },
    );
    Node::new(
        prefix,
        Flow::Seq {
            steps: vec![node(
                "interval",
                Flow::Bind {
                    name: interval,
                    expr: interval_ms,
                    next: Box::new(iteration),
                },
            )],
        },
    )
}
