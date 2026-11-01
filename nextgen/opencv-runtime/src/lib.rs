//! Safe Rust boundary to our small C ABI bridge over native OpenCV C++ libraries.
//! OpenCV headers and C++ object layouts never cross this interface.

use libloading::Library;
use std::{
    ffi::c_char,
    path::{Path, PathBuf},
};
use thiserror::Error;

pub mod dnn;

pub type Result<T> = std::result::Result<T, RuntimeError>;

#[derive(Debug, Error)]
pub enum RuntimeError {
    #[error("OpenCV runtime {path}: {message}")]
    Load { path: PathBuf, message: String },
    #[error("incompatible OpenCV runtime: {0}")]
    Incompatible(String),
    #[error("invalid image: {0}")]
    InvalidImage(&'static str),
    #[error("native OpenCV: {0}")]
    Native(String),
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Version {
    pub major: u32,
    pub minor: u32,
    pub revision: u32,
    pub compiled_major: u32,
}

impl Version {
    fn validate(self) -> Result<Self> {
        if self.major < 5 || self.compiled_major < 5 {
            return Err(RuntimeError::Incompatible(format!(
                "requires OpenCV >= 5, found {self}"
            )));
        }
        if self.major != self.compiled_major {
            return Err(RuntimeError::Incompatible(format!(
                "loaded {self} with a bridge built for {}; rebuild the bridge using this SDK",
                self.compiled_major
            )));
        }
        Ok(self)
    }
}

impl std::fmt::Display for Version {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}.{}.{}", self.major, self.minor, self.revision)
    }
}

#[repr(u32)]
#[derive(Clone, Copy, Debug)]
pub enum Method {
    SqDiff = 0,
    SqDiffNormed = 1,
    CCorr = 2,
    CCorrNormed = 3,
    CCoeff = 4,
    CCoeffNormed = 5,
}

pub struct RgbImage<'a> {
    pub width: u32,
    pub height: u32,
    pub pixels: &'a [u8],
}

impl RgbImage<'_> {
    fn raw(&self) -> Result<RawImage> {
        let length = (self.width as usize)
            .checked_mul(self.height as usize)
            .and_then(|n| n.checked_mul(3));
        if self.width == 0
            || self.height == 0
            || self.width > i32::MAX as u32
            || self.height > i32::MAX as u32
            || length != Some(self.pixels.len())
        {
            return Err(RuntimeError::InvalidImage(
                "expected positive i32 dimensions and width*height*3 bytes",
            ));
        }
        Ok(RawImage {
            pixels: self.pixels.as_ptr(),
            length: self.pixels.len(),
            width: self.width,
            height: self.height,
        })
    }
}

#[repr(C)]
struct RawImage {
    pixels: *const u8,
    length: usize,
    width: u32,
    height: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct Match {
    pub x: i32,
    pub y: i32,
    pub score: f64,
}

type MatchFn = unsafe extern "C" fn(
    *const RawImage,
    *const RawImage,
    u32,
    *mut Match,
    *mut c_char,
    usize,
) -> i32;

pub struct Runtime {
    // Function pointers stay valid until this owning library is dropped.
    _library: Library,
    match_fn: MatchFn,
    version: Version,
    path: PathBuf,
}

impl Runtime {
    /// Loads our ABI-v1 bridge, which dynamically links the caller's native OpenCV SDK.
    /// The supplied native binaries must be trusted and match this platform/architecture.
    pub fn open(path: impl AsRef<Path>) -> Result<Self> {
        let requested = path.as_ref();
        let load_error = |e: &dyn std::fmt::Display| RuntimeError::Load {
            path: requested.into(),
            message: e.to_string(),
        };
        let path = requested.canonicalize().map_err(|e| load_error(&e))?;
        // SAFETY: loading native code is confined to this boundary. Symbols below must implement
        // our documented ABI. The Library is retained for the complete lifetime of those pointers.
        let library = unsafe { load(&path) }.map_err(|e| load_error(&e))?;
        unsafe {
            let abi = library
                .get::<unsafe extern "C" fn() -> u32>(b"easycon_opencv_abi\0")
                .map_err(|e| load_error(&e))?;
            if abi() != 1 {
                return Err(RuntimeError::Incompatible(
                    "unsupported EasyCon OpenCV bridge ABI (expected 1)".into(),
                ));
            }
            let version_fn = library
                .get::<unsafe extern "C" fn() -> Version>(b"easycon_opencv_version\0")
                .map_err(|e| load_error(&e))?;
            let version = version_fn().validate()?;
            let match_fn = *library
                .get::<MatchFn>(b"easycon_opencv_match\0")
                .map_err(|e| load_error(&e))?;
            Ok(Self {
                _library: library,
                match_fn,
                version,
                path,
            })
        }
    }

