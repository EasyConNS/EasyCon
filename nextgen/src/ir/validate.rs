use super::{
    Backoff, Document, Expr, ExprError, Flow, FlowRef, FlowSignature, IR_VERSION, Node, ParMode,
    Type, Value, primitive,
};
use serde::{Deserialize, Serialize};
use std::sync::atomic::{AtomicU64, Ordering};
static NEXT_DOCUMENT: AtomicU64 = AtomicU64::new(1);
use crate::primitives::{Kind, PrimitiveId};
use std::collections::{BTreeMap, BTreeSet};
use thiserror::Error;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Effects {
    pub effectful: bool,
    pub meta: bool,
}
impl Effects {
    pub(super) fn include(&mut self, other: Self) {
        self.effectful |= other.effectful;
        self.meta |= other.meta;
    }
    pub fn is_pure(self) -> bool {
        !self.effectful && !self.meta
    }
}
#[derive(Clone, Debug, Error)]
#[error("InvalidFlow at {path}: {message}")]
pub struct ValidationError {
    pub node_id: Option<String>,
    pub path: String,
    pub message: String,
    pub expression: Option<Box<ExprError>>,
}
impl ValidationError {
    pub(super) fn new(path: &str, message: impl Into<String>) -> Self {
        Self {
            node_id: None,
            path: path.into(),
            message: message.into(),
            expression: None,
        }
    }
    fn expr(error: ExprError) -> Self {
        Self {
            node_id: None,
            path: error.path.clone(),
            message: error.to_string(),
            expression: Some(Box::new(error)),
        }
    }
}
/// Owns the checked AST; callers cannot mutate it after validation.
#[derive(Debug)]
pub struct ValidatedDocument {
    document: Document,
    effects: BTreeMap<String, Effects>,
    pub(super) refs: BTreeMap<String, FlowRef>,
    pub(super) id: u64,
}
impl ValidatedDocument {
    pub fn flow_ref(&self, definition: &str) -> Option<FlowRef> {
        self.refs.get(definition).cloned()
    }
    pub(super) fn owns(&self, value: &Value) -> bool {
        match value {
            Value::FlowRef(r) => {
                r.document_id == self.id
                    && self
                        .refs
                        .get(&r.definition)
                        .is_some_and(|known| known.signature == r.signature)
            }
            Value::List { items, .. } | Value::Tuple(items) => items.iter().all(|v| self.owns(v)),
            Value::Record(fields) => fields.values().all(|v| self.owns(v)),
            Value::Option { value, .. } => value.as_deref().is_none_or(|v| self.owns(v)),
            _ => true,
        }
    }

