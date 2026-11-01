fn main() {
    println!("cargo:rerun-if-changed=../../Info.plist");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("macos") {
        println!(
            "cargo:rustc-link-arg=-Wl,-sectcreate,__TEXT,__info_plist,{}/../../Info.plist",
            env!("CARGO_MANIFEST_DIR")
        );
    }
}
