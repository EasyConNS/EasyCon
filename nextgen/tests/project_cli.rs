use std::{path::Path, process::Command};

#[test]
fn workflow_is_the_default_entry_and_project_directory_is_accepted() {
    let example = Path::new(env!("CARGO_MANIFEST_DIR")).join("examples/capture-match");
    for args in [
        vec!["--check"],
        vec![".", "--check"],
        vec!["workflow.json", "--check"],
    ] {
        let output = Command::new(env!("CARGO_BIN_EXE_easycon-run"))
            .current_dir(&example)
            .args(args)
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        assert!(String::from_utf8_lossy(&output.stdout).contains("project validated:"));
    }
}