    pub fn document(&self) -> &Document {
        &self.document
    }
    pub fn effects(&self, node_id: &str) -> Option<Effects> {
        self.effects.get(node_id).copied()
    }
}
fn valid_name(name: &str) -> bool {
    !name.is_empty() && !name.starts_with('$')
}
fn bind<T>(
    env: &mut [BTreeMap<String, T>],
    name: &str,
    value: T,
    path: &str,
) -> Result<(), ValidationError> {
    if !valid_name(name) {
        return Err(ValidationError::new(
            path,
            "binding names must be nonempty and cannot start with $",
        ));
    }
    let scope = env.last_mut().expect("validator always has a scope");
    if scope.insert(name.into(), value).is_some() {
        return Err(ValidationError::new(
            path,
            format!("duplicate binding {name}"),
        ));
    }
    Ok(())
}
fn valid_type(ty: &Type, depth: usize) -> bool {
    if depth >= 128 {
        return false;
    }
    match ty {
        Type::List(t) | Type::Option(t) => valid_type(t, depth + 1),
        Type::Tuple(ts) => ts.iter().all(|t| valid_type(t, depth + 1)),
        Type::Record(ts) => ts.values().all(|t| valid_type(t, depth + 1)),
        Type::FlowRef(sig) => {
            sig.params
                .iter()
                .all(|(n, t)| valid_name(n) && valid_type(t, depth + 1))
                && valid_type(&sig.output, depth + 1)
        }
        _ => true,
    }
}
struct Validator {
    refs: BTreeMap<String, FlowRef>,
    ids: BTreeSet<String>,
    effects: BTreeMap<String, Effects>,
    constants: BTreeMap<String, Value>,
    values: BTreeMap<String, Value>,
}
impl Validator {
    fn constant(&self, e: &Expr, path: &str) -> Result<Option<Value>, ValidationError> {
        if e.known(&self.constants) {
            e.eval_with(std::slice::from_ref(&self.constants), path, &self.refs)
                .map(Some)
                .map_err(ValidationError::expr)
        } else {
            Ok(None)
        }
    }
    fn set_constant(&mut self, name: &str, value: Option<Value>) {
        if let Some(value) = value {
            self.constants.insert(name.into(), value);
        } else {
            self.constants.remove(name);
        }
    }
    fn expr(
        &self,
        e: &Expr,
        env: &[BTreeMap<String, Type>],
        path: &str,
    ) -> Result<Type, ValidationError> {
        let ty = e
            .infer_with(env, path, 0, &self.refs)
            .map_err(ValidationError::expr)?;
        if !valid_type(&ty, 0) {
            return Err(ValidationError::new(path, "type depth exceeds 128"));
        }
        self.constant(e, path)?;
        Ok(ty)
    }
    fn child(
        &mut self,
        node: &Node,
        env: &mut Vec<BTreeMap<String, Type>>,
        path: &str,
        depth: usize,
    ) -> Result<(Type, Effects), ValidationError> {
        let saved = self.constants.clone();
        env.push(BTreeMap::new());
        let result = self.node(node, env, path, depth);
        env.pop();
        self.constants = saved;
        result
    }
    fn require(
        &self,
        expr: &Expr,
        expected: &Type,
        env: &[BTreeMap<String, Type>],
        path: &str,
    ) -> Result<(), ValidationError> {
        if !expected.accepts(&self.expr(expr, env, path)?) {
            return Err(ValidationError::new(path, format!("expected {expected:?}")));
        }
        Ok(())
    }
    fn integer(
        &self,
        expr: &Expr,
        minimum: i64,
        env: &[BTreeMap<String, Type>],
        path: &str,
    ) -> Result<(), ValidationError> {
        self.require(expr, &Type::Int, env, path)?;
        if let Some(Value::Int(n)) = self.constant(expr, path)?
            && n < minimum
        {
            return Err(ValidationError::new(path, format!("must be >= {minimum}")));
        }
        Ok(())
    }
    fn backoff(
        &self,
        b: &Backoff,
        env: &[BTreeMap<String, Type>],
        path: &str,
    ) -> Result<(), ValidationError> {
        match b {
            Backoff::None => {}
            Backoff::Fixed { ms } => self.integer(ms, 0, env, path)?,
            Backoff::Exponential {
                base_ms,
                factor,
                max_ms,
            } => {
                self.integer(base_ms, 0, env, path)?;
                self.integer(max_ms, 0, env, path)?;
                self.require(factor, &Type::Float, env, path)?;
                if let Some(Value::Float(f)) = self.constant(factor, path)?
                    && f.get() < 1.0
                {
                    return Err(ValidationError::new(path, "factor must be >= 1"));
                }
                if let (Some(Value::Int(a)), Some(Value::Int(b))) =
                    (self.constant(base_ms, path)?, self.constant(max_ms, path)?)
                    && a > b
                {
                    return Err(ValidationError::new(path, "base_ms exceeds max_ms"));
                }
            }
        }
        Ok(())
    }
    fn node(
        &mut self,
        node: &Node,
        env: &mut Vec<BTreeMap<String, Type>>,
        path: &str,
        depth: usize,
    ) -> Result<(Type, Effects), ValidationError> {
        let result = self.node_inner(node, env, path, depth);
        result.map_err(|mut e| {
            if e.node_id.is_none() {
                e.node_id = Some(node.node_id.clone());
            }
            e
        })
    }
    fn node_inner(
        &mut self,
        node: &Node,
        env: &mut Vec<BTreeMap<String, Type>>,
        path: &str,
        depth: usize,
    ) -> Result<(Type, Effects), ValidationError> {
        if depth >= 128 || self.ids.len() >= 100_000 {
            return Err(ValidationError::new(path, "structural node limit exceeded"));
        }
        if node.node_id.is_empty() || !self.ids.insert(node.node_id.clone()) {
            return Err(ValidationError::new(
                path,
                "node_id must be nonempty and document-wide unique",
            ));
        }
        let mut effects = Effects::default();
        let ty = match &node.flow {
            Flow::Eval { expr } => self.expr(expr, env, &format!("{path}.expr"))?,
            Flow::Primitive {
                name,
                version,
                args,
            } => {
                let id = PrimitiveId::lookup(name, *version).ok_or_else(|| {
                    ValidationError::new(path, "unknown primitive name or version")
                })?;
                let contract = id.contract();
                effects = match contract.kind {
                    Kind::Pure => Effects::default(),
                    Kind::Effectful => Effects {
                        effectful: true,
                        meta: false,
                    },
                    Kind::Meta => Effects {
                        effectful: false,
                        meta: true,
                    },
                };
                let mut names = BTreeSet::new();
                let mut constants = BTreeMap::new();
                for (i, arg) in args.iter().enumerate() {
                    if !names.insert(arg.name.as_str()) {
                        return Err(ValidationError::new(path, "duplicate primitive argument"));
                    }
                    let param = contract
                        .inputs
                        .iter()
                        .find(|p| p.name == arg.name)
                        .ok_or_else(|| {
                            ValidationError::new(path, format!("unknown argument {}", arg.name))
                        })?;
                    let arg_path = format!("{path}.args[{i}].expr");
                    let actual = self.expr(&arg.expr, env, &arg_path)?;
                    let expected = primitive::input_type(param.ty, &actual).ok_or_else(|| {
                        ValidationError::new(
                            &arg_path,
                            format!("unsupported argument shape for {}", param.ty),
                        )
                    })?;
                    if actual != expected {
                        return Err(ValidationError::new(
                            &arg_path,
                            format!("expected {expected:?}, got {actual:?}"),
                        ));
                    }
                    if let Some(value) = self.constant(&arg.expr, &arg_path)? {
                        constants.insert(arg.name.clone(), value);
                    }
                }
                if args.len() != contract.inputs.len() {
                    return Err(ValidationError::new(
                        path,
                        "primitive arguments must match the complete contract",
                    ));
                }
                primitive::constraints(id, &constants)
                    .map_err(|e| ValidationError::new(path, e.to_string()))?;
                primitive::output_type(id)
            }
            Flow::Seq { steps } => {
                let saved = self.constants.clone();
                env.push(BTreeMap::new());
                let result = (|| {
                    let mut output = Type::Unit;
                    for (i, step) in steps.iter().enumerate() {
                        let (ty, e) =
                            self.node(step, env, &format!("{path}.steps[{i}]"), depth + 1)?;
                        output = ty;
                        effects.include(e);
                    }
                    Ok::<_, ValidationError>(output)
                })();
                env.pop();
                self.constants = saved;
                result?
            }
            Flow::Bind { name, expr, next } => {
                let ty = self.expr(expr, env, &format!("{path}.expr"))?;
                let value = self.constant(expr, &format!("{path}.expr"))?;
                bind(env, name, ty, path)?;
                self.set_constant(name, value);
                let (ty, e) = self.node(next, env, &format!("{path}.next"), depth + 1)?;
                effects.include(e);
                ty
            }
            Flow::Choose { cond, then, else_ } => {
                if self.expr(cond, env, &format!("{path}.cond"))? != Type::Bool {
                    return Err(ValidationError::new(path, "Choose condition must be Bool"));
                }
                let (ty, e) = self.child(then, env, &format!("{path}.then"), depth + 1)?;
                effects.include(e);
                let (else_ty, e) = match else_ {
                    Some(node) => self.child(node, env, &format!("{path}.else"), depth + 1)?,
                    None => (Type::Unit, Effects::default()),
                };
                effects.include(e);
                if ty != else_ty {
                    return Err(ValidationError::new(
                        path,
                        "Choose branch output types must match; absent else returns Unit",
                    ));
                }
                ty
            }
            Flow::SubflowCall { target, args } => {
                let Type::FlowRef(sig) = self.expr(target, env, &format!("{path}.target"))? else {
                    return Err(ValidationError::new(path, "call target must be FlowRef"));
                };
                let mut names = BTreeSet::new();
                for a in args {
                    if !names.insert(&a.name) {
                        return Err(ValidationError::new(path, "duplicate call argument"));
                    }
                    let expected = sig
                        .params
                        .get(&a.name)
                        .ok_or_else(|| ValidationError::new(path, "unknown call argument"))?;
                    self.require(&a.expr, expected, env, &format!("{path}.args.{}", a.name))?;
                }
                if args.len() != sig.params.len() {
                    return Err(ValidationError::new(
                        path,
                        "call arguments must match signature",
                    ));
                }
                effects.include(sig.effects);
                *sig.output
            }
            Flow::Par { mode, branches } => {
                if branches.is_empty() && *mode != ParMode::All {
                    return Err(ValidationError::new(path, "empty Any/Race is invalid"));
                }
                let mut types = vec![];
                for (i, child) in branches.iter().enumerate() {
                    let (ty, e) =
                        self.child(child, env, &format!("{path}.branches[{i}]"), depth + 1)?;
                    if e.effectful {
                        return Err(ValidationError::new(
                            path,
                            "Par branches may only contain Pure/Meta capabilities",
                        ));
                    }
                    effects.include(e);
                    types.push(ty);
                }
                if *mode == ParMode::All {
                    Type::Tuple(types)
                } else {
                    if types.iter().any(|t| t != &types[0]) {
                        return Err(ValidationError::new(
                            path,
                            "Any/Race output types must match",
                        ));
                    }
                    types.remove(0)
                }
            }
            Flow::Retry {
                body,
                max,
                retry_on,
                backoff,
            } => {
                self.integer(max, 1, env, &format!("{path}.max"))?;
                self.require(
                    retry_on,
                    &Type::List(Box::new(Type::Str)),
                    env,
                    &format!("{path}.retry_on"),
                )?;
                if let Some(v) = self.constant(retry_on, path)? {
                    super::control::retry_kinds(&v).map_err(|m| ValidationError::new(path, m))?;
                }
                self.backoff(backoff, env, &format!("{path}.backoff"))?;
                let (ty, e) = self.child(body, env, &format!("{path}.body"), depth + 1)?;
                effects.include(e);
                ty
            }
            Flow::Timeout { duration_ms, body } => {
                self.integer(duration_ms, 0, env, &format!("{path}.duration_ms"))?;
                let (ty, e) = self.child(body, env, &format!("{path}.body"), depth + 1)?;
                effects.include(e);
                ty
            }
            Flow::LoopUntil {
                body,
                cond,
                max_iters,
                state_init,
                state_next,
            } => {
                if let Some(max) = max_iters {
                    self.integer(max, 1, env, &format!("{path}.max_iters"))?;
                }
                let state = match state_init {
                    Some(e) => self.expr(e, env, &format!("{path}.state_init"))?,
                    None => Type::Unit,
                };
                let saved = self.constants.clone();
                for n in ["$state", "$iteration", "$result"] {
                    self.constants.remove(n);
                }
                env.push(BTreeMap::from([
                    ("$state".into(), state.clone()),
                    ("$iteration".into(), Type::Int),
                ]));
                let (ty, e) = self.child(body, env, &format!("{path}.body"), depth + 1)?;
                effects.include(e);
                env.last_mut().unwrap().insert("$result".into(), ty.clone());
                self.require(cond, &Type::Bool, env, &format!("{path}.cond"))?;
                if let Some(next) = state_next {
                    self.require(next, &state, env, &format!("{path}.state_next"))?;
                }
                env.pop();
                self.constants = saved;
                ty
            }
            Flow::Fallback { primary, secondary } => {
                let (ty, e) = self.child(primary, env, &format!("{path}.primary"), depth + 1)?;
                effects.include(e);
                let (other, e) =
                    self.child(secondary, env, &format!("{path}.secondary"), depth + 1)?;
                effects.include(e);
                if ty != other {
                    return Err(ValidationError::new(
                        path,
                        "Fallback output types must match",
                    ));
                }
                ty
            }
            Flow::Catch {
                body,
                bind_error,
                handler,
            } => {
                let (ty, e) = self.child(body, env, &format!("{path}.body"), depth + 1)?;
                effects.include(e);
                let saved = self.constants.clone();
                env.push(BTreeMap::new());
                if let Some(n) = bind_error {
                    bind(env, n, super::control::error_type(), path)?;
                    self.constants.remove(n);
                }
                let (other, e) = self.node(handler, env, &format!("{path}.handler"), depth + 1)?;
                env.pop();
                self.constants = saved;
                effects.include(e);
                if ty != other {
                    return Err(ValidationError::new(path, "Catch output types must match"));
                }
                ty
            }
            Flow::Pipe { source, sink } => {
                let (ty, e) = self.node(source, env, &format!("{path}.source"), depth + 1)?;
                effects.include(e);
                let saved = self.constants.clone();
                self.set_constant("$prev", self.values.get(&source.node_id).cloned());
                env.push(BTreeMap::from([("$prev".into(), ty)]));
                let result = self.node(sink, env, &format!("{path}.sink"), depth + 1);
                env.pop();
                self.constants = saved;
                let (ty, e) = result?;
                effects.include(e);
                ty
            }
        };
        let value = match &node.flow {
            Flow::Eval { expr } => self.constant(expr, &format!("{path}.expr"))?,
            Flow::Primitive { .. } => None,
            Flow::Par { .. }
            | Flow::Retry { .. }
            | Flow::Timeout { .. }
            | Flow::LoopUntil { .. }
            | Flow::Fallback { .. }
            | Flow::Catch { .. }
            | Flow::SubflowCall { .. } => None,
            Flow::Seq { steps } => {
                if let Some(last) = steps.last() {
                    self.values.get(&last.node_id).cloned()
                } else {
                    Some(Value::Unit)
                }
            }
            Flow::Bind { next, .. } => self.values.get(&next.node_id).cloned(),
            Flow::Choose { cond, then, else_ } => {
                match self.constant(cond, &format!("{path}.cond"))? {
                    Some(Value::Bool(true)) => self.values.get(&then.node_id).cloned(),
                    Some(Value::Bool(false)) => match else_ {
                        Some(node) => self.values.get(&node.node_id).cloned(),
                        None => Some(Value::Unit),
                    },
                    _ => None,
                }
            }
            Flow::Pipe { sink, .. } => self.values.get(&sink.node_id).cloned(),
        };
        if let Some(name) = &node.result_bind {
            bind(env, name, ty.clone(), path)?;
            self.set_constant(name, value.clone());
        }
        if let Some(value) = value {
            self.values.insert(node.node_id.clone(), value);
        }
        self.effects.insert(node.node_id.clone(), effects);
        Ok((ty, effects))
    }
}
pub(super) fn validate(document: Document) -> Result<ValidatedDocument, ValidationError> {
    if document.ir_version != IR_VERSION {
        return Err(ValidationError::new("ir_version", "unsupported IR version"));
    }
    if !document.definitions.contains_key(&document.entry) {
        return Err(ValidationError::new("entry", "missing entry definition"));
    }
    let id = NEXT_DOCUMENT.fetch_add(1, Ordering::Relaxed);
    let mut refs: BTreeMap<_, _> = document
        .definitions
        .iter()
        .map(|(name, d)| {
            (
                name.clone(),
                FlowRef {
                    document_id: id,
                    definition: name.clone(),
                    signature: FlowSignature {
                        params: d.params.clone(),
                        output: Box::new(d.output.clone()),
                        effects: Effects::default(),
                    },
                },
            )
        })
        .collect();
    // Effect bits only grow; finite lattice convergence also covers mutually recursive calls.
    loop {
        let mut validator = Validator {
            refs: refs.clone(),
            ids: BTreeSet::new(),
            effects: BTreeMap::new(),
            constants: BTreeMap::new(),
            values: BTreeMap::new(),
        };
        let mut changed = false;
        for (name, definition) in &document.definitions {
            if name.is_empty() {
                return Err(ValidationError::new("definitions", "empty definition id"));
            }
            validator.constants.clear();
            let path = format!("definitions.{name}");
            let mut env = vec![BTreeMap::new()];
            for (param, ty) in &definition.params {
                if !valid_type(ty, 0) {
                    return Err(ValidationError::new(&path, "invalid parameter type"));
                }
                bind(&mut env, param, ty.clone(), &path)?;
            }
            if !valid_type(&definition.output, 0) {
                return Err(ValidationError::new(&path, "invalid output type"));
            }
            let (ty, e) = validator.node(&definition.body, &mut env, &format!("{path}.body"), 0)?;
            if !definition.output.accepts(&ty) {
                return Err(ValidationError::new(
                    &path,
                    format!("declared output {:?}, got {ty:?}", definition.output),
                ));
            }
            let signature = &mut refs.get_mut(name).unwrap().signature;
            let before = signature.effects;
            signature.effects.include(e);
            changed |= before != signature.effects;
        }
        if !changed {
            return Ok(ValidatedDocument {
                document,
                effects: validator.effects,
                refs,
                id,
            });
        }
    }
}
