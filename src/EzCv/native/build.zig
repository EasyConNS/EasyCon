// build.zig — 用 Zig 编译 ezcv_native 胶水层，动态链接 OpenCV world
//
// 用法:
//   zig build -Dtarget=aarch64-macos -Doptimize=ReleaseFast -Dopencv_dir=<prefix>
//
// 其中 <prefix> 是 OpenCV install 目录 (含 include/ 与 lib/)，例如
//   ../Depend/opencv/build/install
//
// 产物: zig-out/lib/libezcv_native.{dylib|so} 或 zig-out/bin/ezcv_native.dll
//
// install_name 的归一 (把 OpenCV 版本化 soname libopencv_world.500.dylib 改为
// 不带版本号的 libopencv_world.dylib) 由外层 build_ezcv.sh 用 install_name_tool
// 完成；这里只负责把 rpath(@loader_path) 烧进二进制，使两个 dylib 同目录即可加载。

const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const opencv_dir = b.option([]const u8, "opencv_dir",
        "OpenCV install 前缀 (含 include/ 与 lib/，例如 ../opencv/build/install)") orelse {
        std.debug.print("error: 缺少 -Dopencv_dir=<path>\n", .{});
        std.debug.print("       指向 OpenCV install 前缀 (含 include/opencv5 与 lib/libopencv_world.*)\n", .{});
        return;
    };

    // 根模块：C++17，链接 libc++
    const mod = b.createModule(.{
        .target = target,
        .optimize = optimize,
        .link_libcpp = true,
    });

    // 编译 ezcv_native.cpp
    mod.addCSourceFile(.{
        .file = b.path("ezcv_native.cpp"),
        .flags = &.{ "-std=c++17", "-O2" },
    });

    // OpenCV 头文件与库 (动态链接 opencv_world)
    const inc = b.pathJoin(&.{ opencv_dir, "include", "opencv5" });
    const libdir = b.pathJoin(&.{ opencv_dir, "lib" });
    mod.addIncludePath(.{ .cwd_relative = inc });
    mod.addLibraryPath(.{ .cwd_relative = libdir });
    mod.linkSystemLibrary("opencv_world", .{ .preferred_link_mode = .dynamic });

    // 烧入 rpath = @loader_path：运行时在 ezcv 自身目录查找依赖
    // (macOS @loader_path / Linux $ORIGIN 语义)
    mod.addRPathSpecial("@loader_path");

    // 产出共享库 (Zig 0.17: addLibrary + linkage=.dynamic)
    const lib = b.addLibrary(.{
        .name = "ezcv_native",
        .root_module = mod,
        .linkage = .dynamic,
    });
    b.installArtifact(lib);
}
