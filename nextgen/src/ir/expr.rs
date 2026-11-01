use super::{Finite, Literal, Type, Value};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use thiserror::Error;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum UnaryOp {
    Not,
    Neg,
}
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum BinaryOp {
    Add,
    Sub,
    Mul,
    Div,
    Rem,
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
    And,
    Or,
    In,
}
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum NumericType {
    Int,
    Float,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct NamedExpr {
    pub name: String,
    pub expr: Expr,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(tag = "expr", rename_all = "snake_case", deny_unknown_fields)]
pub enum Expr {
    FlowRef {
        definition: String,
    },
    Literal {
        value: Literal,
    },
    Var {
        name: String,
    },
    Field {
        base: Box<Expr>,
        name: String,
    },
    Index {
        base: Box<Expr>,
        index: Box<Expr>,
    },
    List {
        element: Type,
        items: Vec<Expr>,
    },
    Tuple {
        items: Vec<Expr>,
    },
    Record {
        fields: Vec<NamedExpr>,
    },
    Some {
        value: Box<Expr>,
    },
    None {
        element: Type,
    },
    Unary {
        op: UnaryOp,
        value: Box<Expr>,
    },
    Binary {
        op: BinaryOp,
        left: Box<Expr>,
        right: Box<Expr>,
    },
    Convert {
        to: NumericType,
        value: Box<Expr>,
    },
    Call {
        name: String,
        version: u32,
        args: Vec<Expr>,
    },
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ExprErrorKind {
    TypeError,
    UnknownVariable,
    MissingField,
    IndexOutOfBounds,
    UnwrapNone,
    NumericError,
    UnknownFunction,
    InvalidArgument,
    LimitExceeded,
}
#[derive(Clone, Debug, Error)]
#[error("{kind:?} at {path}: {message}")]
pub struct ExprError {
    pub kind: ExprErrorKind,
    pub path: String,
    pub message: String,
}
impl ExprError {
    pub(super) fn new(kind: ExprErrorKind, path: &str, message: impl Into<String>) -> Self {
        Self {
            kind,
            path: path.into(),
            message: message.into(),
        }
    }
}
pub(super) fn lookup<'a, T>(scopes: &'a [BTreeMap<String, T>], name: &str) -> Option<&'a T> {
    scopes.iter().rev().find_map(|scope| scope.get(name))
}
fn fail<T>(kind: ExprErrorKind, path: &str, message: impl Into<String>) -> Result<T, ExprError> {
    Err(ExprError::new(kind, path, message))
}
fn same(actual: &Type, expected: &Type, path: &str) -> Result<(), ExprError> {
    if expected.accepts(actual) {
        Ok(())
    } else {
        fail(
            ExprErrorKind::TypeError,
            path,
            format!("expected {expected:?}, got {actual:?}"),
        )
    }
}
fn number(ty: &Type) -> bool {
    matches!(ty, Type::Int | Type::Float)
}
impl Expr {
    pub fn int(value: i64) -> Self {
        Self::Literal {
            value: Literal::Int(value),
        }
    }
    pub fn boolean(value: bool) -> Self {
        Self::Literal {
            value: Literal::Bool(value),
        }
    }
    pub fn var(name: impl Into<String>) -> Self {
        Self::Var { name: name.into() }
    }
    /// Standalone evaluation performs type checking before interpreting any expression.
    pub fn evaluate(&self, variables: &BTreeMap<String, Value>) -> Result<Value, ExprError> {
        if variables.values().any(|v| !v.valid()) {
            return fail(ExprErrorKind::TypeError, "expr", "invalid runtime value");
        }
        self.infer(
            &[variables.iter().map(|(k, v)| (k.clone(), v.ty())).collect()],
            "expr",
            0,
        )?;
        self.eval(std::slice::from_ref(variables), "expr")
    }
    pub(super) fn known(&self, values: &BTreeMap<String, Value>) -> bool {
        match self {
            Self::Var { name } => values.contains_key(name),
            Self::Literal { .. } | Self::None { .. } | Self::FlowRef { .. } => true,
            Self::Field { base, .. } => base.known(values),
            Self::Index { base, index } => base.known(values) && index.known(values),
            Self::List { items, .. } | Self::Tuple { items } | Self::Call { args: items, .. } => {
                items.iter().all(|e| e.known(values))
            }
            Self::Record { fields } => fields.iter().all(|f| f.expr.known(values)),
            Self::Some { value } | Self::Unary { value, .. } | Self::Convert { value, .. } => {
                value.known(values)
            }
            Self::Binary { left, right, .. } => left.known(values) && right.known(values),
        }
    }
    pub(super) fn infer(
        &self,
        env: &[BTreeMap<String, Type>],
        path: &str,
        depth: usize,
    ) -> Result<Type, ExprError> {
        self.infer_with(env, path, depth, &BTreeMap::new())
    }
    pub(super) fn infer_with(
        &self,
        env: &[BTreeMap<String, Type>],
        path: &str,
        depth: usize,
        refs: &BTreeMap<String, super::FlowRef>,
    ) -> Result<Type, ExprError> {
        if depth >= 128 {
            return fail(
                ExprErrorKind::LimitExceeded,
                path,
                "expression depth exceeds 128",
            );
        }
        let infer = |expr: &Expr, suffix: &str| {
            expr.infer_with(env, &format!("{path}.{suffix}"), depth + 1, refs)
        };
        match self {
            Self::FlowRef { definition } => refs
                .get(definition)
                .map(|r| Type::FlowRef(r.signature.clone()))
                .ok_or_else(|| {
                    ExprError::new(
                        ExprErrorKind::InvalidArgument,
                        path,
                        "unknown flow definition",
                    )
                }),
            Self::Literal { value } => {
                let value = Value::from(value);
                if !value.valid() {
                    return fail(ExprErrorKind::InvalidArgument, path, "empty source binding");
                }
                Ok(value.ty())
            }
            Self::Var { name } => lookup(env, name)
                .cloned()
                .ok_or_else(|| ExprError::new(ExprErrorKind::UnknownVariable, path, name)),
            Self::Field { base, name } => infer(base, "base")?
                .field(name)
                .ok_or_else(|| ExprError::new(ExprErrorKind::MissingField, path, name)),
            Self::Index { base, index } => {
                same(&infer(index, "index")?, &Type::Int, path)?;
                match infer(base, "base")? {
                    Type::List(t) => Ok(*t),
                    Type::Tuple(ts) => {
                        let Self::Literal {
                            value: Literal::Int(i),
                        } = &**index
                        else {
                            return fail(
                                ExprErrorKind::TypeError,
                                path,
                                "Tuple index must be an Int literal",
                            );
                        };
                        usize::try_from(*i)
                            .ok()
                            .and_then(|i| ts.get(i).cloned())
                            .ok_or_else(|| {
                                ExprError::new(ExprErrorKind::IndexOutOfBounds, path, "Tuple index")
                            })
                    }
                    _ => fail(
                        ExprErrorKind::TypeError,
                        path,
                        "index requires List or Tuple",
                    ),
                }
            }
            Self::List { element, items } => {
                for (i, item) in items.iter().enumerate() {
                    same(&infer(item, &format!("items[{i}]"))?, element, path)?;
                }
                Ok(Type::List(Box::new(element.clone())))
            }
            Self::Tuple { items } => Ok(Type::Tuple(
                items
                    .iter()
                    .enumerate()
                    .map(|(i, e)| infer(e, &format!("items[{i}]")))
                    .collect::<Result<_, _>>()?,
            )),
            Self::Record { fields } => {
                let mut types = BTreeMap::new();
                for (i, f) in fields.iter().enumerate() {
                    let ty = infer(&f.expr, &format!("fields[{i}]"))?;
                    if types.insert(f.name.clone(), ty).is_some() {
                        return fail(ExprErrorKind::TypeError, path, "duplicate record field");
                    }
                }
                Ok(Type::Record(types))
            }
            Self::Some { value } => Ok(Type::Option(Box::new(infer(value, "value")?))),
            Self::None { element } => Ok(Type::Option(Box::new(element.clone()))),
            Self::Unary { op, value } => {
                let ty = infer(value, "value")?;
                match op {
                    UnaryOp::Not => same(&ty, &Type::Bool, path)?,
                    UnaryOp::Neg if number(&ty) => {}
                    _ => {
                        return fail(
                            ExprErrorKind::TypeError,
                            path,
                            "negation requires numeric value",
                        );
                    }
                }
                Ok(ty)
            }
            Self::Binary { op, left, right } => {
                let a = infer(left, "left")?;
                let b = infer(right, "right")?;
                match op {
                    BinaryOp::And | BinaryOp::Or => {
                        same(&a, &Type::Bool, path)?;
                        same(&b, &Type::Bool, path)?;
                        Ok(Type::Bool)
                    }
                    BinaryOp::In => {
                        match &b {
                            Type::Str => same(&a, &Type::Str, path)?,
                            Type::List(t) if t.comparable() => same(&a, t, path)?,
                            _ => {
                                return fail(
                                    ExprErrorKind::TypeError,
                                    path,
                                    "in requires Str or comparable List",
                                );
                            }
                        }
                        Ok(Type::Bool)
                    }
                    _ => {
                        same(&a, &b, path)?;
                        match op {
                            BinaryOp::Eq | BinaryOp::Ne if a.comparable() => Ok(Type::Bool),
                            BinaryOp::Lt | BinaryOp::Le | BinaryOp::Gt | BinaryOp::Ge
                                if number(&a) =>
                            {
                                Ok(Type::Bool)
                            }
                            BinaryOp::Add
                            | BinaryOp::Sub
                            | BinaryOp::Mul
                            | BinaryOp::Div
                            | BinaryOp::Rem
                                if number(&a) =>
                            {
                                Ok(a)
                            }
                            _ => fail(
                                ExprErrorKind::TypeError,
                                path,
                                "operator is not defined for this type",
                            ),
                        }
                    }
                }
            }
            Self::Convert { to, value } => {
                if !number(&infer(value, "value")?) {
                    return fail(ExprErrorKind::TypeError, path, "conversion requires number");
                }
                Ok(match to {
                    NumericType::Int => Type::Int,
                    NumericType::Float => Type::Float,
                })
            }
            Self::Call {
                name,
                version,
                args,
            } => {
                let types = args
                    .iter()
                    .enumerate()
                    .map(|(i, e)| infer(e, &format!("args[{i}]")))
                    .collect::<Result<Vec<_>, _>>()?;
                function_type(name, *version, &types, path)
            }
        }
    }
    pub(super) fn eval(
        &self,
        env: &[BTreeMap<String, Value>],
        path: &str,
    ) -> Result<Value, ExprError> {
        self.eval_with(env, path, &BTreeMap::new())
    }
    pub(super) fn eval_with(
        &self,
        env: &[BTreeMap<String, Value>],
        path: &str,
        refs: &BTreeMap<String, super::FlowRef>,
    ) -> Result<Value, ExprError> {
        let eval =
            |expr: &Expr, suffix: &str| expr.eval_with(env, &format!("{path}.{suffix}"), refs);
        match self {
            Self::FlowRef { definition } => refs
                .get(definition)
                .cloned()
                .map(Value::FlowRef)
                .ok_or_else(|| {
                    ExprError::new(
                        ExprErrorKind::InvalidArgument,
                        path,
                        "unknown flow definition",
                    )
                }),
            Self::Literal { value } => Ok(Value::from(value)),
            Self::Var { name } => lookup(env, name)
                .cloned()
                .ok_or_else(|| ExprError::new(ExprErrorKind::UnknownVariable, path, name)),
            Self::Field { base, name } => eval(base, "base")?
                .field(name)
                .ok_or_else(|| ExprError::new(ExprErrorKind::MissingField, path, name)),
            Self::Index { base, index } => {
                let base = eval(base, "base")?;
                let index = eval(index, "index")?;
                let Value::Int(i) = index else {
                    return fail(ExprErrorKind::TypeError, path, "index must be Int");
                };
                let items = match &base {
                    Value::List { items, .. } | Value::Tuple(items) => items,
                    _ => return fail(ExprErrorKind::TypeError, path, "invalid index base"),
                };
                usize::try_from(i)
                    .ok()
                    .and_then(|i| items.get(i).cloned())
                    .ok_or_else(|| {
                        ExprError::new(
                            ExprErrorKind::IndexOutOfBounds,
                            path,
                            "index outside collection",
                        )
                    })
            }
            Self::List { element, items } => Ok(Value::List {
                element: element.clone(),
                items: items
                    .iter()
                    .enumerate()
                    .map(|(i, e)| eval(e, &format!("items[{i}]")))
                    .collect::<Result<_, _>>()?,
            }),
            Self::Tuple { items } => Ok(Value::Tuple(
                items
                    .iter()
                    .enumerate()
                    .map(|(i, e)| eval(e, &format!("items[{i}]")))
                    .collect::<Result<_, _>>()?,
            )),
            Self::Record { fields } => {
                let mut values = BTreeMap::new();
                for (i, f) in fields.iter().enumerate() {
                    values.insert(f.name.clone(), eval(&f.expr, &format!("fields[{i}]"))?);
                }
                Ok(Value::Record(values))
            }
            Self::Some { value } => {
                let value = eval(value, "value")?;
                Ok(Value::Option {
                    element: value.ty(),
                    value: Some(Box::new(value)),
                })
            }
            Self::None { element } => Ok(Value::Option {
                element: element.clone(),
                value: None,
            }),
            Self::Unary { op, value } => match (op, eval(value, "value")?) {
                (UnaryOp::Not, Value::Bool(v)) => Ok(Value::Bool(!v)),
                (UnaryOp::Neg, Value::Int(v)) => v.checked_neg().map(Value::Int).ok_or_else(|| {
                    ExprError::new(
                        ExprErrorKind::NumericError,
                        path,
                        "integer negation overflow",
                    )
                }),
                (UnaryOp::Neg, Value::Float(v)) => float(-v.get(), path),
                _ => fail(ExprErrorKind::TypeError, path, "unary operand"),
            },
            Self::Binary { op, left, right } => {
                let a = eval(left, "left")?;
                if *op == BinaryOp::And && matches!(a, Value::Bool(false)) {
                    return Ok(Value::Bool(false));
                }
                if *op == BinaryOp::Or && matches!(a, Value::Bool(true)) {
                    return Ok(Value::Bool(true));
                }
                binary(*op, a, eval(right, "right")?, path)
            }
            Self::Convert { to, value } => match (to, eval(value, "value")?) {
                (NumericType::Int, Value::Int(v)) => Ok(Value::Int(v)),
                (NumericType::Float, Value::Float(v)) => Ok(Value::Float(v)),
                (NumericType::Float, Value::Int(v)) => float(v as f64, path),
                (NumericType::Int, Value::Float(v)) => {
                    let v = v.get().trunc();
                    if !(-9223372036854775808.0..9223372036854775808.0).contains(&v) {
                        return fail(ExprErrorKind::NumericError, path, "Float outside Int range");
                    }
                    Ok(Value::Int(v as i64))
                }
                _ => fail(ExprErrorKind::TypeError, path, "conversion operand"),
            },
            Self::Call { name, args, .. } => function(
                name,
                args.iter()
                    .enumerate()
                    .map(|(i, e)| eval(e, &format!("args[{i}]")))
                    .collect::<Result<_, _>>()?,
                path,
            ),
        }
    }
}
fn function_type(name: &str, version: u32, args: &[Type], path: &str) -> Result<Type, ExprError> {
    if version != 1 {
        return fail(
            ExprErrorKind::UnknownFunction,
            path,
            "unsupported function version",
        );
    }
    match (name, args) {
        ("len", [Type::Str | Type::List(_)]) => Ok(Type::Int),
        ("contains" | "starts_with" | "ends_with", [Type::Str, Type::Str]) => Ok(Type::Bool),
        ("abs", [t]) if number(t) => Ok(t.clone()),
        ("min" | "max", [first, rest @ ..]) if number(first) && rest.iter().all(|t| t == first) => {
            Ok(first.clone())
        }
        ("is_none" | "is_some", [Type::Option(_)]) => Ok(Type::Bool),
        ("unwrap", [Type::Option(t)]) => Ok(*t.clone()),
        ("image_from_frame", [Type::Frame]) => Ok(Type::Image),
        (
            "len" | "contains" | "starts_with" | "ends_with" | "abs" | "min" | "max" | "is_none"
            | "is_some" | "unwrap" | "image_from_frame",
            _,
        ) => fail(
            ExprErrorKind::TypeError,
            path,
            "function argument types or arity",
        ),
        _ => fail(ExprErrorKind::UnknownFunction, path, name),
    }
}
fn float(value: f64, path: &str) -> Result<Value, ExprError> {
    Finite::new(value)
        .map(Value::Float)
        .ok_or_else(|| ExprError::new(ExprErrorKind::NumericError, path, "non-finite Float result"))
}
fn binary(op: BinaryOp, a: Value, b: Value, path: &str) -> Result<Value, ExprError> {
    use BinaryOp::*;
    if matches!(op, Eq | Ne) {
        return Ok(Value::Bool(a.equal(&b) == (op == Eq)));
    }
    match (op, a, b) {
        (And | Or, Value::Bool(a), Value::Bool(b)) => {
            Ok(Value::Bool(if op == And { a && b } else { a || b }))
        }
        (In, Value::Str(a), Value::Str(b)) => Ok(Value::Bool(b.contains(&a))),
        (In, a, Value::List { items, .. }) => Ok(Value::Bool(items.iter().any(|b| a.equal(b)))),
        (_, Value::Int(a), Value::Int(b)) => {
            let v = match op {
                Add => a.checked_add(b),
                Sub => a.checked_sub(b),
                Mul => a.checked_mul(b),
                Div => a.checked_div(b),
                Rem => a.checked_rem(b),
                Lt => return Ok(Value::Bool(a < b)),
                Le => return Ok(Value::Bool(a <= b)),
                Gt => return Ok(Value::Bool(a > b)),
                Ge => return Ok(Value::Bool(a >= b)),
                _ => None,
            };
            v.map(Value::Int).ok_or_else(|| {
                ExprError::new(
                    ExprErrorKind::NumericError,
                    path,
                    "integer overflow or division by zero",
                )
            })
        }
        (_, Value::Float(a), Value::Float(b)) => {
            let (a, b) = (a.get(), b.get());
            match op {
                Add => float(a + b, path),
                Sub => float(a - b, path),
                Mul => float(a * b, path),
                Div | Rem if b == 0.0 => {
                    fail(ExprErrorKind::NumericError, path, "division by zero")
                }
                Div => float(a / b, path),
                Rem => float(a % b, path),
                Lt => Ok(Value::Bool(a < b)),
                Le => Ok(Value::Bool(a <= b)),
                Gt => Ok(Value::Bool(a > b)),
                Ge => Ok(Value::Bool(a >= b)),
                _ => fail(ExprErrorKind::TypeError, path, "numeric operator"),
            }
        }
        _ => fail(ExprErrorKind::TypeError, path, "binary operands"),
    }
}
fn function(name: &str, args: Vec<Value>, path: &str) -> Result<Value, ExprError> {
    match (name, args.as_slice()) {
        ("len", [Value::Str(v)]) => i64::try_from(v.chars().count())
            .map(Value::Int)
            .map_err(|_| ExprError::new(ExprErrorKind::NumericError, path, "length overflow")),
        ("len", [Value::List { items, .. }]) => i64::try_from(items.len())
            .map(Value::Int)
            .map_err(|_| ExprError::new(ExprErrorKind::NumericError, path, "length overflow")),
        ("contains", [Value::Str(a), Value::Str(b)]) => Ok(Value::Bool(a.contains(b))),
        ("starts_with", [Value::Str(a), Value::Str(b)]) => Ok(Value::Bool(a.starts_with(b))),
        ("ends_with", [Value::Str(a), Value::Str(b)]) => Ok(Value::Bool(a.ends_with(b))),
        ("abs", [Value::Int(v)]) => v
            .checked_abs()
            .map(Value::Int)
            .ok_or_else(|| ExprError::new(ExprErrorKind::NumericError, path, "abs overflow")),
        ("abs", [Value::Float(v)]) => float(v.get().abs(), path),
        ("min" | "max", [first, rest @ ..]) => {
            let mut best = first.clone();
            for item in rest {
                if matches!(
                    binary(
                        if name == "min" {
                            BinaryOp::Lt
                        } else {
                            BinaryOp::Gt
                        },
                        item.clone(),
                        best.clone(),
                        path
                    )?,
                    Value::Bool(true)
                ) {
                    best = item.clone();
                }
            }
            Ok(best)
        }
        ("is_none", [Value::Option { value, .. }]) => Ok(Value::Bool(value.is_none())),
        ("is_some", [Value::Option { value, .. }]) => Ok(Value::Bool(value.is_some())),
        (
            "unwrap",
            [
                Value::Option {
                    value: Some(value), ..
                },
            ],
        ) => Ok(*value.clone()),
        ("unwrap", [Value::Option { value: None, .. }]) => {
            fail(ExprErrorKind::UnwrapNone, path, "cannot unwrap None")
        }
        ("image_from_frame", [Value::Frame(frame)]) => crate::vision::Image::from_frame(frame)
            .map(Value::Image)
            .map_err(|e| ExprError::new(ExprErrorKind::InvalidArgument, path, e.to_string())),
        _ => fail(ExprErrorKind::TypeError, path, "function arguments"),
    }
}
