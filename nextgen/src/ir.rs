//! Versioned orchestration IR. Only a validated document can be executed.
//! Typed expressions, first-class flow references and structured combinators.
//!
//! ```no_run
//! use std::collections::BTreeMap;
//! use easycon_sense::{ir::{Document, FlowOutcome, Value}, run::RunBindings};
//! use tokio_util::sync::CancellationToken;
//! # async fn example() -> Result<(), Box<dyn std::error::Error>> {
//! let json = std::fs::read_to_string("examples/composition.ir.json")?;
//! let checked = Document::from_json(&json)?.validate()?;
//! let execution = checked.execute(
//!     RunBindings::default(), BTreeMap::new(), &CancellationToken::new(),
//! ).await?;
//! assert!(matches!(execution.outcome, FlowOutcome::Success(Value::Int(42))));
//! assert!(!execution.cleanup.has_failures());
//! # Ok(())
//! # }
//! ```

use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

mod cancellation;
mod control;
mod engine;
mod expr;
mod metadata;
mod primitive;
#[cfg(test)]
mod tests;
mod validate;
mod value;

pub use engine::{
    CancelReason, Execution, ExecutionLimits, FlowError, FlowErrorKind, FlowOutcome, TraceContext,
    TraceError, TraceEvent, TraceKind,
};
pub use expr::{BinaryOp, Expr, ExprError, ExprErrorKind, NamedExpr, NumericType, UnaryOp};
pub use validate::{Effects, ValidatedDocument, ValidationError};
pub use value::{
    Finite, FlowRef, FlowSignature, Literal, SourceKind, SourceRef, Symbol, SymbolKind, Type, Value,
};

pub use cancellation::{RunControl, RunStopReason};
pub use control::observe;

pub const IR_VERSION: &str = "0.1";

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Document {
    pub ir_version: String,
    pub entry: String,
    #[serde(deserialize_with = "unique_map")]
    pub definitions: BTreeMap<String, Definition>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Definition {
    #[serde(deserialize_with = "unique_map")]
    pub params: BTreeMap<String, Type>,
    pub output: Type,
    pub body: Node,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Node {
    pub node_id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub result_bind: Option<String>,
    pub flow: Flow,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case", deny_unknown_fields)]
pub enum Flow {
    Eval {
        expr: Expr,
    },
    Primitive {
        name: String,
        version: u32,
        args: Vec<NamedExpr>,
    },
    Seq {
        steps: Vec<Node>,
    },
    Bind {
        name: String,
        expr: Expr,
        next: Box<Node>,
    },
    Choose {
        cond: Expr,
        then: Box<Node>,
        else_: Option<Box<Node>>,
    },
    Par {
        mode: ParMode,
        branches: Vec<Node>,
    },
    Retry {
        body: Box<Node>,
        max: Expr,
        retry_on: Expr,
        backoff: Backoff,
    },
    Timeout {
        duration_ms: Expr,
        body: Box<Node>,
    },
    LoopUntil {
        body: Box<Node>,
        cond: Expr,
        max_iters: Option<Expr>,
        state_init: Option<Expr>,
        state_next: Option<Expr>,
    },
    Fallback {
        primary: Box<Node>,
        secondary: Box<Node>,
    },
    Catch {
        body: Box<Node>,
        bind_error: Option<String>,
        handler: Box<Node>,
    },
    SubflowCall {
        target: Expr,
        args: Vec<NamedExpr>,
    },
    Pipe {
        source: Box<Node>,
        sink: Box<Node>,
    },
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ParMode {
    All,
    Any,
    Race,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case", deny_unknown_fields)]
pub enum Backoff {
    None,
    Fixed {
        ms: Expr,
    },
    Exponential {
        base_ms: Expr,
        factor: Expr,
        max_ms: Expr,
    },
}

impl Node {
    pub fn new(node_id: impl Into<String>, flow: Flow) -> Self {
        Self {
            node_id: node_id.into(),
            result_bind: None,
            flow,
        }
    }
}

impl Document {
    pub fn from_json(json: &str) -> Result<Self, serde_json::Error> {
        serde_json::from_str(json)
    }

    pub fn to_json(&self) -> Result<String, serde_json::Error> {
        serde_json::to_string_pretty(self)
    }

    pub fn validate(self) -> Result<ValidatedDocument, ValidationError> {
        validate::validate(self)
    }
}

// JSON object duplicates must not silently replace a definition or parameter signature.
pub(crate) fn unique_map<'de, D, V>(deserializer: D) -> Result<BTreeMap<String, V>, D::Error>
where
    D: serde::Deserializer<'de>,
    V: Deserialize<'de>,
{
    struct Unique<V>(std::marker::PhantomData<V>);
    impl<'de, V: Deserialize<'de>> serde::de::Visitor<'de> for Unique<V> {
        type Value = BTreeMap<String, V>;
        fn expecting(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
            f.write_str("an object with unique keys")
        }
        fn visit_map<A: serde::de::MapAccess<'de>>(
            self,
            mut map: A,
        ) -> Result<Self::Value, A::Error> {
            let mut values = BTreeMap::new();
            while let Some((key, value)) = map.next_entry::<String, V>()? {
                if values.insert(key, value).is_some() {
                    return Err(serde::de::Error::custom("duplicate object key"));
                }
            }
            Ok(values)
        }
    }
    deserializer.deserialize_map(Unique(std::marker::PhantomData))
}
