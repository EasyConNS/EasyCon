fn main() {
    println!("cargo:rerun-if-changed=Info.plist");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("macos") {
        for binary in ["easycon-sense", "easycon-run"] {
            println!(
                "cargo:rustc-link-arg-bin={binary}=-Wl,-sectcreate,__TEXT,__info_plist,{}/Info.plist",
                env!("CARGO_MANIFEST_DIR")
            );
        }
    }
}
