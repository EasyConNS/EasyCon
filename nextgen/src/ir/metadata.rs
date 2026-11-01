//! Shared field shapes for runtime samples and primitive results; no native payloads are copied.

use super::{Type, Value};
use crate::{
    SampleTime,
    video::VideoFormat,
    vision::{FrameSource, Rect},
};

pub(super) fn record(fields: impl IntoIterator<Item = (&'static str, Type)>) -> Type {
    Type::Record(fields.into_iter().map(|(k, v)| (k.into(), v)).collect())
}
pub(super) fn option(t: Type) -> Type {
    Type::Option(Box::new(t))
}
pub(super) fn val_record<const N: usize>(fields: [(&str, Value); N]) -> Value {
    Value::Record(fields.into_iter().map(|(k, v)| (k.into(), v)).collect())
}
pub(super) fn opt(ty: Type, value: Option<Value>) -> Value {
    Value::Option {
        element: ty,
        value: value.map(Box::new),
    }
}
pub(super) fn rect_type() -> Type {
    record([
        ("x", Type::Int),
        ("y", Type::Int),
        ("width", Type::Int),
        ("height", Type::Int),
    ])
}
pub(super) fn rv(r: Rect) -> Value {
    val_record([
        ("x", Value::Int(r.x.into())),
        ("y", Value::Int(r.y.into())),
        ("width", Value::Int(r.width.into())),
        ("height", Value::Int(r.height.into())),
    ])
}
pub(super) fn frame_source_type() -> Type {
    record([
        ("id", Type::SampleId),
        ("width", Type::Int),
        ("height", Type::Int),
        ("received_ns", Type::Str),
        ("source_ns", option(Type::Str)),
    ])
}
pub(super) fn fs(value: Option<FrameSource>) -> Value {
    opt(
        frame_source_type(),
        value.map(|s| {
            val_record([
                ("id", Value::SampleId(s.id)),
                ("width", Value::Int(s.width.into())),
                ("height", Value::Int(s.height.into())),
                ("received_ns", Value::Str(s.time.received_ns.to_string())),
                (
                    "source_ns",
                    opt(
                        Type::Str,
                        s.time.source_ns.map(|n| Value::Str(n.to_string())),
                    ),
                ),
            ])
        }),
    )
}
pub(super) fn sample_time_type() -> Type {
    record([("received_ns", Type::Str), ("source_ns", option(Type::Str))])
}
pub(super) fn sample_time(time: SampleTime) -> Value {
    val_record([
        ("received_ns", Value::Str(time.received_ns.to_string())),
        (
            "source_ns",
            opt(Type::Str, time.source_ns.map(|n| Value::Str(n.to_string()))),
        ),
    ])
}
pub(super) fn video_format_type() -> Type {
    record([
        ("width", Type::Int),
        ("height", Type::Int),
        ("frames_per_second", Type::Int),
        ("encoding", Type::Str),
    ])
}
pub(super) fn video_format(format: &VideoFormat) -> Value {
    val_record([
        ("width", Value::Int(format.width.into())),
        ("height", Value::Int(format.height.into())),
        (
            "frames_per_second",
            Value::Int(format.frames_per_second.into()),
        ),
        ("encoding", Value::Str(format.encoding.clone())),
    ])
}
