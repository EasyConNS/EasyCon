use std::{
    io::{self, Read, Write},
    time::Duration,
};

use serialport::{ClearBuffer, DataBits, FlowControl, Parity, SerialPort, StopBits};

use crate::{DeviceError, Result};

/// A caller-supplied transport must honor finite read/write timeouts and release its port on Drop.
pub trait Transport: Read + Write + Send {
    fn set_timeout(&mut self, timeout: Duration) -> io::Result<()>;
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LinePolicy {
    pub dtr_on_open: bool,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PortInfo {
    pub name: String,
    pub usb_vendor_id: Option<u16>,
    pub usb_product_id: Option<u16>,
    pub serial_number: Option<String>,
}

/// Enumeration only; no port is opened or automatically probed.
pub fn ports() -> Result<Vec<PortInfo>> {
    serialport::available_ports()
        .map_err(|error| DeviceError::io("enumerate", error.into()))
        .map(|ports| {
            ports
                .into_iter()
                .map(|port| {
                    let (usb_vendor_id, usb_product_id, serial_number) = match port.port_type {
                        serialport::SerialPortType::UsbPort(info) => {
                            (Some(info.vid), Some(info.pid), info.serial_number)
                        }
                        _ => (None, None, None),
                    };
                    PortInfo {
                        name: port.port_name,
                        usb_vendor_id,
                        usb_product_id,
                        serial_number,
                    }
                })
                .collect()
        })
}

pub struct NativeTransport(Box<dyn SerialPort>);

impl NativeTransport {
    pub(crate) fn open(name: &str, baud_rate: u32, lines: LinePolicy) -> Result<Self> {
        let mut port = serialport::new(name, baud_rate)
            .data_bits(DataBits::Eight)
            .parity(Parity::None)
            .stop_bits(StopBits::One)
            .flow_control(FlowControl::None)
            .timeout(Duration::from_millis(10))
            .dtr_on_open(lines.dtr_on_open)
            .open()
            .map_err(|error| DeviceError::io("open", error.into()))?;
        // Lower RTS before DTR to avoid the reset-triggering RTS-high/DTR-low state on ESP boards.
        port.write_request_to_send(false)
            .map_err(|error| DeviceError::io("clear RTS", error.into()))?;
        port.write_data_terminal_ready(false)
            .map_err(|error| DeviceError::io("clear DTR", error.into()))?;
        port.clear(ClearBuffer::All)
            .map_err(|error| DeviceError::io("clear buffers", error.into()))?;
        Ok(Self(port))
    }
}

impl Read for NativeTransport {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        self.0.read(buffer)
    }
}

impl Write for NativeTransport {
    fn write(&mut self, buffer: &[u8]) -> io::Result<usize> {
        self.0.write(buffer)
    }
    fn flush(&mut self) -> io::Result<()> {
        self.0.flush()
    }
}

impl Transport for NativeTransport {
    fn set_timeout(&mut self, timeout: Duration) -> io::Result<()> {
        self.0.set_timeout(timeout).map_err(Into::into)
    }
}
