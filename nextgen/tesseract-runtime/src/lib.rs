//! Runtime-loaded Tesseract C API. No native headers, link flags or Leptonica bindings.

use std::{
    ffi::{CStr, CString, c_char, c_int, c_void},
    path::Path,
    ptr::NonNull,
};

use libloading::Library;
use thiserror::Error;

#[derive(Debug, Error)]
#[error("Tesseract runtime: {0}")]
pub struct Error(String);

pub type Result<T> = std::result::Result<T, Error>;
type Handle = *mut c_void;

struct Api {
    delete: unsafe extern "C" fn(Handle),
    clear: unsafe extern "C" fn(Handle),
    set_variable: unsafe extern "C" fn(Handle, *const c_char, *const c_char) -> c_int,
    set_image: unsafe extern "C" fn(Handle, *const u8, c_int, c_int, c_int, c_int),
    set_resolution: unsafe extern "C" fn(Handle, c_int),
    recognize: unsafe extern "C" fn(Handle, *mut c_void) -> c_int,
    text: unsafe extern "C" fn(Handle) -> *mut c_char,
    tsv: unsafe extern "C" fn(Handle, c_int) -> *mut c_char,
    free_text: unsafe extern "C" fn(*const c_char),
}

pub struct Runtime {
    // The handle is thread-confined; all function pointers are owned by this library.
    handle: NonNull<c_void>,
    api: Api,
    version: String,
    _library: Library,
}

pub struct Recognition {
    pub text: String,
    pub tsv: String,
}

fn cstring(value: &str) -> Result<CString> {
    CString::new(value).map_err(|_| Error("NUL in native string argument".into()))
}

fn rgb_stride(width: u32, height: u32, length: usize) -> Result<c_int> {
    let stride = width.checked_mul(3).filter(|s| *s <= c_int::MAX as u32);
    let expected = (width as usize)
        .checked_mul(height as usize)
        .and_then(|n| n.checked_mul(3));
    match stride {
        Some(stride)
            if width > 0
                && height > 0
                && height <= c_int::MAX as u32
                && expected == Some(length) =>
        {
            Ok(stride as c_int)
        }
        _ => Err(Error(
            "expected RGB8 image with valid i32 dimensions and stride".into(),
        )),
    }
}

impl Runtime {
    /// The explicit library must be trusted and match the process architecture.
    pub fn open(library: &Path, data: &Path, language: &str) -> Result<Self> {
        let library = library.canonicalize().map_err(|e| Error(e.to_string()))?;
        let data = cstring(
            data.to_str()
                .ok_or_else(|| Error("tessdata path must be UTF-8".into()))?,
        )?;
        let language = cstring(language)?;
        // SAFETY: symbols use the public Tesseract 5 C ABI. Resolve every symbol before
        // creating a handle, and retain the library until after the handle is deleted.
        unsafe {
            let library = load(&library).map_err(|e| Error(e.to_string()))?;
            let version_fn = library
                .get::<unsafe extern "C" fn() -> *const c_char>(b"TessVersion\0")
                .map_err(|e| Error(e.to_string()))?;
            let version = version_fn();
            if version.is_null() {
                return Err(Error("null version string".into()));
            }
            let version = CStr::from_ptr(version).to_string_lossy().into_owned();
            if version
                .split('.')
                .next()
                .and_then(|s| s.parse::<u32>().ok())
                != Some(5)
            {
                return Err(Error(format!(
                    "expected Tesseract 5 C API, found {version}"
                )));
            }
            macro_rules! symbol {
                ($name:literal) => {
                    *library
                        .get(concat!($name, "\0").as_bytes())
                        .map_err(|e| Error(e.to_string()))?
                };
            }
            let api = Api {
                delete: symbol!("TessBaseAPIDelete"),
                clear: symbol!("TessBaseAPIClear"),
                set_variable: symbol!("TessBaseAPISetVariable"),
                set_image: symbol!("TessBaseAPISetImage"),
                set_resolution: symbol!("TessBaseAPISetSourceResolution"),
                recognize: symbol!("TessBaseAPIRecognize"),
                text: symbol!("TessBaseAPIGetUTF8Text"),
                tsv: symbol!("TessBaseAPIGetTsvText"),
                free_text: symbol!("TessDeleteText"),
            };
            let create = library
                .get::<unsafe extern "C" fn() -> Handle>(b"TessBaseAPICreate\0")
                .map_err(|e| Error(e.to_string()))?;
            let init = library
                .get::<unsafe extern "C" fn(Handle, *const c_char, *const c_char) -> c_int>(
                    b"TessBaseAPIInit3\0",
                )
                .map_err(|e| Error(e.to_string()))?;
            let handle = NonNull::new(create()).ok_or_else(|| Error("create failed".into()))?;
            let status = init(handle.as_ptr(), data.as_ptr(), language.as_ptr());
            let runtime = Self {
                handle,
                api,
                version,
                _library: library,
            };
            if status != 0 {
                return Err(Error("model initialization failed".into()));
            }
            Ok(runtime)
        }
    }

