# BackgroundAutomator

BackgroundAutomator is a Windows desktop utility designed for targeting, inspecting, and automating interactions with background and foreground application windows without moving the physical mouse cursor.

## Documentation

* **System Architecture & Logic (Agent Guide)**: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
* **User Guide (Hướng dẫn sử dụng tiếng Việt)**: [docs/USER_GUIDE.md](docs/USER_GUIDE.md)
* **Development Roadmap & Phases**: [docs/PHASES.md](docs/PHASES.md)

## Current Status: Phase 4 (Hardening + Persistence + Release) — COMPLETE

Phase 1 established the foundation for window enumeration, mouse crosshair targeting, hierarchical HWND resolution, and coordinate translations.
Phase 2 delivered background mouse click dispatching via Win32 `PostMessage`, multi-point sequencing, asynchronous runner loops, and global hotkeys.
Phase 3 delivered a composable macro action engine, background window client-area capture via `PrintWindow`, and color matching with configurable tolerance and timeout.
Phase 4 hardens the entire system: off-UI-thread capture execution, hung-target detection, single-flight capture throttling, durable target descriptors and re-resolution (no live HWNDs persisted), atomic JSON profile persistence, UIPI elevation diagnostics, multi-monitor negative coordinate support, and portable self-contained release builds.

### Key Capabilities in Phase 4
* **Safe Terminal Auto-Confirm**: Automated, unattended approval of explicit CLI/agent confirmation prompts (e.g. `Run this command? > 1. Yes, run command`). Requires explicit allowlist rules; fail-closed by default (no wildcards).
* **Terminal Foreground Input Delivery**:
  * `BackgroundPostMessage`: True non-intrusive background input (`WM_KEYDOWN`/`WM_KEYUP`) for compatible Win32/WinForms controls without taking focus.
  * `ForegroundPulse`: Controlled, momentary foreground activation via `SetForegroundWindow` and `SendInput(Enter)` for modern terminal surfaces (Windows Terminal / ConPTY), immediately followed by revalidation and previous foreground window restoration.
