//! NEOX system layer: everything that touches Windows. All system access goes through small traits
//! (`Runner`, `RegistryApi`, `Adapters`), so the handler logic is unit-tested on any OS with in-memory fakes.

pub mod actions;
pub mod adapters;
pub mod cleaner;
pub mod games;
pub mod handlers;
pub mod managers;
pub mod parse;
pub mod registry;
pub mod bench;
pub mod runner;
pub mod sysinfo;
pub mod system;