    pub fn version(&self) -> &str {
        &self.version
    }

    pub fn set_variable(&mut self, name: &str, value: &str) -> Result<()> {
        let name = cstring(name)?;
        let value = cstring(value)?;
        // SAFETY: live, exclusively borrowed handle; C strings live through the call.
        let accepted =
            unsafe { (self.api.set_variable)(self.handle.as_ptr(), name.as_ptr(), value.as_ptr()) };
        if accepted == 0 {
            return Err(Error(format!(
                "unknown or invalid variable {}",
                name.to_string_lossy()
            )));
        }
        Ok(())
    }

    pub fn recognize_rgb(&mut self, width: u32, height: u32, rgb: &[u8]) -> Result<Recognition> {
        let stride = rgb_stride(width, height, rgb.len())?;
        // SAFETY: SetImage copies the validated RGB buffer into native-owned storage.
        // Clear discards prior recognition; all calls exclusively borrow this handle.
        unsafe {
            let handle = self.handle.as_ptr();
            (self.api.clear)(handle);
            (self.api.set_image)(
                handle,
                rgb.as_ptr(),
                width as c_int,
                height as c_int,
                3,
                stride,
            );
            (self.api.set_resolution)(handle, 300);
            if (self.api.recognize)(handle, std::ptr::null_mut()) != 0 {
                return Err(Error("recognition failed".into()));
            }
            let text = self.take_text((self.api.text)(handle))?;
            let tsv = self.take_text((self.api.tsv)(handle, 0))?;
            Ok(Recognition { text, tsv })
        }
    }

    unsafe fn take_text(&self, ptr: *mut c_char) -> Result<String> {
        if ptr.is_null() {
            return Err(Error("native text getter returned null".into()));
        }
        // SAFETY: caller supplies a getter-owned NUL-terminated string. Release it with
        // the same library's allocator, including on invalid UTF-8.
        let text = unsafe { CStr::from_ptr(ptr) }
            .to_str()
            .map(str::to_owned)
            .map_err(|e| Error(e.to_string()));
        unsafe { (self.api.free_text)(ptr) };
        text
    }
}

impl Drop for Runtime {
    fn drop(&mut self) {
        // SAFETY: the uniquely owned handle is deleted before unloading its library.
        unsafe { (self.api.delete)(self.handle.as_ptr()) };
    }
}

#[cfg(windows)]
unsafe fn load(path: &Path) -> std::result::Result<Library, libloading::Error> {
    use libloading::os::windows::{
        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR,
        Library as WindowsLibrary,
    };
    // SAFETY: absolute path; search dependencies beside the DLL and in standard dirs.
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
    // SAFETY: absolute path; resolve native dependencies before exposing the API.
    unsafe { UnixLibrary::open(Some(path), RTLD_LOCAL | RTLD_NOW).map(Into::into) }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn missing_library_and_nul_arguments_are_errors() {
        assert!(
            Runtime::open(Path::new("missing-tesseract"), Path::new("missing"), "eng").is_err()
        );
        assert!(cstring("eng\0chi_sim").is_err());
    }

    #[test]
    fn rgb_boundary_rejects_empty_short_and_overflowing_buffers() {
        assert_eq!(rgb_stride(2, 3, 18).unwrap(), 6);
        for (width, height, length) in [
            (0, 1, 0),
            (1, 0, 0),
            (2, 3, 17),
            (u32::MAX, 1, 3),
            (1, u32::MAX, 3),
        ] {
            assert!(rgb_stride(width, height, length).is_err());
        }
    }
}
