//! Built-in serial firmware implementations behind the common device contracts.
pub mod easycon;
pub mod pokeldn;

pub use easycon::EasyCon;
pub use pokeldn::Pokeldn;
