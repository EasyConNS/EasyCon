use std::path::{Path, PathBuf};

use easycon_sense::project::PreparedProject;
use serde::{Deserialize, Serialize};

#[derive(Serialize)]
pub struct FileEntry {
    pub name: String,
    pub path: PathBuf,
    pub directory: bool,
}

#[derive(Serialize)]
pub struct FileListing {
    pub directory: PathBuf,
    pub parent: Option<PathBuf>,
    pub entries: Vec<FileEntry>,
}

pub fn list(path: &Path) -> Result<FileListing, String> {
    let directory = path.canonicalize().map_err(|e| e.to_string())?;
    let mut entries = Vec::new();
    for entry in std::fs::read_dir(&directory).map_err(|e| e.to_string())? {
        let entry = entry.map_err(|e| e.to_string())?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if name.starts_with('.') {
            continue;
        }
        // Follow directory links so mounted project folders can be selected too.
        let is_directory = entry.path().is_dir();
        if is_directory
            || entry
                .path()
                .extension()
                .is_some_and(|ext| ext.eq_ignore_ascii_case("json"))
        {
            entries.push(FileEntry {
                name,
                path: entry.path(),
                directory: is_directory,
            });
        }
    }
    entries.sort_by(|a, b| {
        b.directory
            .cmp(&a.directory)
            .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
    });
    Ok(FileListing {
        parent: directory.parent().map(Path::to_owned),
        directory,
        entries,
    })
}

#[derive(Default, Deserialize, Serialize)]
pub struct Preferences {
    pub video: Option<String>,
    pub gamepad: Option<String>,
}

#[derive(Serialize)]
pub struct Selection {
    pub path: PathBuf,
    pub preferences: Preferences,
}

pub fn select(path: &Path) -> Result<Selection, String> {
    let path = path.canonicalize().map_err(|e| e.to_string())?;
    if !path.is_file()
        || !path
            .extension()
            .is_some_and(|ext| ext.eq_ignore_ascii_case("json"))
    {
        return Err("请选择 workflow JSON 文件".into());
    }
    let project = PreparedProject::load(&path).map_err(|e| e.to_string())?;
    let preferences = match std::fs::read_to_string(project.directory.join("config.json")) {
        Ok(text) => {
            serde_json::from_str(text.trim_start_matches('\u{feff}')).map_err(|e| e.to_string())?
        }
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Preferences::default(),
        Err(error) => return Err(error.to_string()),
    };
    Ok(Selection { path, preferences })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn selects_validated_ir_and_returns_absolute_path() {
        let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../examples/composition.ir.json");
        let selected = select(&path).unwrap();
        assert!(selected.path.is_absolute());
        assert_eq!(selected.path, path.canonicalize().unwrap());
        assert!(selected.preferences.video.is_none());
        assert!(
            select(
                Path::new(env!("CARGO_MANIFEST_DIR"))
                    .join("../examples/capture-match/config.json")
                    .as_path()
            )
            .is_err()
        );
    }

    #[test]
    fn lists_directories_before_json_files() {
        let listing = list(
            Path::new(env!("CARGO_MANIFEST_DIR"))
                .join("../examples")
                .as_path(),
        )
        .unwrap();
        assert!(
            listing
                .entries
                .iter()
                .any(|e| e.name == "composition.ir.json")
        );
        assert!(
            listing
                .entries
                .iter()
                .any(|e| e.name == "capture-match" && e.directory)
        );
        let first_file = listing.entries.iter().position(|e| !e.directory).unwrap();
        assert!(listing.entries[first_file..].iter().all(|e| !e.directory));
        assert!(listing.entries.iter().all(|e| e.path.is_absolute()));
    }
}