* **Double Revalidation & Anti-Race Guards**: Verifies target existence, visibility, non-minimized state, prompt visibility, option selection, and allowed command twice—before and after foreground activation. Re-checks foreground ownership immediately prior to `SendInput` and guards against duplicate Enter inputs.
* **Authoritative Visible Viewport Detection**: UI Automation inspections strictly prioritize `TextPattern.GetVisibleRanges()` (`TextDetectionScope.VisibleViewportOnly`) to prevent false matches against scrolled-away terminal history.
* **Window Capture Hardening**: Background client-area captures execute asynchronously on the thread pool with `CaptureClientAreaAsync`, guarded by `IsHungAppWindow` and a controlled single-flight gate (`SemaphoreSlim(1,1)`).
* **Durable Target Descriptors & Re-resolution**: Profiles store persistent descriptors (ProcessName, WindowTitle, WindowClass, MatchMode, ChildDescriptor) rather than ephemeral live HWNDs, re-resolving fresh handles across app restarts with ambiguity detection.
* **Atomic Profile Persistence**: Profiles are saved atomically via temporary file writes and replacements in `%LOCALAPPDATA%\BackgroundAutomator\profiles\`. Damaged profiles are isolated without impacting enumeration.
* **UIPI & Privilege Diagnostics**: Active target processes are queried for elevation tokens (`OpenProcessToken`, `TokenElevation`), surfacing UI warnings and a one-click "Restart as Administrator" option when privilege mismatches would drop click messages.
* **Multi-Monitor Support**: Verified signed 32-bit Win32 coordinate translations for secondary monitors positioned at negative virtual desktop coordinates.
* **Resource Soak Verified**: GDI and USER handle allocations verified leak-free across 500+ rapid capture cycles using Win32 `GetGuiResources`.
* **Portable Release**: Single-file, self-contained `win-x64` executable generated with zero external runtime dependencies.

---

## Solution Architecture & Repository Structure

```text
BackgroundAutomator/
├── BackgroundAutomator.sln
├── src/
│   ├── BackgroundAutomator.App/
│   │   ├── Forms/
│   │   │   └── MainForm.cs              # Target Inspector UI & Crosshair interaction
│   │   ├── Program.cs                   # Entry point with PerMonitorV2 initialization
│   │   └── app.manifest                 # OS-level PerMonitorV2 manifest
│   │
│   ├── BackgroundAutomator.Core/
│   │   ├── Coordinates/
│   │   │   └── CoordinateService.cs     # Screen/Client coordinate translations & round-trip validation
│   │   ├── Logging/
│   │   │   └── IAppLogger.cs            # In-memory diagnostic logging abstraction
│   │   └── Targeting/
│   │       ├── HwndFormatter.cs         # 64-bit safe HWND hex formatting (e.g. 0x00000000001203AA)
│   │       ├── TargetPoint.cs           # HWND-bound client coordinate model
│   │       ├── WindowTarget.cs          # Target representation (Root, Target, Parent HWNDs, PID, title, class)
│   │       ├── WindowTargetCandidate.cs # Dropdown candidate metadata
│   │       └── WindowTargetService.cs   # EnumWindows & recursive child HWND resolution
│   │
│   └── BackgroundAutomator.Win32/
│       ├── NativeConstants.cs           # Centralized Win32 message, style, and flag constants
│       ├── NativeTypes.cs               # POINT, RECT, Enum delegates
│       ├── User32.cs                    # P/Invoke definitions for user32.dll & dwmapi.dll
│       ├── Kernel32.cs                  # P/Invoke definitions for kernel32.dll & process helpers
│       └── Gdi32.cs                     # P/Invoke definitions for gdi32.dll
│
├── tests/
│   ├── BackgroundAutomator.Tests/         # Comprehensive xUnit unit & integration tests
│   └── BackgroundAutomator.TestTarget/    # Deterministic WinForms target application with message logging
│
└── README.md
```

### Core Invariants & Architecture Decisions
1. **Centralized Win32 Layer**: All P/Invoke signatures and native structs reside strictly in `BackgroundAutomator.Win32`. No `[DllImport]` statements exist in Forms or Core business logic.
2. **HWND-Bound Client Coordinates**: Client coordinates `(x, y)` have no meaning without an associated HWND. The `TargetPoint` model strictly pairs `IntPtr Hwnd`, `int ClientX`, and `int ClientY`.
3. **Deepest Useful Child Target**: Clicking a button inside a panel inside a form targets the button's HWND with coordinates relative to that button, preserving the root window for process-level identification.
4. **x64 Handle Integrity**: HWND values are treated as native integer pointers (`IntPtr` / `nint`) and formatted as 16-character hexadecimal strings (`0x00000000001203AA`) without 32-bit truncation.
5. **PerMonitorV2 DPI Awareness**: DPI awareness is declared in both the `app.manifest` and at program startup (`Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` and `User32.SetProcessDpiAwarenessContext`), ensuring exact pixel mapping on 100%, 125%, 150%, and multi-monitor scaling setups.

---

## Prerequisites

* Windows 10 (Version 1703+) or Windows 11 (x64)
* .NET 8 SDK or .NET 10 SDK with .NET 8 targeting support

---

## How to Build

From the root `BackgroundAutomator` directory, run:

```powershell
dotnet build BackgroundAutomator.sln
```

---

## How to Execute Tests

Run all unit and integration tests:

```powershell
dotnet test BackgroundAutomator.sln
```

The test suite covers:
* Full 64-bit HWND formatting and hex parsing.
* HWND-bound client coordinate invariants.
* Implicit conversions and geometry calculations for `POINT` and `RECT`.
* Window target metadata and friendly string formatting.
* Coordinate round-trip invariance (`screen -> client -> screen`) and window movement simulations.
* Hierarchical child control resolution across multi-level containers (`Form -> Panel -> Button`).
* Real-process end-to-end integration tests using `BackgroundAutomator.TestTarget.exe`.

---

## How to Run BackgroundAutomator.App

To launch the Target Window Inspector:

```powershell
dotnet run --project src/BackgroundAutomator.App/BackgroundAutomator.App.csproj
```

### Using the Inspector:
1. **Select Window Dropdown**: Choose any running top-level window from the list, or click **Refresh List** to reload.
2. **Crosshair Targeting**: Left-click and hold the **◎ Drag crosshair to target window / control** button, drag the cursor over any control in another application (such as `TestTarget`), and release the mouse to lock onto that control.
3. **Inspector Details**: View the resolved Process Name, Title, PID, Thread ID, Root HWND, Target HWND, Parent HWND, Window Class, Global Screen Coordinates, and Target Client Coordinates.
4. **Resilience**: Move or resize the target window and observe coordinate updates. Close the target window to see the inspector gracefully transition to `Target unavailable (window closed)` without crashing.

---

## How to Run BackgroundAutomator.TestTarget

To launch the deterministic test bench:

```powershell
dotnet run --project tests/BackgroundAutomator.TestTarget/BackgroundAutomator.TestTarget.csproj
```

Features of `TestTarget`:
* **Interactive Controls**: Test Button inside a nested Panel with distinct background colors and borders.
* **Click Counter**: Increments on button click with a reset button.
* **Coordinate Tracker**: Displays real-time cursor position in screen coordinates, form client coordinates, and button client coordinates.
* **Keyboard Logger**: Text box logging keystrokes, virtual key codes, and character codes.
* **Raw Windows Message Log**: Virtualized, double-buffered log retaining up to 1,000 entries with message name, HWND, target control, wParam, lParam, decoded X/Y coordinates, and thread ID.

---

## Known Win32 Limitations & Target Application Compatibility

Targeting and background window interaction depend fundamentally on the input architecture of the target application:

1. **Standard Win32 & WinForms Controls**: Full support. Win32 buttons, edit controls, combo boxes, and nested panels expose distinct HWNDs and process standard Windows messages (`WM_LBUTTONDOWN`, `WM_LBUTTONUP`).
2. **WPF & Modern UI Frameworks (WinUI, Electron, Chromium)**: Applications such as Chrome, Edge, VS Code, Discord, or modern WPF apps often host their entire UI surface inside a single root HWND (`Chrome_RenderWidgetHostHWND`, `HwndHost`, etc.). In these applications, internal buttons do not have individual HWNDs; the entire client area shares the parent HWND, and coordinates are relative to that surface.
3. **DirectInput, Raw Input, and Hardware-Polled Games**: Games and applications that bypass the Windows message queue to read directly from hardware devices (DirectInput, Raw Input API, or exclusive-mode DirectX/Vulkan surfaces) may not respond to window-level message posting.
4. **Security Boundaries (UAC / UIPI)**: BackgroundAutomator cannot inspect or interact with windows belonging to processes running at a higher integrity level (e.g. standard user targeting an Administrator window). Running BackgroundAutomator as Administrator is required if targeting elevated processes.
5. **Native Win32 PrintWindow Non-Cancellability**: Win32 `PrintWindow` calls execute in unmanaged kernel/GDI code and cannot be forcefully aborted by a managed `CancellationToken`. BackgroundAutomator guards against hangs via `User32.IsHungAppWindow` pre-checks, worker thread dispatch (`CaptureClientAreaAsync`), and a single-flight gate (`SemaphoreSlim(1, 1)`) preventing thread buildup.
6. **Desktop Session Requirements**: Global hotkeys (F6/F7) and interactive mouse immobility verification require an active Windows interactive desktop session. Virtual CI environments running without a desktop session cannot execute interactive user-input acceptance tests.

---

## Release Publishing & Portable Distribution

To build the self-contained single-file portable release for Windows x64:

```powershell
dotnet publish src/BackgroundAutomator.App/BackgroundAutomator.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o ./publish
```

This generates `BackgroundAutomator.App.exe` in `./publish/`, which can be run on any 64-bit Windows 10/11 machine without requiring the .NET runtime to be installed.

### Profile Storage Location

Saved profiles are stored as human-readable, schema-versioned JSON files at:
```text
%LOCALAPPDATA%\BackgroundAutomator\profiles\*.json
```
Profiles can also be backed up, restored, or transferred across machines. Target windows will automatically re-resolve when loaded on another system based on the profile's durable `TargetDescriptor`.
# background-automator
