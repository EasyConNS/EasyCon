//! Adapter over the existing closed primitive catalogue, not a second capability runtime.
use super::{ExprError, ExprErrorKind, Finite, SourceKind, SymbolKind, Type, Value};
use crate::{
    meta::{LogLevel, LogValue},
    primitives::{
        ImageInput, OutputType, PrimitiveCall, PrimitiveError, PrimitiveId, PrimitiveOutput,
    },
    run::RunScope,
    vision::{MatchConfig, OcrBackendKind, Rect, TemplateMode},
};
use easycon_device::{Button, Hat, Report, Stick, StickId};
use std::{collections::BTreeMap, sync::Arc};

pub(super) use super::metadata::rect_type;
use super::metadata::{frame_source_type, fs, opt, option, record, rv, val_record};

pub(super) type Args = BTreeMap<String, Value>;
fn stick_type() -> Type {
    record([("x", Type::Int), ("y", Type::Int)])
}
fn triple(t: Type) -> Type {
    Type::Tuple(vec![t.clone(), t.clone(), t])
}
pub(super) fn input_type(word: &str, actual: &Type) -> Option<Type> {
    Some(match word {
        "VideoSourceRef" => Type::Source(SourceKind::Video),
        "AudioSourceRef" => Type::Source(SourceKind::Audio),
        "GamepadRef" => Type::Source(SourceKind::Gamepad),
        "TextTargetRef" => Type::Source(SourceKind::TextInput),
        "MatcherRef" => Type::Source(SourceKind::TemplateMatcher),
        "OcrRef" => Type::Source(SourceKind::Ocr),
        "OcrKind" => Type::Symbol(SymbolKind::OcrKind),
        "FrameOrImageRef" => match actual {
            Type::Frame | Type::Image => actual.clone(),
            _ => return None,
        },
        "ImageRef" => Type::Image,
        "AudioWindowRef" => Type::AudioWindow,
        "Option<SampleId>" => option(Type::SampleId),
        "Option<Rect>" => option(rect_type()),
        "Int>0" | "Int>=0" => Type::Int,
        "Str" => Type::Str,
        "NonEmptyList<Button>" => Type::List(Box::new(Type::Symbol(SymbolKind::Button))),
        "Hat" => Type::Symbol(SymbolKind::Hat),
        "StickId" => Type::Symbol(SymbolKind::StickId),
        "Stick" => stick_type(),
        "LogLevel" => Type::Symbol(SymbolKind::LogLevel),
        "MatchConfig" => record([
            ("mode", Type::Symbol(SymbolKind::TemplateMode)),
            ("region", option(rect_type())),
        ]),
        "Report" => record([
            ("buttons", Type::Int),
            ("hat", Type::Symbol(SymbolKind::Hat)),
            ("left", stick_type()),
            ("right", stick_type()),
        ]),
        "LogFields" => match actual {
            Type::Record(fields)
                if fields.values().all(|t| {
                    matches!(
                        t,
                        Type::Unit | Type::Bool | Type::Int | Type::Float | Type::Str
                    )
                }) =>
            {
                actual.clone()
            }
            _ => return None,
        },
        _ => return None,
    })
}
pub(super) fn output_type(id: PrimitiveId) -> Type {
    match id.contract().output {
        OutputType::Unit => Type::Unit,
        OutputType::Int => Type::Int,
        OutputType::Float => Type::Float,
        OutputType::Frame => Type::Frame,
        OutputType::AudioWindow => Type::AudioWindow,
        OutputType::InputReceipt => Type::InputReceipt,
        OutputType::Match => record([
            ("source", option(frame_source_type())),
            ("search", rect_type()),
            ("bounds", rect_type()),
            ("mode", Type::Symbol(SymbolKind::TemplateMode)),
            ("raw_score", Type::Float),
            ("confidence", option(Type::Float)),
            ("implementation", Type::Str),
        ]),
        OutputType::OcrResult => record([
            ("source", option(frame_source_type())),
            ("region", rect_type()),
            ("kind", Type::Symbol(SymbolKind::OcrKind)),
            ("text", Type::Str),
            ("confidence", option(Type::Float)),
            ("profile", Type::Str),
            ("boxes", Type::List(Box::new(box_type()))),
        ]),
        OutputType::RegionStatistics => record([
            ("implementation", Type::Str),
            ("source", option(frame_source_type())),
            ("region", rect_type()),
            ("pixel_count", Type::Int),
            ("rgb_min", triple(Type::Int)),
            ("rgb_max", triple(Type::Int)),
            ("rgb_mean", triple(Type::Float)),
        ]),
        OutputType::ImageDifference => record([
            ("implementation", Type::Str),
            ("left_source", option(frame_source_type())),
            ("right_source", option(frame_source_type())),
            ("left_region", rect_type()),
            ("right_region", rect_type()),
            ("pixel_count", Type::Int),
            ("changed_pixels", Type::Int),
            ("mean_absolute_error", Type::Float),
            ("root_mean_square_error", Type::Float),
            ("max_channel_delta", Type::Int),
        ]),
    }
}
fn box_type() -> Type {
    record([
        ("text", Type::Str),
        ("confidence", option(Type::Float)),
        (
            "polygon",
            Type::Tuple(vec![record([("x", Type::Float), ("y", Type::Float)]); 4]),
        ),
    ])
}
fn invalid(message: impl Into<String>) -> ExprError {
    ExprError::new(ExprErrorKind::InvalidArgument, "args", message)
}
fn get<'a>(args: &'a Args, name: &str) -> Result<&'a Value, ExprError> {
    args.get(name)
        .ok_or_else(|| invalid(format!("missing argument {name}")))
}
fn int(value: &Value) -> Result<i64, ExprError> {
    if let Value::Int(v) = value {
        Ok(*v)
    } else {
        Err(invalid("expected Int"))
    }
}
fn range(value: &Value, max: i64) -> Result<i64, ExprError> {
    let v = int(value)?;
    if (0..=max).contains(&v) {
        Ok(v)
    } else {
        Err(invalid(format!("integer outside 0..={max}")))
    }
}
fn fields(value: &Value) -> Result<&Args, ExprError> {
    if let Value::Record(v) = value {
        Ok(v)
    } else {
        Err(invalid("expected Record"))
    }
}
fn symbol(value: &Value) -> Result<&str, ExprError> {
    if let Value::Symbol(v) = value {
        Ok(v.name())
    } else {
        Err(invalid("expected Symbol"))
    }
}
fn source(value: &Value) -> Result<&str, ExprError> {
    if let Value::Source(v) = value {
        Ok(&v.binding)
    } else {
        Err(invalid("expected Source"))
    }
}
fn string(value: &Value) -> Result<&str, ExprError> {
    if let Value::Str(v) = value {
        Ok(v)
    } else {
        Err(invalid("expected Str"))
    }
}
fn image(value: &Value) -> Result<&crate::vision::Image, ExprError> {
    if let Value::Image(v) = value {
        Ok(v)
    } else {
        Err(invalid("expected Image"))
    }
}
fn rect(value: &Value) -> Result<Rect, ExprError> {
    let f = fields(value)?;
    let r = Rect {
        x: range(get(f, "x")?, u32::MAX.into())? as u32,
        y: range(get(f, "y")?, u32::MAX.into())? as u32,
        width: range(get(f, "width")?, u32::MAX.into())? as u32,
        height: range(get(f, "height")?, u32::MAX.into())? as u32,
    };
    if r.width == 0
        || r.height == 0
        || r.x.checked_add(r.width).is_none()
        || r.y.checked_add(r.height).is_none()
    {
        return Err(invalid(
            "nonempty Rect without coordinate overflow required",
        ));
    }
    Ok(r)
}
fn optional_rect(value: &Value) -> Result<Option<Rect>, ExprError> {
    if let Value::Option { value, .. } = value {
        value.as_deref().map(rect).transpose()
    } else {
        Err(invalid("expected Option<Rect>"))
    }
}
fn stick(value: &Value) -> Result<Stick, ExprError> {
    let f = fields(value)?;
    Ok(Stick {
        x: range(get(f, "x")?, 255)? as u8,
        y: range(get(f, "y")?, 255)? as u8,
    })
}
fn hat(value: &Value) -> Result<Hat, ExprError> {
    Ok(match symbol(value)? {
        "Up" => Hat::Up,
        "UpRight" => Hat::UpRight,
        "Right" => Hat::Right,
        "DownRight" => Hat::DownRight,
        "Down" => Hat::Down,
        "DownLeft" => Hat::DownLeft,
        "Left" => Hat::Left,
        "UpLeft" => Hat::UpLeft,
        "Center" => Hat::Center,
        _ => return Err(invalid("invalid Hat")),
    })
}
fn stick_id(value: &Value) -> Result<StickId, ExprError> {
    match symbol(value)? {
        "Left" => Ok(StickId::Left),
        "Right" => Ok(StickId::Right),
        _ => Err(invalid("invalid StickId")),
    }
}
fn mode(value: &Value) -> Result<TemplateMode, ExprError> {
    Ok(match symbol(value)? {
        "SqDiff" => TemplateMode::SqDiff,
        "SqDiffNormed" => TemplateMode::SqDiffNormed,
        "CCorr" => TemplateMode::CCorr,
        "CCorrNormed" => TemplateMode::CCorrNormed,
        "CCoeff" => TemplateMode::CCoeff,
        "CCoeffNormed" => TemplateMode::CCoeffNormed,
        _ => return Err(invalid("invalid TemplateMode")),
    })
}
fn buttons(value: &Value) -> Result<Vec<Button>, ExprError> {
    let Value::List { items, .. } = value else {
        return Err(invalid("expected buttons List"));
    };
    if items.is_empty() {
        return Err(invalid("buttons must be nonempty"));
    }
    items
        .iter()
        .map(|v| {
            Ok(match symbol(v)? {
                "Y" => Button::Y,
                "B" => Button::B,
                "A" => Button::A,
                "X" => Button::X,
                "L" => Button::L,
                "R" => Button::R,
                "ZL" => Button::ZL,
                "ZR" => Button::ZR,
                "Minus" => Button::Minus,
                "Plus" => Button::Plus,
                "LeftStick" => Button::LeftStick,
                "RightStick" => Button::RightStick,
                "Home" => Button::Home,
                "Capture" => Button::Capture,
                _ => return Err(invalid("invalid Button")),
            })
        })
        .collect()
}
fn report(value: &Value) -> Result<Report, ExprError> {
    let f = fields(value)?;
    Ok(Report {
        buttons: range(get(f, "buttons")?, 0x3fff)? as u16,
        hat: hat(get(f, "hat")?)?,
        left: stick(get(f, "left")?)?,
        right: stick(get(f, "right")?)?,
    })
}
/// Partial constant arguments are checked without requiring runtime references.
pub(super) fn constraints(id: PrimitiveId, args: &Args) -> Result<(), ExprError> {
    for (name, value) in args {
        match name.as_str() {
            "duration_ms" if int(value)? < 0 => {
                return Err(invalid("duration_ms must be nonnegative"));
            }
            "frames" if int(value)? <= 0 || usize::try_from(int(value)?).is_err() => {
                return Err(invalid("frames must be positive and fit usize"));
            }
            "buttons" => {
                buttons(value)?;
            }
            "region" | "left_region" | "right_region" => {
                optional_rect(value)?;
            }
            "position" => {
                stick(value)?;
            }
            "report" => {
                report(value)?;
            }
            "config" if id == PrimitiveId::MatchTemplate => {
                optional_rect(get(fields(value)?, "region")?)?;
            }
            _ => {}
        }
    }
    Ok(())
}

