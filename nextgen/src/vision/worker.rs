//! Async adapters over owned native workers. Workflow-supplied factories choose all assets.
//!
//! ```no_run
//! use std::collections::BTreeMap;
//! use easycon_sense::{
//!     ir::{ValidatedDocument, Value},
//!     run::RunBindings,
//!     vision::{OcrConfig, worker::NativeOcr},
//! };
//! use tokio_util::sync::CancellationToken;
//!
//! async fn run_workflow(
//!     document: &ValidatedDocument,
//!     params: BTreeMap<String, Value>,
//!     config: OcrConfig, // The caller chooses this workflow's model and native library.
//!     resource_lifetime: &CancellationToken,
//!     run_stop: &CancellationToken,
//! ) -> Result<(), Box<dyn std::error::Error>> {
//!     let mut ocr = NativeOcr::open(config, resource_lifetime).await?;
//!     let mut bindings = RunBindings::default();
//!     bindings.ocr.insert("workflow-ocr".into(), &mut ocr);
//!     let execution = document.execute(bindings, params, run_stop).await;
//!     // Also shut down on entry-parameter errors; do not propagate execution with ? first.
//!     let shutdown = ocr.shutdown().await;
//!     shutdown?;
//!     let execution = execution?;
//!     println!("{:?}", execution.outcome);
//!     Ok(())
//! }
//! ```

use std::path::PathBuf;
use tokio_util::sync::CancellationToken;

use super::{
    Image, MatchConfig, OcrBackend, OcrBackendKind, OcrConfig, OcrResult, Rect, Result,
    TemplateBackendKind, TemplateMatch, TemplateMatcher, VisionError, VisionFuture,
    check_cancelled,
};
use crate::native_work::{NativeWorkError, NativeWorker, WorkerError};

fn failure(error: NativeWorkError<VisionError>) -> VisionError {
    match error {
        NativeWorkError::Operation(error) => error,
        NativeWorkError::Worker(WorkerError::Cancelled) => VisionError::Cancelled,
        NativeWorkError::Worker(error) => VisionError::Worker(error),
    }
}

fn observed<T>(result: Result<T>, token: &CancellationToken, stopped: bool) -> Result<T> {
    // Worker integrity errors remain fatal even when cancellation arrived concurrently.
    if matches!(result, Err(VisionError::Worker(_))) {
        return result;
    }
    if stopped {
        return Err(VisionError::Cancelled);
    }
    check_cancelled(token)?;
    result
}

#[must_use = "call shutdown().await when the host releases this provider"]
pub struct NativeMatcher {
    worker: NativeWorker<Box<dyn TemplateMatcher>>,
    kind: TemplateBackendKind,
}

impl NativeMatcher {
    pub async fn open_opencv(
        directory: Option<PathBuf>,
        parent: &CancellationToken,
    ) -> Result<Self> {
        Self::spawn(
            move || match directory {
                Some(path) => {
                    Ok(Box::new(super::opencv::OpenCv5::open(path)?) as Box<dyn TemplateMatcher>)
                }
                None => super::create_template_matcher(TemplateBackendKind::OpenCv5),
            },
            parent,
        )
        .await
    }

    /// The factory crosses threads, but the backend it creates never does.
    pub async fn spawn<F>(factory: F, parent: &CancellationToken) -> Result<Self>
    where
        F: FnOnce() -> Result<Box<dyn TemplateMatcher>> + Send + 'static,
    {
        let mut worker = NativeWorker::start(move |_| factory(), parent)
            .await
            .map_err(failure)?;
        let kind = match worker.call(parent, |backend, _| Ok(backend.kind())).await {
            Ok(kind) => kind,
            Err(error) => {
                worker.shutdown().await.map_err(VisionError::Worker)?;
                return Err(failure(error));
            }
        };
        Ok(Self { worker, kind })
    }

    pub async fn shutdown(self) -> Result<()> {
        self.worker.shutdown().await.map_err(VisionError::Worker)
    }
}

impl TemplateMatcher for NativeMatcher {
    fn kind(&self) -> TemplateBackendKind {
        self.kind
    }

    fn match_template(
        &mut self,
        image: &Image,
        template: &Image,
        config: MatchConfig,
        token: &CancellationToken,
    ) -> Result<TemplateMatch> {
        let (image, template) = (image.clone(), template.clone());
        let result = self
            .worker
            .call_blocking(token, move |backend, token| {
                backend.match_template(&image, &template, config, token)
            })
            .map_err(failure);
        observed(result, token, self.worker.is_cancelled())
    }

    fn match_template_async<'a>(
        &'a mut self,
        image: &'a Image,
        template: &'a Image,
        config: MatchConfig,
        token: &'a CancellationToken,
    ) -> VisionFuture<'a, TemplateMatch> {
        Box::pin(async move {
            let (image, template) = (image.clone(), template.clone());
            let result = self
                .worker
                .call(token, move |backend, token| {
                    backend.match_template(&image, &template, config, token)
                })
                .await
                .map_err(failure);
            observed(result, token, self.worker.is_cancelled())
        })
    }
}

#[must_use = "call shutdown().await when the host releases this provider"]
pub struct NativeOcr {
    worker: NativeWorker<Box<dyn OcrBackend>>,
    kind: OcrBackendKind,
}

impl NativeOcr {
    pub async fn open(config: OcrConfig, parent: &CancellationToken) -> Result<Self> {
        Self::spawn(move || super::create_ocr(config), parent).await
    }

    pub async fn spawn<F>(factory: F, parent: &CancellationToken) -> Result<Self>
    where
        F: FnOnce() -> Result<Box<dyn OcrBackend>> + Send + 'static,
    {
        let mut worker = NativeWorker::start(move |_| factory(), parent)
            .await
            .map_err(failure)?;
        let kind = match worker.call(parent, |backend, _| Ok(backend.kind())).await {
            Ok(kind) => kind,
            Err(error) => {
                worker.shutdown().await.map_err(VisionError::Worker)?;
                return Err(failure(error));
            }
        };
        Ok(Self { worker, kind })
    }

    pub async fn shutdown(self) -> Result<()> {
        self.worker.shutdown().await.map_err(VisionError::Worker)
    }
}

impl OcrBackend for NativeOcr {
    fn kind(&self) -> OcrBackendKind {
        self.kind
    }

    fn recognize(
        &mut self,
        image: &Image,
        region: Option<Rect>,
        token: &CancellationToken,
    ) -> Result<OcrResult> {
        let image = image.clone();
        let result = self
            .worker
            .call_blocking(token, move |backend, token| {
                backend.recognize(&image, region, token)
            })
            .map_err(failure);
        observed(result, token, self.worker.is_cancelled())
    }

    fn recognize_async<'a>(
        &'a mut self,
        image: &'a Image,
        region: Option<Rect>,
        token: &'a CancellationToken,
    ) -> VisionFuture<'a, OcrResult> {
        Box::pin(async move {
            let image = image.clone();
            let result = self
                .worker
                .call(token, move |backend, token| {
                    backend.recognize(&image, region, token)
                })
                .await
                .map_err(failure);
            observed(result, token, self.worker.is_cancelled())
        })
    }
}