    pub fn version(&self) -> Version {
        self.version
    }
    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn match_template(
        &self,
        source: RgbImage<'_>,
        template: RgbImage<'_>,
        method: Method,
    ) -> Result<Match> {
        let source = source.raw()?;
        let template = template.raw()?;
        if template.width > source.width || template.height > source.height {
            return Err(RuntimeError::InvalidImage("template exceeds search image"));
        }
        let mut result = Match::default();
        let mut error = [0u8; 4096];
        // SAFETY: both RGB buffers are validated and borrowed until the call returns. The bridge
        // only reads them, owns all cv::Mat instances, catches exceptions, and retains no pointers.
        // The output/error buffers are correctly sized and live for the call.
        let status = unsafe {
            (self.match_fn)(
                &source,
                &template,
                method as u32,
                &mut result,
                error.as_mut_ptr().cast(),
                error.len(),
            )
        };
        if status != 0 {
            let length = error
                .iter()
                .position(|&byte| byte == 0)
                .unwrap_or(error.len());
            return Err(RuntimeError::Native(format!(
                "status {status}: {}",
                String::from_utf8_lossy(&error[..length])
            )));
        }
        if !result.score.is_finite()
            || result.x < 0
            || result.y < 0
            || result.x as u32 > source.width - template.width
            || result.y as u32 > source.height - template.height
        {
            return Err(RuntimeError::Native("invalid match output".into()));
        }
        Ok(result)
    }
}

#[cfg(windows)]
unsafe fn load(path: &Path) -> std::result::Result<Library, libloading::Error> {
    use libloading::os::windows::{
        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR,
        Library as WindowsLibrary,
    };
    // SAFETY: absolute library path; dependencies are resolved beside it and in standard dirs,
    // without changing process-wide PATH or the working directory.
    unsafe {
        WindowsLibrary::load_with_flags(
            path,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS,
        )
        .map(Into::into)
    }
}

#[cfg(unix)]
unsafe fn load(path: &Path) -> std::result::Result<Library, libloading::Error> {
    use libloading::os::unix::{Library as UnixLibrary, RTLD_LOCAL, RTLD_NOW};
    // SAFETY: absolute library path. Resolve all dependent symbols before exposing the runtime.
    unsafe { UnixLibrary::open(Some(path), RTLD_LOCAL | RTLD_NOW).map(Into::into) }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn version_floor_allows_future_majors_but_requires_matching_native_abi() {
        for major in [5, 6, 42] {
            assert!(
                Version {
                    major,
                    minor: 3,
                    revision: 7,
                    compiled_major: major
                }
                .validate()
                .is_ok()
            );
        }
        for (major, compiled_major) in [(4, 4), (5, 4), (6, 5)] {
            assert!(
                Version {
                    major,
                    compiled_major,
                    ..Version::default()
                }
                .validate()
                .is_err()
            );
        }
    }

    #[test]
    fn invalid_rgb_buffers_are_rejected_at_the_ffi_boundary() {
        for (width, height, pixels) in
            [(0, 1, &[][..]), (1, 1, &[0, 0][..]), (u32::MAX, 2, &[][..])]
        {
            assert!(
                RgbImage {
                    width,
                    height,
                    pixels
                }
                .raw()
                .is_err()
            );
        }
    }

    #[test]
    fn missing_library_returns_its_requested_path() {
        let missing = Path::new("missing-easycon-opencv-runtime");
        assert!(
            matches!(Runtime::open(missing), Err(RuntimeError::Load { path, .. }) if path == missing)
        );
    }
}