pub(super) async fn execute(
    id: PrimitiveId,
    args: &Args,
    run: &mut RunScope<'_>,
    token: &tokio_util::sync::CancellationToken,
) -> Result<PrimitiveOutput, PrimitiveInvokeError> {
    constraints(id, args)?;
    let arg = |n| get(args, n);
    let selected = if matches!(id, PrimitiveId::PadPress | PrimitiveId::PadRelease) {
        buttons(arg("buttons")?)?
    } else {
        vec![]
    };
    let call = match id {
        PrimitiveId::Capture => PrimitiveCall::Capture {
            source: source(arg("source")?)?,
            after: match arg("after")? {
                Value::Option { value: None, .. } => None,
                Value::Option { value: Some(v), .. } => {
                    if let Value::SampleId(id) = &**v {
                        Some(id)
                    } else {
                        return Err(invalid("after SampleId").into());
                    }
                }
                _ => return Err(invalid("after Option").into()),
            },
        },
        PrimitiveId::ReadAudio => PrimitiveCall::ReadAudio {
            source: source(arg("source")?)?,
            frames: int(arg("frames")?)?,
        },
        PrimitiveId::RegionStatistics => PrimitiveCall::RegionStatistics {
            image: image(arg("image")?)?,
            region: optional_rect(arg("region")?)?,
        },
        PrimitiveId::ImageDifference => PrimitiveCall::ImageDifference {
            left: image(arg("left")?)?,
            right: image(arg("right")?)?,
            left_region: optional_rect(arg("left_region")?)?,
            right_region: optional_rect(arg("right_region")?)?,
        },
        PrimitiveId::MatchTemplate => {
            let config = fields(arg("config")?)?;
            PrimitiveCall::MatchTemplate {
                matcher: source(arg("matcher")?)?,
                image: image(arg("image")?)?,
                template: image(arg("template")?)?,
                config: MatchConfig {
                    mode: mode(get(config, "mode")?)?,
                    region: optional_rect(get(config, "region")?)?,
                },
            }
        }
        PrimitiveId::Ocr => PrimitiveCall::Ocr {
            kind: match symbol(arg("kind")?)? {
                "Tesseract" => OcrBackendKind::Tesseract,
                "PpOcr" => OcrBackendKind::PpOcr,
                _ => return Err(invalid("invalid OcrKind").into()),
            },
            engine: source(arg("engine")?)?,
            image: match arg("image")? {
                Value::Frame(frame) => ImageInput::Frame(frame),
                Value::Image(image) => ImageInput::Image(image),
                _ => return Err(invalid("expected Frame or Image").into()),
            },
            region: optional_rect(arg("region")?)?,
        },
        PrimitiveId::AudioRms => PrimitiveCall::AudioRms {
            window: if let Value::AudioWindow(v) = arg("window")? {
                v
            } else {
                return Err(invalid("window AudioWindow").into());
            },
        },
        PrimitiveId::PadPress => PrimitiveCall::PadPress {
            target: source(arg("target")?)?,
            buttons: &selected,
        },
        PrimitiveId::PadRelease => PrimitiveCall::PadRelease {
            target: source(arg("target")?)?,
            buttons: &selected,
        },
        PrimitiveId::PadSetHat => PrimitiveCall::PadSetHat {
            target: source(arg("target")?)?,
            hat: hat(arg("hat")?)?,
        },
        PrimitiveId::PadSetStick => PrimitiveCall::PadSetStick {
            target: source(arg("target")?)?,
            stick: stick_id(arg("stick")?)?,
            position: stick(arg("position")?)?,
        },
        PrimitiveId::PadCenterStick => PrimitiveCall::PadCenterStick {
            target: source(arg("target")?)?,
            stick: stick_id(arg("stick")?)?,
        },
        PrimitiveId::PadApplyReport => PrimitiveCall::PadApplyReport {
            target: source(arg("target")?)?,
            report: report(arg("report")?)?,
        },
        PrimitiveId::PadReset => PrimitiveCall::PadReset {
            target: source(arg("target")?)?,
        },
        PrimitiveId::TypeText => PrimitiveCall::TypeText {
            target: source(arg("target")?)?,
            text: string(arg("text")?)?,
        },
        PrimitiveId::Sleep => PrimitiveCall::Sleep {
            duration_ms: int(arg("duration_ms")?)?,
        },
        PrimitiveId::ClockNow => PrimitiveCall::ClockNow,
        PrimitiveId::Log => {
            let level = match symbol(arg("level")?)? {
                "Debug" => LogLevel::Debug,
                "Info" => LogLevel::Info,
                "Warn" => LogLevel::Warn,
                "Error" => LogLevel::Error,
                _ => return Err(invalid("log level").into()),
            };
            let fields = fields(arg("fields")?)?
                .iter()
                .map(|(k, v)| {
                    Ok((
                        k.clone(),
                        match v {
                            Value::Unit => LogValue::Null,
                            Value::Bool(v) => LogValue::Bool(*v),
                            Value::Int(v) => LogValue::Int(*v),
                            Value::Float(v) => LogValue::Float(v.get()),
                            Value::Str(v) => LogValue::Str(v.clone()),
                            _ => return Err(invalid("flat log fields required")),
                        },
                    ))
                })
                .collect::<Result<_, ExprError>>()?;
            PrimitiveCall::Log {
                level,
                message: string(arg("message")?)?,
                fields,
            }
        }
    };
    run.execute_in(call, token)
        .await
        .map_err(PrimitiveInvokeError::Primitive)
}
pub(super) enum PrimitiveInvokeError {
    Expr(ExprError),
    Primitive(Arc<PrimitiveError>),
}
impl From<ExprError> for PrimitiveInvokeError {
    fn from(e: ExprError) -> Self {
        Self::Expr(e)
    }
}
fn fv(v: f64) -> Result<Value, ExprError> {
    Finite::new(v)
        .map(Value::Float)
        .ok_or_else(|| invalid("provider returned non-finite Float"))
}
fn count(v: u64) -> Result<Value, ExprError> {
    i64::try_from(v)
        .map(Value::Int)
        .map_err(|_| invalid("provider count exceeds Int"))
}
pub(super) fn output(output: PrimitiveOutput) -> Result<Value, ExprError> {
    Ok(match output {
        PrimitiveOutput::Unit => Value::Unit,
        PrimitiveOutput::Int(v) => Value::Int(v),
        PrimitiveOutput::Float(v) => fv(v)?,
        PrimitiveOutput::Frame(v) => Value::Frame(v),
        PrimitiveOutput::AudioWindow(v) => Value::AudioWindow(v),
        PrimitiveOutput::InputReceipt(v) => Value::InputReceipt(v),
        PrimitiveOutput::Match(v) => val_record([
            ("source", fs(v.source)),
            ("search", rv(v.search)),
            ("bounds", rv(v.bounds)),
            (
                "mode",
                Value::Symbol(
                    super::Symbol::new(SymbolKind::TemplateMode, format!("{:?}", v.mode))
                        .ok_or_else(|| invalid("template mode"))?,
                ),
            ),
            ("raw_score", fv(v.raw_score)?),
            (
                "confidence",
                opt(Type::Float, v.confidence.map(fv).transpose()?),
            ),
            ("implementation", Value::Str(v.implementation)),
        ]),
        PrimitiveOutput::Ocr(v) => {
            let confidence = v.confidence();
            let boxes = v
                .boxes
                .into_iter()
                .map(|s| {
                    Ok(val_record([
                        ("text", Value::Str(s.text)),
                        (
                            "confidence",
                            opt(Type::Float, s.confidence.map(|v| fv(v.into())).transpose()?),
                        ),
                        (
                            "polygon",
                            Value::Tuple(
                                s.polygon
                                    .into_iter()
                                    .map(|p| {
                                        Ok(val_record([
                                            ("x", fv(p.x.into())?),
                                            ("y", fv(p.y.into())?),
                                        ]))
                                    })
                                    .collect::<Result<_, ExprError>>()?,
                            ),
                        ),
                    ]))
                })
                .collect::<Result<_, ExprError>>()?;
            val_record([
                ("source", fs(v.source)),
                ("region", rv(v.region)),
                (
                    "kind",
                    Value::Symbol(
                        super::Symbol::new(SymbolKind::OcrKind, format!("{:?}", v.kind))
                            .ok_or_else(|| invalid("OCR kind"))?,
                    ),
                ),
                ("text", Value::Str(v.text)),
                (
                    "confidence",
                    opt(Type::Float, confidence.map(|v| fv(v.into())).transpose()?),
                ),
                ("profile", Value::Str(v.profile)),
                (
                    "boxes",
                    Value::List {
                        element: box_type(),
                        items: boxes,
                    },
                ),
            ])
        }
        PrimitiveOutput::RegionStatistics(v) => val_record([
            ("implementation", Value::Str(v.implementation.into())),
            ("source", fs(v.source)),
            ("region", rv(v.region)),
            ("pixel_count", count(v.pixel_count)?),
            (
                "rgb_min",
                Value::Tuple(
                    v.rgb_min
                        .into_iter()
                        .map(|v| Value::Int(v.into()))
                        .collect(),
                ),
            ),
            (
                "rgb_max",
                Value::Tuple(
                    v.rgb_max
                        .into_iter()
                        .map(|v| Value::Int(v.into()))
                        .collect(),
                ),
            ),
            (
                "rgb_mean",
                Value::Tuple(v.rgb_mean.into_iter().map(fv).collect::<Result<_, _>>()?),
            ),
        ]),
        PrimitiveOutput::ImageDifference(v) => val_record([
            ("implementation", Value::Str(v.implementation.into())),
            ("left_source", fs(v.left_source)),
            ("right_source", fs(v.right_source)),
            ("left_region", rv(v.left_region)),
            ("right_region", rv(v.right_region)),
            ("pixel_count", count(v.pixel_count)?),
            ("changed_pixels", count(v.changed_pixels)?),
            ("mean_absolute_error", fv(v.mean_absolute_error)?),
            ("root_mean_square_error", fv(v.root_mean_square_error)?),
            ("max_channel_delta", Value::Int(v.max_channel_delta.into())),
        ]),
    })
}
