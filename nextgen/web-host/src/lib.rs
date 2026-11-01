//! Loopback-only browser host. Hardware stays owned by existing native workers.

use std::{path::PathBuf, sync::Arc};

use axum::{
    Json, Router,
    body::{Body, Bytes},
    extract::{Path, Query, Request, State},
    http::{HeaderMap, StatusCode, header},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use serde::Deserialize;
use tower_http::services::ServeDir;

mod files;
pub mod hardware;

#[derive(Clone)]
struct AppState {
    hardware: Arc<hardware::Hardware>,
    directory: PathBuf,
    authority: String,
}

pub fn router(
    hardware: Arc<hardware::Hardware>,
    directory: PathBuf,
    assets: PathBuf,
    authority: String,
) -> Router {
    let state = AppState {
        hardware,
        directory,
        authority,
    };
    Router::new()
        .route("/api/state", get(snapshot))
        .route("/api/devices/{kind}", get(devices))
        .route("/api/connections/{kind}", post(connect).delete(disconnect))
        .route("/api/video/frame", get(frame))
        .route("/api/video/stream", get(video_stream))
        .route("/api/files", get(files))
        .route("/api/workflow", post(select_workflow))
        .fallback_service(ServeDir::new(assets))
        .layer(middleware::from_fn_with_state(state.clone(), local_only))
        .with_state(state)
}

struct ApiError(StatusCode, String);
impl IntoResponse for ApiError {
    fn into_response(self) -> Response {
        (self.0, Json(serde_json::json!({ "error": self.1 }))).into_response()
    }
}
fn invalid(error: impl std::fmt::Display) -> ApiError {
    ApiError(StatusCode::BAD_REQUEST, error.to_string())
}
fn operation(error: impl std::fmt::Display) -> ApiError {
    ApiError(StatusCode::CONFLICT, error.to_string())
}

async fn local_only(State(state): State<AppState>, request: Request, next: Next) -> Response {
    if !allowed(request.headers(), &state.authority) {
        return ApiError(StatusCode::FORBIDDEN, "仅接受本地控制台的同源请求".into())
            .into_response();
    }
    let mut response = next.run(request).await;
    response
        .headers_mut()
        .insert(header::CACHE_CONTROL, "no-store".parse().unwrap());
    response
        .headers_mut()
        .insert(header::X_CONTENT_TYPE_OPTIONS, "nosniff".parse().unwrap());
    response.headers_mut().insert(header::CONTENT_SECURITY_POLICY, "default-src 'self'; img-src 'self' blob: data:; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'".parse().unwrap());
    response
}

fn allowed(headers: &HeaderMap, authority: &str) -> bool {
    let host = headers.get(header::HOST).and_then(|v| v.to_str().ok());
    host == Some(authority)
        && headers
            .get(header::ORIGIN)
            .is_none_or(|v| v.to_str().ok() == Some(format!("http://{authority}").as_str()))
        && headers
            .get("sec-fetch-site")
            .is_none_or(|v| v != "cross-site")
}

async fn snapshot(State(state): State<AppState>) -> Json<hardware::Snapshot> {
    Json(state.hardware.state.read().await.clone())
}

async fn devices(
    Path(kind): Path<hardware::Kind>,
) -> Result<Json<Vec<hardware::DeviceOption>>, ApiError> {
    tokio::task::spawn_blocking(move || hardware::enumerate(kind))
        .await
        .map_err(operation)?
        .map(Json)
        .map_err(operation)
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Connect {
    id: String,
}

async fn connect(
    State(state): State<AppState>,
    Path(kind): Path<hardware::Kind>,
    Json(input): Json<Connect>,
) -> Result<StatusCode, ApiError> {
    // Own the operation through completion even when the requesting browser navigates away.
    tokio::spawn(async move { state.hardware.connect(kind, input.id).await })
        .await
        .map_err(operation)?
        .map_err(operation)?;
    Ok(StatusCode::NO_CONTENT)
}

async fn disconnect(
    State(state): State<AppState>,
    Path(kind): Path<hardware::Kind>,
) -> Result<StatusCode, ApiError> {
    tokio::spawn(async move { state.hardware.disconnect(kind).await })
        .await
        .map_err(operation)?
        .map_err(operation)?;
    Ok(StatusCode::NO_CONTENT)
}

async fn frame(State(state): State<AppState>) -> Response {
    let mut preview = state.hardware.preview.subscribe();
    if !matches!(
        state.hardware.state.read().await.video.phase,
        hardware::Phase::Connected
    ) {
        return StatusCode::NO_CONTENT.into_response();
    }
    let next = tokio::select! {
        biased;
        _ = state.hardware.stop.cancelled() => None,
        changed = tokio::time::timeout(std::time::Duration::from_secs(1), preview.changed()) => {
            match changed {
                Ok(Ok(())) => preview.borrow_and_update().clone(),
                _ => None,
            }
        }
    };
    match next {
        Some(frame) => (
            [("content-type", "image/jpeg"), ("x-frame-id", &frame.id)],
            frame.jpeg,
        )
            .into_response(),
        None => StatusCode::NO_CONTENT.into_response(),
    }
}

async fn video_stream(State(state): State<AppState>) -> Response {
    let mut preview = state.hardware.preview.subscribe();
    if !matches!(
        state.hardware.state.read().await.video.phase,
        hardware::Phase::Connected
    ) {
        return StatusCode::NO_CONTENT.into_response();
    }
    // A new viewer starts with the next frame, not a cached image from an earlier session.
    preview.borrow_and_update();
    let stream = futures_util::stream::unfold(
        (preview, state.hardware.stop.clone()),
        |(mut preview, stop)| async move {
            tokio::select! {
                biased;
                _ = stop.cancelled() => return None,
                changed = preview.changed() => { changed.ok()?; }
            }
            // watch retains only the latest frame when a viewer cannot keep up.
            let frame = preview.borrow_and_update().clone()?;
            let mut part = format!(
                "--easycon-frame\r\nContent-Type: image/jpeg\r\nContent-Length: {}\r\nX-Frame-Id: {}\r\n\r\n",
                frame.jpeg.len(), frame.id,
            )
            .into_bytes();
            part.extend_from_slice(&frame.jpeg);
            part.extend_from_slice(b"\r\n");
            Some((
                Ok::<_, std::convert::Infallible>(Bytes::from(part)),
                (preview, stop),
            ))
        },
    );
    (
        [(
            header::CONTENT_TYPE,
            "multipart/x-mixed-replace; boundary=easycon-frame",
        )],
        Body::from_stream(stream),
    )
        .into_response()
}

#[derive(Deserialize)]
struct FileQuery {
    path: Option<PathBuf>,
}

async fn files(
    State(state): State<AppState>,
    Query(query): Query<FileQuery>,
) -> Result<Json<files::FileListing>, ApiError> {
    tokio::task::spawn_blocking(move || files::list(&query.path.unwrap_or(state.directory)))
        .await
        .map_err(operation)?
        .map(Json)
        .map_err(invalid)
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Select {
    path: PathBuf,
}

async fn select_workflow(Json(input): Json<Select>) -> Result<Json<files::Selection>, ApiError> {
    tokio::task::spawn_blocking(move || files::select(&input.path))
        .await
        .map_err(operation)?
        .map(Json)
        .map_err(invalid)
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::{
        body::{Body, to_bytes},
        http::Request,
    };
    use futures_util::StreamExt;
    use tower::ServiceExt;

    fn app() -> Router {
        router(
            hardware::Hardware::new(),
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../examples"),
            PathBuf::new(),
            "127.0.0.1:19392".into(),
        )
    }

    #[tokio::test]
    async fn rejects_foreign_origin_and_rebinding_host() {
        for (host, origin) in [
            ("evil.example:19392", "http://evil.example:19392"),
            ("127.0.0.1:19392", "https://evil.example"),
        ] {
            let response = app()
                .oneshot(
                    Request::builder()
                        .uri("/api/files")
                        .header("host", host)
                        .header("origin", origin)
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(response.status(), StatusCode::FORBIDDEN);
        }
    }

    #[tokio::test]
    async fn real_api_lists_files_and_starts_disconnected() {
        for path in ["/api/state", "/api/files"] {
            let response = app()
                .oneshot(
                    Request::builder()
                        .uri(path)
                        .header("host", "127.0.0.1:19392")
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(response.status(), StatusCode::OK);
            assert_eq!(response.headers()[header::CACHE_CONTROL], "no-store");
            let json: serde_json::Value =
                serde_json::from_slice(&to_bytes(response.into_body(), 1024 * 1024).await.unwrap())
                    .unwrap();
            if path == "/api/state" {
                assert_eq!(json["video"]["phase"], "disconnected");
                assert_eq!(json["dbfs"], serde_json::Value::Null);
            } else {
                assert!(std::path::Path::new(json["directory"].as_str().unwrap()).is_absolute());
            }
        }
    }

    #[tokio::test]
    async fn empty_device_is_rejected_without_touching_hardware() {
        let response = app()
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri("/api/connections/audio")
                    .header("host", "127.0.0.1:19392")
                    .header("content-type", "application/json")
                    .body(Body::from(r#"{"id":""}"#))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::CONFLICT);
    }

    #[tokio::test]
    async fn disconnected_video_has_no_snapshot_or_stream() {
        for path in ["/api/video/frame", "/api/video/stream"] {
            let response = app()
                .oneshot(
                    Request::builder()
                        .uri(path)
                        .header("host", "127.0.0.1:19392")
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(response.status(), StatusCode::NO_CONTENT);
        }
    }

    #[tokio::test]
    async fn stream_fans_out_latest_frame_and_closes_on_disconnect_or_shutdown() {
        let hardware = hardware::Hardware::new();
        hardware.state.write().await.video.phase = hardware::Phase::Connected;
        let app = router(
            hardware.clone(),
            PathBuf::new(),
            PathBuf::new(),
            "127.0.0.1:19392".into(),
        );
        let request = || {
            Request::builder()
                .uri("/api/video/stream")
                .header("host", "127.0.0.1:19392")
                .body(Body::empty())
                .unwrap()
        };
        let response = app.clone().oneshot(request()).await.unwrap();
        assert_eq!(
            response.headers()[header::CONTENT_TYPE],
            "multipart/x-mixed-replace; boundary=easycon-frame"
        );
        assert_eq!(response.headers()[header::CACHE_CONTROL], "no-store");
        let mut first = response.into_body().into_data_stream();
        let mut second = app
            .clone()
            .oneshot(request())
            .await
            .unwrap()
            .into_body()
            .into_data_stream();
        let publish = |id: &str| {
            hardware.preview.send_replace(Some(hardware::Preview {
                id: id.into(),
                jpeg: Bytes::from_static(b"\xff\xd8\xff\xd9"),
            }));
        };
        publish("1:1");
        let part = first.next().await.unwrap().unwrap();
        assert_eq!(part, b"--easycon-frame\r\nContent-Type: image/jpeg\r\nContent-Length: 4\r\nX-Frame-Id: 1:1\r\n\r\n\xff\xd8\xff\xd9\r\n"[..]);
        assert_eq!(second.next().await.unwrap().unwrap(), part);
        assert!(
            tokio::time::timeout(std::time::Duration::from_millis(5), first.next())
                .await
                .is_err()
        );
        publish("1:2");
        publish("1:3");
        let part = first.next().await.unwrap().unwrap();
        assert!(String::from_utf8_lossy(&part).contains("X-Frame-Id: 1:3\r\n"));
        hardware.preview.send_replace(None);
        assert!(first.next().await.is_none());
        assert!(second.next().await.is_none());
        assert_eq!(hardware.preview.receiver_count(), 0);
        let mut third = app
            .oneshot(request())
            .await
            .unwrap()
            .into_body()
            .into_data_stream();
        hardware.stop.cancel();
        assert!(third.next().await.is_none());
        assert_eq!(hardware.preview.receiver_count(), 0);
    }
}
