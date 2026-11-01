//! Owned, thread-local native DNN session; C++ objects never escape the bridge.

use std::{
    ffi::{c_char, c_void},
    path::Path,
    ptr::NonNull,
};

use crate::{Result, Runtime, RuntimeError, Version};

type OpenFn =
    unsafe extern "C" fn(*const u8, usize, *mut *mut c_void, *mut i64, *mut c_char, usize) -> i32;
type ForwardFn = unsafe extern "C" fn(
    *mut c_void,
    *const f32,
    usize,
    u32,
    u32,
    *mut RawTensor,
    *mut c_char,
    usize,
) -> i32;
type DeleteFn = unsafe extern "C" fn(*mut c_void);

#[repr(C)]
struct RawTensor {
    values: *const f32,
    length: usize,
    shape: [i64; 3],
}

pub struct Tensor {
    pub shape: [i64; 3],
    pub values: Vec<f32>,
}

pub struct Dnn {
    runtime: Runtime,
    handle: NonNull<c_void>,
    forward: ForwardFn,
    delete: DeleteFn,
    input_shape: [i64; 4],
}

fn native_status(status: i32, buffer: &[u8]) -> Result<()> {
    if status == 0 {
        return Ok(());
    }
    let end = buffer.iter().position(|b| *b == 0).unwrap_or(buffer.len());
    Err(RuntimeError::Native(
        String::from_utf8_lossy(&buffer[..end]).into_owned(),
    ))
}

fn input_length(height: u32, width: u32) -> Option<usize> {
    if height == 0 || width == 0 || height > i32::MAX as u32 || width > i32::MAX as u32 {
        return None;
    }
    (height as usize)
        .checked_mul(width as usize)?
        .checked_mul(3)
}

fn output_length(shape: [i64; 3]) -> Option<usize> {
    if shape[0] != 1 || shape[1] <= 0 || shape[2] <= 0 {
        return None;
    }
    let length = usize::try_from(shape[1])
        .ok()?
        .checked_mul(usize::try_from(shape[2]).ok()?)?;
    (length <= isize::MAX as usize / std::mem::size_of::<f32>()).then_some(length)
}

impl Dnn {
    /// The trusted ABI-v1 bridge and its dependencies must match this architecture.
    pub fn open(bridge: impl AsRef<Path>, model: &[u8]) -> Result<Self> {
        let runtime = Runtime::open(bridge)?;
        let symbol_error = |e: libloading::Error| RuntimeError::Load {
            path: runtime.path().into(),
            message: e.to_string(),
        };
        // SAFETY: resolve the separate DNN ABI before creating a handle. The owning
        // runtime outlives every function pointer and the native handle.
        let (open, forward, delete) = unsafe {
            let abi = runtime
                ._library
                .get::<unsafe extern "C" fn() -> u32>(b"easycon_opencv_dnn_abi\0")
                .map_err(symbol_error)?;
            if abi() != 1 {
                return Err(RuntimeError::Incompatible(
                    "expected OpenCV DNN bridge ABI 1; rebuild the bridge".into(),
                ));
            }
            (
                *runtime
                    ._library
                    .get::<OpenFn>(b"easycon_opencv_dnn_open\0")
                    .map_err(symbol_error)?,
                *runtime
                    ._library
                    .get::<ForwardFn>(b"easycon_opencv_dnn_forward\0")
                    .map_err(symbol_error)?,
                *runtime
                    ._library
                    .get::<DeleteFn>(b"easycon_opencv_dnn_delete\0")
                    .map_err(symbol_error)?,
            )
        };
        let mut handle = std::ptr::null_mut();
        let mut input_shape = [0; 4];
        let mut error = [0; 4096];
        // SAFETY: native open consumes model bytes synchronously and retains no Rust
        // pointers. Failure leaves a null handle; success transfers unique ownership.
        let status = unsafe {
            open(
                model.as_ptr(),
                model.len(),
                &mut handle,
                input_shape.as_mut_ptr(),
                error.as_mut_ptr().cast(),
                error.len(),
            )
        };
        native_status(status, &error)?;
        let handle =
            NonNull::new(handle).ok_or_else(|| RuntimeError::Native("null DNN handle".into()))?;
        Ok(Self {
            runtime,
            handle,
            forward,
            delete,
            input_shape,
        })
    }

    pub fn input_shape(&self) -> [i64; 4] {
        self.input_shape
    }
    pub fn version(&self) -> Version {
        self.runtime.version()
    }
    pub fn path(&self) -> &Path {
        self.runtime.path()
    }

    pub fn forward(&mut self, input: &[f32], height: u32, width: u32) -> Result<Tensor> {
        if input_length(height, width) != Some(input.len()) || input.iter().any(|v| !v.is_finite())
        {
            return Err(RuntimeError::InvalidImage(
                "expected finite float32 [1,3,H,W] buffer",
            ));
        }
        let mut result = RawTensor {
            values: std::ptr::null(),
            length: 0,
            shape: [0; 3],
        };
        let mut error = [0; 4096];
        // SAFETY: validated input lives through the call; mutable borrow prevents
        // concurrent inference. Output belongs to the handle until its next call.
        let status = unsafe {
            (self.forward)(
                self.handle.as_ptr(),
                input.as_ptr(),
                input.len(),
                height,
                width,
                &mut result,
                error.as_mut_ptr().cast(),
                error.len(),
            )
        };
        native_status(status, &error)?;
        if result.values.is_null() || output_length(result.shape) != Some(result.length) {
            return Err(RuntimeError::Native(
                "invalid float32 [1,T,C] tensor".into(),
            ));
        }
        // SAFETY: the trusted bridge provides a contiguous float32 allocation;
        // dimensions/length were checked and the session remains exclusively borrowed.
        let values = unsafe { std::slice::from_raw_parts(result.values, result.length) }.to_vec();
        Ok(Tensor {
            shape: result.shape,
            values,
        })
    }
}

impl Drop for Dnn {
    fn drop(&mut self) {
        // SAFETY: unique live handle, deleted before the runtime library is dropped.
        unsafe { (self.delete)(self.handle.as_ptr()) };
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tensors_reject_empty_overflow_and_wrong_batch_shapes() {
        assert_eq!(input_length(48, 320), Some(48 * 320 * 3));
        for (h, w) in [(0, 320), (48, 0), (u32::MAX, 1)] {
            assert!(input_length(h, w).is_none());
        }
        assert_eq!(output_length([1, 40, 18710]), Some(40 * 18710));
        for shape in [
            [0, 1, 1],
            [2, 1, 1],
            [1, -1, 2],
            [1, 2, 0],
            [1, i64::MAX, i64::MAX],
        ] {
            assert!(output_length(shape).is_none());
        }
    }
}
