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
| `src/EasyCon.Core` | **Composition root** + capability model (`Capabilities/`: CapabilitySet + IPadInput/IConsoleIo/IFileSystem/ICaptureSource/IVisionService/IOcrService/IInference/IEnvironment) + host assembler (`Hosting/`: ScriptHostAssembler/ScriptCompileProfiles) + script engine surface (`Script/`: IScriptEngine/IScriptSession) + execution bridge (`Runner/`: EcxVm) + config (`Config/`: ConfigManager) + LLM client (`LLM/`) |
| `src/EasyCon.Device` | Hardware device communication (serial); zero project references |
| `src/EasyCon.Capture` | Screen/image capture; vision matching; OCR engine cache |
| `src/EasyCon.Script` | ECS script parser, binder, SSA, bytecode compiler/linker, CLI-side interpreter; zero project references |
| `src/EasyCon.Vm` | Native C VM (`native/ecs-vm`, C99) executing .ecx bytecode on MCU. **Not in `EasyCon2.slnx`** — built by `ci/build-vm.sh` and on the fly by `test/EasyCon.Tests/Support/CvmRunner.cs` |
| `src/EzTesseract` | OCR (hand-written Tesseract/Leptonica bindings); zero project references |
| `src/EasyCon.Lsp` | LSP language server for ECS scripts (reuses Script's syntax tree only) |
| `src/EasyCon.SDLInput` | Cross-platform input via SDL3 (the only production input backend) |
| `src/EasyCon2.Avalonia.Core` | Pure VM/logic layer — zero Avalonia package references |
| `src/EasyCon2.Avalonia` | Avalonia GUI (MVVM): views/controls + platform service implementations + `VPad/` |
| `src/EasyCon2.UI.Common` | Legacy resx resources only (see "Layering" below — mostly vestigial) |
| `test/EasyCon.Tests` | Core/Script tests (NUnit), includes C VM ↔ C# interpreter cross-validation |
| `test/EasyCon.Lsp.Tests` | LSP tests |
| `test/EasyCon.SDLInput.Tests` | Input→HID mapping tests (fake device connection) |
| `test/EasyCon2.Avalonia.Core.Tests` | Agent/MCP/LSP-client/script-service tests |
| `test/EasyCon2.Avalonia.UiTests` | Headless Avalonia render tests |
| `tools/OpenCvDnnDemo` | OpenCvSharp5 DNN experiment (in slnx under `/Demo/`) |

## Layering and the composition root

The layering does **not** match the "Core is the bottom layer" reading of its name. The real graph is:

```
EasyCon.Script   EasyCon.Device   EasyCon.Capture ──► EzTesseract     (leaf libraries, zero refs)
      ▲               ▲                  ▲
      └───────────────┴──────────────────┘
                      │
                EasyCon.Core            ← capability PORTS + COMPOSITION ROOT
                      ▲
      ┌───────────────┼──────────────────┐
EasyCon.SDLInput   EasyCon.Lsp   EasyCon2.Avalonia.Core ──► EasyCon2.Avalonia
EasyCon2.CLI ──► EasyCon.Core + EasyCon.Lsp
```

Rules to respect when changing code:

- **`EasyCon.Core` is the composition root**, not a leaf: it references Script/Device/Capture and pulls
  OpenCV/Tesseract through them. Do not add a project reference *to* Core from Script/Device/Capture/EzTesseract —
  those four must stay zero-reference leaves.
- **Never construct `CapabilitySet` outside `Hosting/ScriptHostAssembler`.** Hosts (GUI/CLI) supply only
  "raw material" (`ScriptHostContext`: pad, frame/ROI/label delegates, console, args); the assembler owns
  adapter wrapping, defaults (Environment/Files/OCR/DNN) and resource teardown (`CapabilityLease`).
- **Never hand-write `CompileOptions` literals.** Use `ScriptCompileProfiles.Desktop` (PC wide slots,
  desktop interpreter only — the image cannot be serialized) or `ScriptCompileProfiles.Portable`
  (frozen 8-bit slots, the only MCU-distributable and cacheable profile).
- DI *container* is deliberately absent: composition is explicit constructor passing from
  `App.axaml.cs` (GUI) and `Program.cs` (CLI).
- Two known warts, do not spread them: `EasyCon2.Avalonia/ViewModels/` holds a few Avalonia-typed VMs
  outside the compiler-enforced purity zone, and `EasyCon2.UI.Common` is a vestigial resx-only project.

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

- **ViewModel must NOT reference any Avalonia control types** (Window, Control, TextBox, etc.). `EasyCon2.Avalonia.Core` carries no Avalonia package references, so this constraint is **enforced by the compiler** — a ViewModel that starts using Avalonia types simply fails to build. The few Avalonia-typed VMs (`MonitorViewModel`, `ESPConfigViewModel`, `KeyMappingViewModel`, `FileTreeViewModel`, `MainWindowViewModel`, `ControllerConnectionViewModel`) live in `EasyCon2.Avalonia/ViewModels/`, i.e. deliberately outside the enforced zone.
- **View resolution is explicit, not convention-based**: views are declared in axaml with `DataContext="{Binding ...}"`, or constructed by `WindowService`. The registered `ViewLocator` matches almost nothing and is effectively dead code — do not rely on `FooViewModel → FooView` name mapping. Views for Core-layer VMs live in the `EasyCon2.Avalonia` project but keep `EasyCon2.Avalonia.Core.*` namespaces so axaml `using:` and `x:DataType` resolve.
- **View → ViewModel**: prefer bindings (`{Binding}`, `{x:Bind}`), avoid code-behind event subscriptions
- **ViewModel → View**: use `[ObservableProperty]` (CommunityToolkit.Mvvm) or `AvaloniaProperty` with bindings
- **Code-behind** is only for: platform APIs (file dialogs, drag-drop), layout (SizeChanged), visual tree init (FoldingManager, LSP). No business logic.
- **Custom controls**: use `AvaloniaProperty.RegisterDirect` / `Register`, not plain CLR properties + events

## Testing

- **Framework**: NUnit (NOT xUnit or MSTest)
- Test projects: `EasyCon.Tests`, `EasyCon.Lsp.Tests`, `EasyCon.SDLInput.Tests`, `EasyCon2.Avalonia.Core.Tests`, `EasyCon2.Avalonia.UiTests`
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

**Current-state specs (kept in sync with code — trust these):**

- `docs/Pipeline.md` — unified compilation pipeline and single-source-of-truth landing points
- `docs/ModuleSystem.md` — interface-based independent compilation + content-addressed cache
- `docs/VM2.md` — bytecode/instruction-set spec (the live VM)
- `docs/VmSemanticContract.md` — S-01..S-21 dual-end (C# interpreter ↔ C VM) semantic contract
  (S-20 "pinned constants" and S-21 "capability-default degradation" are implemented)
- `docs/EcmEcxFormat.md` — ECX1 flat image + ECM1 flat module-cache bit-level format
- `docs/Script.md` — ECS scripting language reference
- `docs/Functions.md` — script function handbook (script-author-facing, CN)
- `docs/McuBytecodeDelivery.md` — plan of record for compiling and flashing MCU bytecode

**Outline & decision record (merged; replaces the former root analysis docs):**

- `PROJECT_OUTLINE.md` — current-state map, performance ladder & four-way VM comparison verdict,
  format/container and C-VM decision records, v2.3 alignment status, compiler-modernization
  plan (pending: M3/M4, lib-import v2 N1-N3), gates, pitfalls, backlog

**Historical / partially stale (verify against code before relying on them):**

- `docs/Framework.md` — layering overview; has been corrected for the composition-root reality, but still the softest doc
- `ARCHITECTURE_REVIEW_REPORT.md` — 2026-09-26 audit (94 findings). Most P0/P1 items were landed in
  `ac7e16c`; read it as a **historical checklist**, not a current defect list.
- `docs/GETTING_STARTED.md` — user setup guide
