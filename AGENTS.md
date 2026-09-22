# AGENTS.md — EasyCon

## Project overview

EasyCon is a Nintendo Switch automation tool with virtual controller, image recognition (OpenCV via OpenCvSharp5), and a custom ECS scripting language. .NET 10.0, C# (LangVersion=preview).

**Entry points**: `src/EasyCon2.Avalonia` (GUI), `src/EasyCon2.CLI` (CLI).

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
| `src/EasyCon.Core` | Core abstractions; capability model (`Capabilities/`: CapabilitySet + IPadInput/IConsoleIo/IFileSystem/ICaptureSource/IVisionService/IOcrService/IInference); script engine surface (`Script/`: IScriptEngine/IScriptSession) |
| `src/EasyCon.Device` | Hardware device communication (serial) |
| `src/EasyCon.Capture` | Screen/image capture |
| `src/EasyCon.Script` | ECS script parser, compiler, runtime |
| `src/EasyCon.Vm` | Native C VM (ecs-vm, C99) executing .ecx bytecode on MCU |
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
- Test projects: `EasyCon.Tests`, `EasyCon.Lsp.Tests`, `EasyCon.WinInput.Tests`, `EasyCon2.Avalonia.Core.Tests`, `EasyCon2.Avalonia.UiTests`, `EasyCon.SDLInput.Tests`
- Use `[Test]` attribute, not `[Fact]`

### Fake device connection (no-hardware device tests)

Device logic (queue, throttle, report building, serialization) can be exercised without an MCU by injecting a fake connection:

- `EasyCon.Device` exposes the seam: `IConnection` is `public`, and `NintendoSwitch.CreateConnection(connStr, baudrate)` is `protected virtual` (its default returns `TTLSerialClient`). The production enqueue/dequeue path is unchanged.
- Subclass `NintendoSwitch` in a test project and override `CreateConnection` to return a fake whose `Write(params byte[])` records the payload — that payload is exactly the HID packet sent to the MCU (`SwitchReport.GetBytes()`). Assert on this **dequeue output**, not on internal state.
- The fake must raise `StatusChanged(Status.Connected)` inside `Connect()` so `TryConnect` succeeds and the background device write loop starts.
- The write loop is asynchronous (30 ms `MINIMAL_INTERVAL`); wait for recorded bytes with a timeout instead of asserting immediately.
- `SdlEventLoop` and `SdlKeyboardInputBinder` can be constructed and driven via `HandleKeyEvent` **without loading native SDL**; native SDL is only touched by `SdlEventLoop.Start()`, so never call it in tests.
- Reference implementation: `test/EasyCon.SDLInput.Tests` (`FakeConnection`, `TestSwitch`, `KeyboardMappingHidTests`). Reuse this pattern for any test that needs device logic without hardware.

### Headless UI tests (Avalonia.Headless)

`test/EasyCon2.Avalonia.UiTests` renders real controls off-screen (no window server) to assert layout and to produce PNGs for visual review:

- Initialize once per fixture with `TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting()`, then add `FluentTheme` plus `Resources/Styles/EasyConWorkbenchTheme.axaml` to `Application.Current.Styles`.
- Build the window, set `DataContext`, call `Show()`, then `CaptureRenderedFrame()` to force a render pass and capture it.
- **Never call `WriteableBitmap.Save` to write a screenshot.** It goes through `Avalonia.Skia.Helpers.ImageSavingHelper` → the SkiaSharp native PNG encoder, which **crashes the test host natively and intermittently** in this headless setup (`Fatal error` at `sk_pngencoder_encode`). The symptom is a run that silently executes fewer tests and exits non-zero, so `ci\test.bat` (and therefore `ci.yml`) turns red at random. Use the `SaveFrame` helper in `KeyMappingWindowRenderTests` instead: it encodes with SkiaSharp directly (`SKImage.FromPixels` + `Encode`), the same path the stable `SvgRenderSmokeTests` uses. Root cause is not yet identified (managed and native SkiaSharp are both 4.148.0, matching `Avalonia.Skia` 12.0.4 — not a version mismatch).
- Screenshot tests carry `[Explicit]` + `[Category("Manual")]` so they never run in CI. Run them on demand with `dotnet test test\EasyCon2.Avalonia.UiTests -c Release --filter "TestCategory=Manual"`; the PNGs land in `%TEMP%\opencode\svgqa\`.

## CI pipeline

- **format** job runs first (blocking): `dotnet format --verify-no-changes`
- **Auto Fix Format** workflow auto-commits formatting fixes on PRs to main/dev
- Main branch: Release build + test + publish artifact
- Dev branch: Debug build + test only
- PR format commits use `[skip ci]` to avoid recursive triggers

## Native code (OpenCV)

OpenCV bindings come from the `OpenCvSharp5` NuGet packages (OpenCV 5.0); native binaries ship via `OpenCvSharp5.runtime.*` packages referenced by `src/EasyCon.Capture` (and `tools/OpenCvDnnDemo`).

## Documentation

- `docs/Script.md` — ECS scripting language reference
- `docs/Functions.md` — script function handbook (script-author-facing, CN)
- `docs/Framework.md` — system architecture
- `docs/VM1.md` / `docs/VM2.md` — virtual machine instruction sets
- `docs/GETTING_STARTED.md` — user setup guide
- `src/EasyCon2.Avalonia/DOCUMENTATION_INDEX.md` — UI docs index
