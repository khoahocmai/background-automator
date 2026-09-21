# BackgroundClicker

BackgroundClicker is a Windows desktop utility designed for targeting, inspecting, and automating interactions with background and foreground application windows without moving the physical mouse cursor.

**Development roadmap:** [docs/PHASES.md](docs/PHASES.md)

## Current Status: Phase 3 (Macro Engine + Window Capture + WaitColor) — COMPLETE

Phase 1 established the foundation for window enumeration, mouse crosshair targeting, hierarchical HWND resolution, and coordinate translations.
Phase 2 delivered background mouse click dispatching via Win32 `PostMessage`, multi-point sequencing, asynchronous runner loops, and global hotkeys.
Phase 3 delivers a robust macro action pipeline, background window client-area capture via `PrintWindow`, and color matching with configurable tolerance and timeout.

### Key Capabilities in Phase 3
* **Macro Engine**: Sequential execution of composable `IMacroAction` steps with `MacroRunner`.
* **Action Types**:
  * `ClickAction`: Background single click at client coordinate.
  * `DoubleClickAction`: Background double click at client coordinate.
  * `DelayAction`: Non-blocking, cancellation-aware asynchronous pause.
  * `WaitColorAction`: Polls window client area until target RGB color is observed within tolerance.
* **Window Capture Service**: Background client-area capture using Win32 `PrintWindow` with `PW_CLIENTONLY` and strict GDI lifecycle resource management (DC, Bitmap, and Object cleanup).
* **Coordinate Invariant**: Both window capture pixel queries and background click targets strictly share the identical client coordinate system where `(0, 0)` is the top-left of the target client area.
* **Interactive Macro UI**: Visual sequence builder supporting action reordering (Move Up/Down), addition, deletion, and real-time execution monitoring with UI thread marshalling.
* **Deterministic Test Bench**: Extended `BackgroundClicker.TestTarget` with dedicated color panels, color change buttons, and automated delayed color transitions for regression testing.

---

## Solution Architecture & Repository Structure

```text
BackgroundClicker/
├── BackgroundClicker.sln
├── src/
│   ├── BackgroundClicker.App/
│   │   ├── Forms/
│   │   │   └── MainForm.cs              # Target Inspector UI & Crosshair interaction
│   │   ├── Program.cs                   # Entry point with PerMonitorV2 initialization
│   │   └── app.manifest                 # OS-level PerMonitorV2 manifest
│   │
│   ├── BackgroundClicker.Core/
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
│   └── BackgroundClicker.Win32/
│       ├── NativeConstants.cs           # Centralized Win32 message, style, and flag constants
│       ├── NativeTypes.cs               # POINT, RECT, Enum delegates
│       ├── User32.cs                    # P/Invoke definitions for user32.dll & dwmapi.dll
│       ├── Kernel32.cs                  # P/Invoke definitions for kernel32.dll & process helpers
│       └── Gdi32.cs                     # P/Invoke definitions for gdi32.dll
│
├── tests/
│   ├── BackgroundClicker.Tests/         # Comprehensive xUnit unit & integration tests
│   └── BackgroundClicker.TestTarget/    # Deterministic WinForms target application with message logging
│
└── README.md
```

### Core Invariants & Architecture Decisions
1. **Centralized Win32 Layer**: All P/Invoke signatures and native structs reside strictly in `BackgroundClicker.Win32`. No `[DllImport]` statements exist in Forms or Core business logic.
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

From the root `BackgroundClicker` directory, run:

```powershell
dotnet build BackgroundClicker.sln
```

---

## How to Execute Tests

Run all unit and integration tests:

```powershell
dotnet test BackgroundClicker.sln
```

The test suite covers:
* Full 64-bit HWND formatting and hex parsing.
* HWND-bound client coordinate invariants.
* Implicit conversions and geometry calculations for `POINT` and `RECT`.
* Window target metadata and friendly string formatting.
* Coordinate round-trip invariance (`screen -> client -> screen`) and window movement simulations.
* Hierarchical child control resolution across multi-level containers (`Form -> Panel -> Button`).
* Real-process end-to-end integration tests using `BackgroundClicker.TestTarget.exe`.

---

## How to Run BackgroundClicker.App

To launch the Target Window Inspector:

```powershell
dotnet run --project src/BackgroundClicker.App/BackgroundClicker.App.csproj
```

### Using the Inspector:
1. **Select Window Dropdown**: Choose any running top-level window from the list, or click **Refresh List** to reload.
2. **Crosshair Targeting**: Left-click and hold the **◎ Drag crosshair to target window / control** button, drag the cursor over any control in another application (such as `TestTarget`), and release the mouse to lock onto that control.
3. **Inspector Details**: View the resolved Process Name, Title, PID, Thread ID, Root HWND, Target HWND, Parent HWND, Window Class, Global Screen Coordinates, and Target Client Coordinates.
4. **Resilience**: Move or resize the target window and observe coordinate updates. Close the target window to see the inspector gracefully transition to `Target unavailable (window closed)` without crashing.

---

## How to Run BackgroundClicker.TestTarget

To launch the deterministic test bench:

```powershell
dotnet run --project tests/BackgroundClicker.TestTarget/BackgroundClicker.TestTarget.csproj
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
4. **Security Boundaries (UAC / UIPI)**: BackgroundClicker cannot inspect or interact with windows belonging to processes running at a higher integrity level (e.g. standard user targeting an Administrator window). Running BackgroundClicker as Administrator is required if targeting elevated processes.

---

## Future Roadmap (Scheduled for Phase 4)

* Profile persistence & configuration storage (JSON / registry)
* UIPI / privilege mismatch detection and optional restart-as-admin UX
* Target re-resolution across application restarts (process/title/class matching)
* Self-contained single-file portable release (`win-x64`)
