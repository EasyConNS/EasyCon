use super::metadata;
use crate::{
    SampleId, audio::AudioWindow, primitives::InputReceipt, video::VideoFrame, vision::Image,
};
use serde::{Deserialize, Deserializer, Serialize};
use std::{collections::BTreeMap, sync::Arc};

/// Constructor and JSON decoding reject non-finite values and normalize negative zero.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(transparent)]
pub struct Finite(f64);
impl Finite {
    pub fn new(value: f64) -> Option<Self> {
        value
            .is_finite()
            .then_some(Self(if value == 0.0 { 0.0 } else { value }))
    }
    pub fn get(self) -> f64 {
        self.0
    }
}
impl<'de> Deserialize<'de> for Finite {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        Self::new(f64::deserialize(deserializer)?)
            .ok_or_else(|| serde::de::Error::custom("Float must be finite"))
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum SourceKind {
    Video,
    Audio,
    Gamepad,
    TextInput,
    TemplateMatcher,
    Ocr,
}
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SourceRef {
    pub kind: SourceKind,
    pub binding: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum SymbolKind {
    Button,
    Hat,
    StickId,
    TemplateMode,
    OcrKind,
    LogLevel,
}
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(try_from = "SymbolData", into = "SymbolData")]
pub struct Symbol {
    kind: SymbolKind,
    name: String,
}
#[derive(Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct SymbolData {
    kind: SymbolKind,
    name: String,
}
impl Symbol {
    pub fn new(kind: SymbolKind, name: impl Into<String>) -> Option<Self> {
        let name = name.into();
        let names: &[&str] = match kind {
            SymbolKind::Button => &[
                "Y",
                "B",
                "A",
                "X",
                "L",
                "R",
                "ZL",
                "ZR",
                "Minus",
                "Plus",
                "LeftStick",
                "RightStick",
                "Home",
                "Capture",
            ],
            SymbolKind::Hat => &[
                "Up",
                "UpRight",
                "Right",
                "DownRight",
                "Down",
                "DownLeft",
                "Left",
                "UpLeft",
                "Center",
            ],
            SymbolKind::StickId => &["Left", "Right"],
            SymbolKind::TemplateMode => &[
                "SqDiff",
                "SqDiffNormed",
                "CCorr",
                "CCorrNormed",
                "CCoeff",
                "CCoeffNormed",
            ],
            SymbolKind::LogLevel => &["Debug", "Info", "Warn", "Error"],
            SymbolKind::OcrKind => &["Tesseract", "PpOcr"],
        };
        names
            .contains(&name.as_str())
            .then_some(Self { kind, name })
    }
    pub fn kind(&self) -> SymbolKind {
        self.kind
    }
    pub fn name(&self) -> &str {
        &self.name
    }
}
impl TryFrom<SymbolData> for Symbol {
    type Error = &'static str;
    fn try_from(value: SymbolData) -> Result<Self, Self::Error> {
        Self::new(value.kind, value.name).ok_or("unknown typed symbol")
    }
}
impl From<Symbol> for SymbolData {
    fn from(value: Symbol) -> Self {
        Self {
            kind: value.kind,
            name: value.name,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct FlowSignature {
    #[serde(deserialize_with = "super::unique_map")]
    pub params: BTreeMap<String, Type>,
    pub output: Box<Type>,
    pub effects: super::Effects,
}
/// Identity is minted by a validated document, never by deserializing a runtime value.
#[derive(Clone, Debug)]
pub struct FlowRef {
    pub(super) document_id: u64,
    pub(super) definition: String,
    pub(super) signature: FlowSignature,
}
impl FlowRef {
    pub fn definition(&self) -> &str {
        &self.definition
    }
    pub fn signature(&self) -> &FlowSignature {
        &self.signature
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(
    tag = "type",
    content = "of",
    rename_all = "snake_case",
    deny_unknown_fields
)]
pub enum Type {
    Unit,
    Bool,
    Int,
    Float,
    Str,
    List(Box<Type>),
    Tuple(Vec<Type>),
    Record(#[serde(deserialize_with = "super::unique_map")] BTreeMap<String, Type>),
    Option(Box<Type>),
    Source(SourceKind),
    Symbol(SymbolKind),
    Frame,
    AudioWindow,
    Image,
    SampleId,
    InputReceipt,
    ErrorRecord,
    FlowRef(FlowSignature),
}
impl Type {
    /// Flow signatures allow a conservative effect bound; all other shapes remain explicit.
    pub fn accepts(&self, actual: &Self) -> bool {
        match (self, actual) {
            (Self::FlowRef(a), Self::FlowRef(b)) => {
                a.params == b.params
                    && a.output == b.output
                    && (!b.effects.effectful || a.effects.effectful)
                    && (!b.effects.meta || a.effects.meta)
            }
            (Self::List(a), Self::List(b)) | (Self::Option(a), Self::Option(b)) => a.accepts(b),
            (Self::Tuple(a), Self::Tuple(b)) => {
                a.len() == b.len() && a.iter().zip(b).all(|(a, b)| a.accepts(b))
            }
            (Self::Record(a), Self::Record(b)) => {
                a.len() == b.len()
                    && a.iter()
                        .all(|(k, a)| b.get(k).is_some_and(|b| a.accepts(b)))
            }
            _ => self == actual,
        }
    }
    pub fn comparable(&self) -> bool {
        match self {
            Self::Unit | Self::Bool | Self::Int | Self::Float | Self::Str | Self::Symbol(_) => true,
            Self::List(t) | Self::Option(t) => t.comparable(),
            Self::Tuple(ts) => ts.iter().all(Self::comparable),
            Self::Record(ts) => ts.values().all(Self::comparable),
            _ => false,
        }
    }
    pub fn field(&self, name: &str) -> Option<Type> {
        match self {
            Self::ErrorRecord => match name {
                "kind" | "message" | "origin" | "recovery" => Some(Self::Str),
                "node_id" | "invocation_id" | "cancel_reason" | "expression_kind"
                | "expression_path" | "primitive_name" => Some(Self::Option(Box::new(Self::Str))),
                "primitive_version" => Some(Self::Option(Box::new(Self::Int))),
                "cause" => Some(Self::Option(Box::new(Self::ErrorRecord))),
                "suppressed" => Some(Self::List(Box::new(Self::ErrorRecord))),
                _ => None,
            },
            Self::Record(fields) => fields.get(name).cloned(),
            Self::Frame => match name {
                "id" => Some(Self::SampleId),
                "width" | "height" => Some(Self::Int),
                "time" => Some(metadata::sample_time_type()),
                "format" => Some(metadata::video_format_type()),
                _ => None,
            },
            Self::AudioWindow => match name {
                "id" => Some(Self::SampleId),
                "frames" | "channels" | "sample_rate" => Some(Self::Int),
                "time" => Some(metadata::sample_time_type()),
                _ => None,
            },
            Self::Image => match name {
                "width" | "height" => Some(Self::Int),
                "bounds" => Some(metadata::rect_type()),
                "source" => Some(metadata::option(metadata::frame_source_type())),
                _ => None,
            },
            Self::SampleId => match name {
                "source" | "session" | "sequence" => Some(Self::Str),
                _ => None,
            },
            Self::InputReceipt => match name {
                "session" | "sequence" | "delivery" => Some(Self::Str),
                _ => None,
            },
            _ => None,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(
    tag = "type",
    content = "value",
    rename_all = "snake_case",
    deny_unknown_fields
)]
pub enum Literal {
    Unit,
    Bool(bool),
    Int(#[serde(with = "decimal")] i64),
    Float(Finite),
    Str(String),
    Source(SourceRef),
    Symbol(Symbol),
}
mod decimal {
    use serde::{Deserialize, Deserializer, Serializer};
    pub fn serialize<S: Serializer>(value: &i64, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(&value.to_string())
    }
    pub fn deserialize<'de, D: Deserializer<'de>>(deserializer: D) -> Result<i64, D::Error> {
        let text = String::deserialize(deserializer)?;
        let value = text.parse::<i64>().map_err(serde::de::Error::custom)?;
        if value.to_string() != text {
            return Err(serde::de::Error::custom(
                "Int requires canonical decimal string",
            ));
        }
        Ok(value)
    }
}

/// Runtime values are deliberately not serializable. Native references never enter JSON IR.
#[derive(Clone, Debug)]
pub enum Value {
    Unit,
    Bool(bool),
    Int(i64),
    Float(Finite),
    Str(String),
    List {
        element: Type,
        items: Vec<Value>,
    },
    Tuple(Vec<Value>),
    Record(BTreeMap<String, Value>),
    Option {
        element: Type,
        value: Option<Box<Value>>,
    },
    Source(SourceRef),
    Symbol(Symbol),
    Frame(Arc<VideoFrame>),
    AudioWindow(Arc<AudioWindow>),
    Image(Image),
    SampleId(SampleId),
    InputReceipt(InputReceipt),
    ErrorRecord(Arc<super::FlowError>),
    FlowRef(FlowRef),
}
impl From<&Literal> for Value {
    fn from(value: &Literal) -> Self {
        match value {
            Literal::Unit => Self::Unit,
            Literal::Bool(v) => Self::Bool(*v),
            Literal::Int(v) => Self::Int(*v),
            Literal::Float(v) => Self::Float(*v),
            Literal::Str(v) => Self::Str(v.clone()),
            Literal::Source(v) => Self::Source(v.clone()),
            Literal::Symbol(v) => Self::Symbol(v.clone()),
        }
    }
}
impl Value {
    pub fn ty(&self) -> Type {
        match self {
            Self::Unit => Type::Unit,
            Self::Bool(_) => Type::Bool,
            Self::Int(_) => Type::Int,
            Self::Float(_) => Type::Float,
            Self::Str(_) => Type::Str,
            Self::List { element, .. } => Type::List(Box::new(element.clone())),
            Self::Tuple(items) => Type::Tuple(items.iter().map(Self::ty).collect()),
            Self::Record(fields) => {
                Type::Record(fields.iter().map(|(k, v)| (k.clone(), v.ty())).collect())
            }
            Self::Option { element, .. } => Type::Option(Box::new(element.clone())),
            Self::Source(s) => Type::Source(s.kind),
            Self::Symbol(s) => Type::Symbol(s.kind),
            Self::Frame(_) => Type::Frame,
            Self::AudioWindow(_) => Type::AudioWindow,
            Self::Image(_) => Type::Image,
            Self::SampleId(_) => Type::SampleId,
            Self::InputReceipt(_) => Type::InputReceipt,
            Self::ErrorRecord(_) => Type::ErrorRecord,
            Self::FlowRef(r) => Type::FlowRef(r.signature.clone()),
        }
    }
    pub(crate) fn valid(&self) -> bool {
        self.valid_at(0)
    }
    fn valid_at(&self, depth: usize) -> bool {
        if depth >= 128 {
            return false;
        }
        match self {
            Self::List { element, items } => items
                .iter()
                .all(|v| v.valid_at(depth + 1) && element.accepts(&v.ty())),
            Self::Option { element, value } => value
                .as_ref()
                .is_none_or(|v| v.valid_at(depth + 1) && element.accepts(&v.ty())),
            Self::Tuple(items) => items.iter().all(|v| v.valid_at(depth + 1)),
            Self::Record(fields) => fields.values().all(|v| v.valid_at(depth + 1)),
            Self::Source(s) => !s.binding.trim().is_empty(),
            _ => true,
        }
    }
    pub(crate) fn field(&self, name: &str) -> Option<Self> {
        match self {
            Self::ErrorRecord(error) => error.field(name),
            Self::Record(fields) => fields.get(name).cloned(),
            Self::Frame(frame) => match name {
                "id" => Some(Self::SampleId(frame.id.clone())),
                "width" => Some(Self::Int(frame.format.width.into())),
                "height" => Some(Self::Int(frame.format.height.into())),
                "time" => Some(metadata::sample_time(frame.time)),
                "format" => Some(metadata::video_format(&frame.format)),
                _ => None,
            },
            Self::AudioWindow(window) => match name {
                "id" => Some(Self::SampleId(window.id.clone())),
                "frames" => i64::try_from(window.frames()).ok().map(Self::Int),
                "channels" => Some(Self::Int(window.channels.into())),
                "sample_rate" => Some(Self::Int(window.sample_rate.into())),
                "time" => Some(metadata::sample_time(window.time)),
                _ => None,
            },
            Self::Image(image) => match name {
                "width" => Some(Self::Int(image.width().into())),
                "height" => Some(Self::Int(image.height().into())),
                "bounds" => Some(metadata::rv(image.bounds())),
                "source" => Some(metadata::fs(image.source().cloned())),
                _ => None,
            },
            Self::SampleId(id) => match name {
                "source" => Some(Self::Str(id.source.clone())),
                "session" => Some(Self::Str(id.session.0.to_string())),
                "sequence" => Some(Self::Str(id.sequence.to_string())),
                _ => None,
            },
            Self::InputReceipt(receipt) => match name {
                "session" => Some(Self::Str(receipt.session.to_string())),
                "sequence" => Some(Self::Str(receipt.sequence.to_string())),
                "delivery" => Some(Self::Str(format!("{:?}", receipt.delivery))),
                _ => None,
            },
            _ => None,
        }
    }
    pub(crate) fn equal(&self, other: &Self) -> bool {
        match (self, other) {
            (Self::Unit, Self::Unit) => true,
            (Self::Bool(a), Self::Bool(b)) => a == b,
            (Self::Int(a), Self::Int(b)) => a == b,
            (Self::Float(a), Self::Float(b)) => a == b,
            (Self::Str(a), Self::Str(b)) => a == b,
            (Self::Symbol(a), Self::Symbol(b)) => a == b,
            (Self::List { items: a, .. }, Self::List { items: b, .. })
            | (Self::Tuple(a), Self::Tuple(b)) => {
                a.len() == b.len() && a.iter().zip(b).all(|(a, b)| a.equal(b))
            }
            (Self::Record(a), Self::Record(b)) => {
                a.len() == b.len() && a.iter().all(|(k, v)| b.get(k).is_some_and(|b| v.equal(b)))
            }
            (Self::Option { value: a, .. }, Self::Option { value: b, .. }) => match (a, b) {
                (None, None) => true,
                (Some(a), Some(b)) => a.equal(b),
                _ => false,
            },
            _ => false,
        }
    }
}
