# AGENTS.md — EasyCon

## Project overview

EasyCon is a Nintendo Switch automation tool with virtual controller, image recognition (OpenCV + custom EzCv), and a custom ECS scripting language. .NET 10.0, C# (LangVersion=preview).

**Entry points**: `src/EasyCon2.Avalonia` (GUI), `src/EasyCon2.CLI` (CLI), `src/EasyCon2` (legacy WPF, deprecated).

## Build & test commands

```powershell
# Build entire solution
dotnet build

# Run all tests (Release)
ci\test.bat
# or
dotnet test EasyCon2.slnx -c Release

# Run tests in Debug
ci\test.bat Debug

# Run a single test project
dotnet test test/EasyCon.Tests/EasyCon.Tests.csproj

# Check formatting (CI blocks merges on failure)
dotnet format --verify-no-changes

# Auto-fix formatting
dotnet format

# Publish (Windows x64)
ci\windows-x64.bat
```

**Solution file** is `EasyCon2.slnx` (XML-based MSBuild solution, .NET 10+). Not `.sln`.

## Solution structure

| Directory | Role |
|---|---|
| `src/EasyCon.Core` | Core abstractions, interfaces, models |
| `src/EasyCon.Device` | Hardware device communication (serial) |
| `src/EasyCon.Capture` | Screen/image capture |
| `src/EasyCon.Script` | ECS script parser, compiler, runtime |
| `src/EasyCon.Script.Jit` | JIT compiler for ECS scripts |
| `src/EzCv` | Custom OpenCV wrapper (Zig native interop) |
| `src/EzTesseract` | OCR (Tesseract wrapper) |
| `src/EasyCon.Lsp` | LSP language server for ECS scripts |
| `src/EasyCon.Server` | HTTP/WebSocket server for remote control |
| `src/EasyCon2.Avalonia` | Avalonia GUI (MVVM) |
| `src/EasyCon2.Avalonia.Core` | Shared Avalonia viewmodels/logic |
| `src/EasyCon2.UI.Common` | Shared UI resources, styles |
| `src/EasyCon.WinInput` | Windows input simulation |
| `src/EasyCon.SDLInput` | Cross-platform input via SDL3 |
| `test/EasyCon.Tests` | Core/Script tests (NUnit) |
| `test/EasyCon.Lsp.Tests` | LSP tests |
| `test/EasyCon.WinInput.Tests` | Windows input tests |

## Code conventions (enforced by .editorconfig)

- **Indent**: 4 spaces, no tabs
- **Line endings**: CRLF
- **No final newline** (`insert_final_newline = false`) — unusual, don't "fix" it
- **Private fields**: `_camelCase` prefix
- **Explicit types**: `csharp_style_var_* = false` — prefer explicit types over `var`
- **Nullable enabled** solution-wide
- **No `this.` qualification** for fields/properties/methods
- **Braces**: Allman style (`csharp_new_line_before_open_brace = all`)
- **Unused parameters**: `all` severity (will flag as suggestion)

## Package management

**Central package management** is enabled (`Directory.Packages.props`). Individual `.csproj` files have `<PackageReference>` with no `Version` attribute. Add new packages to `Directory.Packages.props`, not inline.

## MVVM architecture (Avalonia UI)

The Avalonia GUI (`EasyCon2.Avalonia` / `EasyCon2.Avalonia.Core`) follows strict MVVM:

- **ViewModel must NOT reference any Avalonia control types** (Window, Control, TextBox, etc.)
- **View → ViewModel**: prefer bindings (`{Binding}`, `{x:Bind}`), avoid code-behind event subscriptions
- **ViewModel → View**: use `[ObservableProperty]` (CommunityToolkit.Mvvm) or `AvaloniaProperty` with bindings
- **Code-behind** is only for: platform APIs (file dialogs, drag-drop), layout (SizeChanged), visual tree init (FoldingManager, LSP). No business logic.
- **Custom controls**: use `AvaloniaProperty.RegisterDirect` / `Register`, not plain CLR properties + events

## Testing

- **Framework**: NUnit (NOT xUnit or MSTest)
- Test projects: `EasyCon.Tests`, `EasyCon.Lsp.Tests`, `EasyCon.WinInput.Tests`, `EasyCon2.Avalonia.Core.Tests`
- Use `[Test]` attribute, not `[Fact]`

## CI pipeline

- **format** job runs first (blocking): `dotnet format --verify-no-changes`
- **Auto Fix Format** workflow auto-commits formatting fixes on PRs to main/dev
- Main branch: Release build + test + publish artifact
- Dev branch: Debug build + test only
- PR format commits use `[skip ci]` to avoid recursive triggers

## Native code (EzCv)

`src/EzCv` uses Zig for native OpenCV interop. Build artifacts live in `.zig-cache/` and `src/EzCv/native/out_*/`. Changes to Zig source require a Zig toolchain to rebuild native binaries. Prebuilt binaries may be expected for normal .NET workflows.

## Documentation

- `docs/Script.md` — ECS scripting language reference
- `docs/Framework.md` — system architecture
- `docs/VM1.md` / `docs/VM2.md` — virtual machine instruction sets
- `docs/GETTING_STARTED.md` — user setup guide
- `src/EasyCon2.Avalonia/DOCUMENTATION_INDEX.md` — UI docs index
