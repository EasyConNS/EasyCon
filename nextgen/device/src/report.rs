use crate::{DeviceError, Result};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u16)]
pub enum Button {
    Y = 0x0001,
    B = 0x0002,
    A = 0x0004,
    X = 0x0008,
    L = 0x0010,
    R = 0x0020,
    ZL = 0x0040,
    ZR = 0x0080,
    Minus = 0x0100,
    Plus = 0x0200,
    LeftStick = 0x0400,
    RightStick = 0x0800,
    Home = 0x1000,
    Capture = 0x2000,
}

impl Button {
    pub const fn mask(self) -> u16 {
        self as u16
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(u8)]
pub enum Hat {
    Up = 0,
    UpRight = 1,
    Right = 2,
    DownRight = 3,
    Down = 4,
    DownLeft = 5,
    Left = 6,
    UpLeft = 7,
    #[default]
    Center = 8,
}

impl TryFrom<u8> for Hat {
    type Error = DeviceError;
    fn try_from(value: u8) -> Result<Self> {
        match value {
            0 => Ok(Self::Up),
            1 => Ok(Self::UpRight),
            2 => Ok(Self::Right),
            3 => Ok(Self::DownRight),
            4 => Ok(Self::Down),
            5 => Ok(Self::DownLeft),
            6 => Ok(Self::Left),
            7 => Ok(Self::UpLeft),
            8 => Ok(Self::Center),
            _ => Err(DeviceError::InvalidArgument("HAT must be in 0..=8".into())),
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Stick {
    pub x: u8,
    pub y: u8,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum StickId {
    Left,
    Right,
}

impl Default for Stick {
    fn default() -> Self {
        Self { x: 128, y: 128 }
    }
}

/// A complete state replaces the previous report; there is no hidden key merging or duration.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Report {
    pub buttons: u16,
    pub hat: Hat,
    pub left: Stick,
    pub right: Stick,
}

impl Report {
    pub fn validate(&self) -> Result<()> {
        if self.buttons & !0x3fff != 0 {
            return Err(DeviceError::InvalidArgument(
                "button bits 14 and 15 are reserved".into(),
            ));
        }
        Ok(())
    }
}
