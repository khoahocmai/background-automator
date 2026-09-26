# BackgroundAutomator — System Architecture, Logic & Agent Guide

> **Document Purpose**: This document serves as the authoritative technical reference and architectural specification for the BackgroundAutomator codebase. It is designed so that any AI agent or software engineer can read it and immediately understand the entire system architecture, subsystem mechanics, design invariants, data flows, and code conventions.

---

## 1. Executive Summary & Core Philosophy

**BackgroundAutomator** is a high-reliability Windows desktop utility built on **.NET 8 (C#)** and **WPF (Fluent Design)**, designed to inspect, target, and automate mouse clicks and macros on background or foreground application windows **without moving or hijacking the user's physical mouse cursor**.

### Fundamental Philosophy & Non-Negotiable Invariants
1. **Zero Physical Cursor Interference**: The application never calls `SetCursorPos` or `mouse_event` / `SendInput` to move the physical hardware cursor. All automation messages are injected directly into the target window's Win32 message queue via unmanaged `PostMessage`.
2. **HWND-Bound Client Coordinates**: Screen coordinates are volatile and break when windows move or resize. In BackgroundAutomator, all click and capture coordinates are strictly client coordinates bound to a specific window handle (`HWND`), where `(0, 0)` is the top-left corner of that specific control's client area.
3. **Deepest Useful Child Target**: Rather than dispatching clicks to a top-level window and guessing where internal controls sit, BackgroundAutomator recursively resolves the deepest child control HWND (e.g. button inside a panel inside a form).
4. **No Ephemeral Live HWNDs in Persistent Storage**: Window handles (`HWND`) are ephemeral operating system pointers that change every time an application restarts. Persistent profiles store durable **Target Descriptors** (Process Name, Window Class, Window Title, Title Match Mode, and Child Control Descriptors). Fresh HWNDs are dynamically re-resolved at runtime.
5. **Strict GDI Resource Lifecycle**: All GDI device contexts, bitmaps, and selected objects created during background client-area captures are tracked and freed in reverse order of creation inside guarded `finally` blocks, preventing resource and handle leaks.

---

## 2. High-Level System Architecture & Solution Layering

The solution follows a strict, unidirectional layered architecture:

```
┌─────────────────────────────────────────────────────────────┐
│                  BackgroundAutomator.App                      │
│   (WPF Fluent UI, MVVM ViewModels/Views, Hotkeys)           │
└──────────────────────────────┬──────────────────────────────┘
                               │ references
┌──────────────────────────────▼──────────────────────────────┐
│                  BackgroundAutomator.Core                     │
│  (Targeting, Coordinates, Clicking, Runners, Macro Engine,  │
│   Window Capture, Profiles & Persistence, Security/UIPI,    │
│   Approval Engine, Keyboard & Foreground Pulse, UIA Text)   │
└──────────────────────────────┬──────────────────────────────┘
                               │ references
┌──────────────────────────────▼──────────────────────────────┐
│                  BackgroundAutomator.Win32                    │
│    (Centralized P/Invoke: User32, Gdi32, Kernel32, Advapi32,│
│     SendInput, Native Constants, Structs, Message Packing)  │
└─────────────────────────────────────────────────────────────┘

                  ┌────────────────────────┐
                  │    Test Ecosystem      │
                  ├────────────────────────┤
                  │ BackgroundAutomator.     │
                  │   TestTarget (WinForms)│
                  │ BackgroundAutomator.     │
                  │   Tests (xUnit / 246)  │
                  └────────────────────────┘
```

### Dependency Rules:
- **`BackgroundAutomator.Win32`** has **zero** dependencies on other solution projects. All native Win32 API declarations, structures (`POINT`, `RECT`, `INPUT`), and flags live here. No other project may contain raw `[DllImport]` declarations.
- **`BackgroundAutomator.Core`** depends only on `BackgroundAutomator.Win32`. It contains pure domain logic, background worker loops, serialization models, and abstractions. It has **no dependency** on `BackgroundAutomator.App` or WinForms UI controls.
- **`BackgroundAutomator.App`** depends on `BackgroundAutomator.Core` and `BackgroundAutomator.Win32`. It hosts the WPF Fluent views, MVVM view models, data binding, and global keyboard hotkeys.
- **`BackgroundAutomator.TestTarget`** is an isolated, deterministic WinForms harness exposing verifiable controls, click counters, color panels, and a raw Windows message logger.
- **`BackgroundAutomator.Tests`** executes automated unit and integration tests against Core and real spawned processes of `TestTarget`.

---

## 3. Subsystem Breakdown & Implementation Details

```
src/
├── BackgroundAutomator.Win32/          # Native Interop Layer
├── BackgroundAutomator.Core/           # Core Domain & Logic
│   ├── Approval/                     # Command Prompt Parsing & Safe Approval Engine
│   ├── Capture/                      # GDI Window Capture & Color Inspection
│   ├── Clicking/                     # PostMessage Background Click Engine
│   ├── Coordinates/                  # Screen <-> Client Coordinate Translation
│   ├── Keyboard/                     # Foreground Keyboard & Window Focus Services
│   ├── Logging/                      # Diagnostic Logger Abstraction
│   ├── Macro/                        # Composable Action Engine & Runner
│   ├── Profiles/                     # Profile Schema, Serialization & Atomic Storage
│   ├── Runner/                       # Asynchronous Simple Click Runner Loop
│   ├── Security/                     # Process Elevation & UIPI Detection
│   ├── Targeting/                    # Window Inspection, Resolution & Descriptors
│   └── TextDetection/                # Windows UI Automation Text Extraction
└── BackgroundAutomator.App/            # WPF Fluent Presentation Layer
    ├── Views/                        # Fluent Pages (Target, Simple, Macro, Profiles, Diag)
    ├── ViewModels/                   # CommunityToolkit MVVM ViewModels
    ├── Models/                       # Presentation-layer Models
    ├── Converters/                   # XAML Binding Converters
    ├── Services/                     # Navigation & Page Service
    └── Hotkeys/                      # System-wide Win32 Global Hotkeys (F6/F7)
```

### 3.1. Win32 Native Interop Layer (`BackgroundAutomator.Win32`)

Centralizes all unmanaged interop definitions and low-level bitwise helpers:
- **[User32.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/User32.cs)**: Window enumeration (`EnumWindows`, `EnumChildWindows`), hierarchy inspection (`GetParent`, `GetAncestor`, `WindowFromPoint`, `RealChildWindowFromPoint`), coordinates (`ScreenToClient`, `ClientToScreen`, `GetClientRect`), messaging (`PostMessage`, `SendMessage`), foreground management (`SetForegroundWindow`, `GetForegroundWindow`, `IsIconic`), input injection (`SendInput`), capture (`PrintWindow`, `GetDC`, `ReleaseDC`, `IsHungAppWindow`), hotkeys (`RegisterHotKey`, `UnregisterHotKey`), DPI (`SetProcessDpiAwarenessContext`, `GetDpiForWindow`), and DWM (`DwmGetWindowAttribute` for `DWMWA_CLOAKED`).
- **[Gdi32.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/Gdi32.cs)**: `CreateCompatibleDC`, `CreateCompatibleBitmap`, `SelectObject`, `DeleteObject`, `DeleteDC`.
- **[Kernel32.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/Kernel32.cs)**: Process querying (`OpenProcess`, `CloseHandle`), thread information, error codes.
- **[Advapi32.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/Advapi32.cs)**: Token security queries (`OpenProcessToken`, `GetTokenInformation`).
- **[SendInputHelper.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/SendInputHelper.cs)**:
  - Dispatches synthesized hardware keyboard input packets (`INPUT` with `INPUT_KEYBOARD`) via `User32.SendInput`.
  - Dispatches discrete key-down and key-up pairs (`SendKeyDownUp`, `SendEnter`).
- **[KeyboardMessageHelper.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/KeyboardMessageHelper.cs)**:
  - Generates correct 32-bit `lParam` bitfields for `WM_KEYDOWN` and `WM_KEYUP` background messages, incorporating virtual scan codes via `User32.MapVirtualKey`.
- **[MouseMessageHelper.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/MouseMessageHelper.cs)**:
  - Packs signed 16-bit coordinates into a 32-bit `LPARAM`:
    ```csharp
    uint low = (ushort)(short)x;
    uint high = (ushort)(short)y;
    return unchecked((IntPtr)(int)(low | (high << 16)));
    ```
  - Unpacks signed 16-bit coordinates safely preserving negative monitor spaces:
    ```csharp
    int x = unchecked((short)((long)lParam & 0xFFFF));
    int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
    ```
- **[NativeTypes.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Win32/NativeTypes.cs)**: Value-type structs `POINT` and `RECT` (with implicit conversions to/from `System.Drawing`), and `INPUT`, `KEYBDINPUT`, `MOUSEINPUT`, `HARDWAREINPUT` with explicit 64-bit alignment union layout.

---

### 3.2. Targeting Subsystem (`BackgroundAutomator.Core.Targeting`)

Responsible for discovering, inspecting, and resolving target windows.

#### Core Models & Services:
1. **[TargetPoint](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Targeting/TargetPoint.cs)**:
   - Immutable record pairing `IntPtr Hwnd`, `int ClientX`, and `int ClientY`.
   - Invariant: A coordinate point is meaningless without its associated window handle.
2. **[WindowTarget](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Targeting/WindowTarget.cs)**:
   - Rich snapshot of a live target: `RootHwnd`, `TargetHwnd`, `ParentHwnd`, `ProcessId`, `ThreadId`, `ProcessName`, `WindowTitle`, `WindowClass`, `ScreenBounds`, `ClientBounds`.
   - Computes friendly display names for the UI.
3. **[WindowTargetService](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Targeting/WindowTargetService.cs)**:
   - `EnumerateTopLevelWindows`: Uses `User32.EnumWindows` to find visible, uncloaked windows with non-empty titles.
   - `ResolveTargetFromScreenPoint(POINT screenPoint)`:
     - Uses `User32.WindowFromPoint` to find the window under the cursor.
     - Resolves the root top-level window via `User32.GetAncestor(GA_ROOT)`.
     - Recursively drills down via `User32.RealChildWindowFromPoint` and `ChildWindowFromPointEx` to locate the deepest nested child control (button, edit box, panel) at that screen coordinate.
4. **[TargetDescriptor](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Targeting/TargetDescriptor.cs)** & **`ChildTargetDescriptor`**:
   - The persistent representation of a target.
   - Captures `ProcessName`, `WindowTitle`, `WindowClass`, `TitleMatchMode` (`Exact`, `Contains`, `StartsWith`, `Any`), and optional child control selectors (`ControlClass`, `ControlText`, `ControlIndex`).
   - Guarantees zero live HWND storage.
5. **[TargetResolver](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Targeting/TargetResolver.cs)**:
   - Resolves a `TargetDescriptor` back into a live `TargetResolutionResult`.
   - **Ambiguity Detection**: If multiple running top-level windows match the criteria, returns `TargetResolutionStatus.Ambiguous` with the list of candidates, preventing accidental click dispatch to the wrong window.
   - If a child descriptor is present, uses `User32.EnumChildWindows` to find and index matching child controls.
6. **[HwndFormatter](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Targeting/HwndFormatter.cs)**:
   - Formats `IntPtr` as an explicit 16-character 64-bit hex string (`0x00000000001203AA`), preventing 32-bit truncation errors on x64 platforms.

---

### 3.3. Coordinate Subsystem (`BackgroundAutomator.Core.Coordinates`)

- **[CoordinateService](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Coordinates/CoordinateService.cs)**:
  - Translates between global screen coordinates and window client coordinates via `User32.ScreenToClient` and `User32.ClientToScreen`.
  - Verifies coordinate round-trip consistency (`Screen -> Client -> Screen`).
  - Correctly supports multi-monitor setups where secondary monitors sit at negative virtual desktop coordinates (e.g. `X = -1920, Y = -1080`).
- **DPI Awareness**:
  - Configured at the operating system manifest level (`app.manifest`) as `PerMonitorV2`.
  - Initialized in `Program.cs` before any UI creation via `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` and `User32.SetProcessDpiAwarenessContext`.

---

### 3.4. Background Clicking Engine (`BackgroundAutomator.Core.Clicking`)

- **[IBackgroundAutomator](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Clicking/IBackgroundAutomator.cs)**: Contract defining `Click(TargetPoint)` and `DoubleClick(TargetPoint)`.
- **[BackgroundClickerEngine](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Clicking/BackgroundClickerEngine.cs)**:
  - Validates `User32.IsWindow(target.Hwnd)`.
  - Encodes `(ClientX, ClientY)` into `lParam` using `MouseMessageHelper.MakeMouseLParam`.
  - Dispatches non-blocking asynchronous messages using `User32.PostMessage`:
    - **Single Click Sequence**:
      1. `WM_MOUSEMOVE` (`wParam = 0`, `lParam`)
      2. Pacing delay (if configured via `MessagePacingMilliseconds`)
      3. `WM_LBUTTONDOWN` (`wParam = MK_LBUTTON`, `lParam`)
      4. Pacing delay
      5. `WM_LBUTTONUP` (`wParam = 0`, `lParam`)
    - **Double Click Sequence**:
      1. `WM_MOUSEMOVE`
      2. `WM_LBUTTONDOWN` (`MK_LBUTTON`)
      3. `WM_LBUTTONUP` (0)
      4. `WM_LBUTTONDBLCLK` (`MK_LBUTTON`)
      5. `WM_LBUTTONUP` (0)
  - **Why `PostMessage` instead of `SendMessage`?**: `SendMessage` blocks the calling thread synchronously until the target window's message pump processes the message. If the target window is hung, busy, or displaying a modal dialog, `SendMessage` would freeze the automation engine. `PostMessage` posts to the target queue asynchronously and returns immediately.

---

### 3.5. Simple Click Runner Subsystem (`BackgroundAutomator.Core.Runner`)

- **[ClickRunner](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Runner/ClickRunner.cs)**:
  - Manages sequential execution of an ordered list of `ClickPoint`s.
  - Thread-safe lifecycle state machine:
    $$\text{Idle} \xrightarrow{\text{Start()}} \text{Running} \xrightarrow{\text{Stop()}} \text{Stopping} \rightarrow \text{Idle}$$
  - Execution runs on a background worker task (`Task.Run`).
  - Controlled by a `CancellationTokenSource`.
  - Supports two repeat modes (`RepeatMode`):
    - `UntilStopped`: Repeats the sequence infinitely until the user presses Stop or F6/F7.
    - `Count`: Repeats the sequence for a fixed number of cycles.
  - Inter-cycle and inter-point delays implemented with non-blocking `Task.Delay(ms, token)`.
  - Detects target closure during execution: if `User32.IsWindow(target.Hwnd)` becomes false, fires `ErrorOccurred` and terminates cleanly.

---

### 3.6. Macro Engine Subsystem (`BackgroundAutomator.Core.Macro`)

A composable, sequential pipeline for multi-step automation tasks:

```
┌────────────────────────────────────────────────────────┐
│                      MacroRunner                       │
│    (Sequential Execution, Linked CancellationToken,    │
│     Lifecycle State, Action Events & Timing)           │
└──────────────────────────┬─────────────────────────────┘
                           │ executes actions sequentially
   ┌───────────────────────┼─────────────────────────┐
   │                       │                         │
┌──▼──────────┐     ┌──────▼──────┐          ┌───────▼────────┐
│ ClickAction │     │ DelayAction │          │ WaitColorAction│
│ DoubleClick │     │ (Task.Delay)│          │ (GDI Capture + │
│   Action    │     │             │          │  Color Match)  │
└─────────────┘     └─────────────┘          └────────────────┘
```

#### Core Classes:
- **[IMacroAction](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/IMacroAction.cs)**:
  - Base interface requiring `ExecuteAsync(MacroExecutionContext context, CancellationToken ct)`.
- **[MacroExecutionContext](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/MacroExecutionContext.cs)**:
  - Dependency bag injected into each action: `IBackgroundAutomator Clicker`, `IWindowCaptureService CaptureService`, and `IntPtr TargetHwnd`.
- **[MacroActionResult](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/MacroActionResult.cs)**:
  - Status enum `MacroActionStatus`: `Success`, `Failed`, `TimedOut`, `Cancelled`, `TargetUnavailable`, `CaptureFailed`.
- **Macro Actions**:
  - **[ClickAction](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/ClickAction.cs)**: Executes single click at `(X, Y)`.
  - **[DoubleClickAction](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/DoubleClickAction.cs)**: Executes double click at `(X, Y)`.
  - **[DelayAction](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/DelayAction.cs)**: Non-blocking asynchronous delay.
  - **[WaitColorAction](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/WaitColorAction.cs)**:
    - Repeatedly captures client area via `CaptureService.CaptureClientAreaAsync`.
    - Samples pixel at client coordinates `(X, Y)`.
    - Compares with `ExpectedColor` using per-channel RGB `Tolerance`:
      $$|R_1 - R_2| \le T \land |G_1 - G_2| \le T \land |B_1 - B_2| \le T$$
    - Polls at configurable interval (default 100ms) until match or `TimeoutMilliseconds` expires.
    - If target window closes, returns `TargetUnavailable`.
    - If capture fails (e.g. target hung), returns `CaptureFailed`.
- **[MacroRunner](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Macro/MacroRunner.cs)**:
  - Orchestrates execution of `IReadOnlyList<IMacroAction>`.
  - Supports linked external cancellation tokens.
  - Short-circuits immediately if an action fails, cancels, or times out.
  - Fires fine-grained events: `StateChanged`, `ActionStarting`, `ActionCompleted`, and `ExecutionCompleted`.

---

### 3.7. Window Capture & Color Inspection Subsystem (`BackgroundAutomator.Core.Capture`)

Captures the visual appearance of background windows without bringing them to the foreground.

#### Win32 Capture Pipeline:
```
GetDC(hWnd)
   └──> CreateCompatibleDC(hdcWindow)
           └──> CreateCompatibleBitmap(hdcWindow, width, height)
                   └──> SelectObject(hdcMem, hBitmap)
                           └──> PrintWindow(hWnd, hdcMem, PW_CLIENTONLY)
                                   └──> Image.FromHbitmap(hBitmap) -> managed Bitmap
```

#### Strict GDI Cleanup Order:
To avoid leaking GDI and USER handles (which causes Windows desktop crashes when hitting the 10,000 handle ceiling), cleanup is strictly ordered in nested `finally` blocks:
1. `SelectObject(hdcMem, hOldBitmap)` (Restore original unmanaged object)
2. `Gdi32.DeleteObject(hBitmap)`
3. `Gdi32.DeleteDC(hdcMem)`
4. `User32.ReleaseDC(hWnd, hdcWindow)`

#### Hardening against Hung Targets & Thread Exhaustion:
1. **Pre-Check Target Responsiveness**: `User32.IsHungAppWindow(hWnd)` is checked before initiating capture. If the target application's message pump is deadlocked, capture is aborted immediately.
2. **Worker Thread Offloading**: Captures execute off the UI thread via `Task.Run(() => CaptureClientArea(hWnd))`.
3. **Single-Flight Concurrency Gate**: A `SemaphoreSlim(1, 1)` gate with `WaitAsync(0, ct)` ensures that if a previous capture is still processing, subsequent requests are dropped rather than accumulating hundreds of queued worker threads.

---

### 3.8. Profile Persistence & Storage Subsystem (`BackgroundAutomator.Core.Profiles`)

Handles saving and loading automation configurations to disk.

#### File Storage Location:
Profiles are stored as formatted JSON files in:
`%LOCALAPPDATA%\BackgroundAutomator\profiles\<profile_name>.json`

#### Durable Schema ([ProfileModel.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Profiles/ProfileModel.cs)):
```json
{
  "name": "MyAutomationProfile",
  "version": "1.0",
  "createdAt": "2026-09-23T11:00:00Z",
  "updatedAt": "2026-09-23T11:30:00Z",
  "mode": "Macro",
  "target": {
    "processName": "notepad",
    "windowTitle": "Untitled - Notepad",
    "windowClass": "Notepad",
    "titleMatchMode": "Contains",
    "childDescriptor": null
  },
  "settings": {
    "intervalMilliseconds": 1000,
    "repeatMode": "UntilStopped",
    "repeatCount": 1
  },
  "points": [],
  "macroActions": [
    {
      "actionType": "Click",
      "x": 120,
      "y": 45,
      "delayMilliseconds": 0,
      "expectedColorHex": null,
      "colorTolerance": 0,
      "timeoutMilliseconds": 0
    }
  ]
}
```

#### Atomic File Writes ([ProfileStorageService.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Profiles/ProfileStorageService.cs)):
To prevent profile corruption during sudden application termination or power loss:
1. Serializes JSON to a unique temporary file (`<guid>.tmp`) with `FileOptions.WriteThrough`.
2. Flushes file streams to disk (`Flush(flushToDisk: true)`).
3. Atomically moves/replaces the temporary file over the target file via `File.Move(tempPath, finalPath, overwrite: true)`.
4. If an error occurs, the temporary file is deleted.
5. Damaged or corrupted JSON files in the directory are safely skipped during profile listing (`ListProfiles`) without crashing the application.

---

### 3.9. Security & UIPI Privilege Diagnostics (`BackgroundAutomator.Core.Security`)

Windows enforces **User Interface Privilege Isolation (UIPI)**. Under UIPI, Windows blocks lower-integrity processes (e.g. non-elevated BackgroundAutomator) from sending window messages (`WM_LBUTTONDOWN`, `WM_LBUTTONUP`) to higher-integrity processes (e.g. an application running as Administrator). The messages are silently discarded by the Windows kernel without returning an error code.

- **[ProcessElevationService](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Security/ProcessElevationService.cs)**:
  - Queries `WindowsIdentity.GetCurrent()` to check if BackgroundAutomator is elevated.
  - Queries the target process token via `Kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `Advapi32.OpenProcessToken(TOKEN_QUERY)` + `Advapi32.GetTokenInformation(TokenElevation)`.
  - Produces an `ElevationCheckResult`:
    - `Compatible`: Both non-elevated or BackgroundAutomator is elevated.
    - `UipiMismatch`: Target is Admin but BackgroundAutomator is non-admin. Triggers UI warnings.
    - `Unknown`: Access denied or protected system process.
- **UI Action**:
  - The UI displays an amber warning badge when a UIPI mismatch occurs.
  - Provides a **Restart as Administrator** button that relaunches the executable with `ProcessStartInfo.Verb = "runas"`.

---

### 3.10. Application UI & Global Hotkeys (`BackgroundAutomator.App`)

- **WPF Fluent Presentation Layer (`MainWindow.xaml`, `Views/`, `ViewModels/`)**:
  - Organized with Fluent sidebar navigation (`Wpf.Ui.Controls.NavigationView`): **Target**, **Click Sequence**, **Macro**, **Profiles**, and **Diagnostics**.
  - Built with lightweight MVVM using **CommunityToolkit.Mvvm** (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
  - **Crosshair Drag-and-Drop Targeting**:
    - User clicks and holds the Crosshair button.
    - Captures mouse events and inspects the window underneath via `WindowTargetService.ResolveTargetFromScreenPoint` using `User32.GetCursorPos`.
    - Coordinates and candidate details are previewed in real-time.
    - When the mouse button is released, locks onto the resolved target, checks UIPI compatibility, and updates all views.
  - Persistent bottom status bar showing current target, runner status, and hotkey hints.
- **[GlobalHotkeyManager.cs](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.App/Hotkeys/GlobalHotkeyManager.cs)**:
  - Registers system-wide hotkeys via `User32.RegisterHotKey`:
    - **F6** (`HotkeyId = 1001`): Start / Stop active runner (Simple or Macro).
    - **F7** (`HotkeyId = 1002`): Emergency Stop (instantly halts any running loop).
  - Listens for `WM_HOTKEY` via `HwndSource` message hook in `MainWindow`.
  - Works even when BackgroundAutomator is minimized or another application has focus.
  - Gracefully handles conflicts if another application has already registered F6 or F7.

---

### 3.11. UI Automation Text Detection Subsystem (`BackgroundAutomator.Core.TextDetection`)

Responsible for reading textual content from background and foreground windows via the Windows UI Automation (UIA) COM accessibility tree:
- **[IUiAutomationTextDetectionService](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/TextDetection/IUiAutomationTextDetectionService.cs)**: Text extraction contract with cancellation support.
- **[TextDetectionScope](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/TextDetection/TextDetectionScope.cs)**:
  - `VisibleViewportOnly`: Restricts text extraction strictly to what is currently visible on the screen/control via `TextPattern.GetVisibleRanges()`. Fails closed if visible ranges cannot be obtained.
  - `DocumentBuffer`: Extracts the entire document/history range (`TextPattern.DocumentRange`).
- **Visible Viewport Authority Invariant**:
  - For unattended approval automation, `VisibleViewportOnly` is **mandatory**.
  - The detector **never** silently falls back from visible ranges to document buffer history, preventing historical scrolled-off prompts from accidentally triggering approvals.
- **Raw Text Preservation**:
  - `TextDetectionResult` returns both `ObservedText` (whitespace-collapsed for standard matching) and `RawText` (preserving newlines, column padding, and option markers like `> 1. Yes, run command`).

---

### 3.12. Command Approval Subsystem (`BackgroundAutomator.Core.Approval`)

Responsible for securely parsing terminal prompts and enforcing strict allowlist policies before approving commands:
- **[ICommandPromptParser](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Approval/ICommandPromptParser.cs)** & **[CommandPromptParser](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Approval/CommandPromptParser.cs)**:
  - Parses real-world raw terminal text buffers (such as Cascadia / Windows Terminal `TermControl`).
  - Supports modern AI Agent / CLI prompt wrappers:
    - Tool blocks: `● Bash(...)` or `● run_command(...)`
    - Prefixed lines: `Command: ...`
    - Markdown-style backtick fences: ```` ```sh ... ``` ````
    - Raw preceding lines above the prompt
  - Identifies active prompt (`Run this command?`, `Do you want to run this command?`, etc.).
  - Detects selected option marker (`>`, `❯`, `*`) and extracts selected option text (e.g. `> 1. Yes, run command`).
  - Returns `CommandPromptSnapshot` (runtime-only snapshot; never persists ephemeral HWNDs).
- **[CommandApprovalRule](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Approval/CommandApprovalRule.cs)**:
  - Multi-condition security rule specification: `ExpectedProcess`, `ExpectedWindowClass`, `ExpectedPrompt`, `ExpectedSelectedOption`, `AllowedCommand`, `CommandMatchMode`, and `Enabled`.
- **[CommandApprovalEvaluator](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Approval/CommandApprovalEvaluator.cs)**:
  - Evaluates snapshot against rule with fail-closed semantics.
  - Requires **all** conditions to match: target process, prompt text, selected option, command existence, and exact allowlist match (`CommandMatchMode.Exact`).
  - Returns explicit blocked reasons: `TargetMismatch`, `PromptNotVisible`, `OptionNotSelected`, `CommandNotFound`, `CommandNotAllowed`, `AmbiguousPrompt`, `DetectionFailed`.
- **[AutoConfirmExecutionMode](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Approval/AutoConfirmExecutionMode.cs)**:
  - `ObserveOnly`: Safe dry-run mode that parses, validates, and logs `WOULD APPROVE` or `BLOCKED` without sending any keystrokes. Default for new rules.
  - `Confirm`: Executes live double-validation and keystroke dispatch.

---

### 3.13. Keyboard & Foreground Pulse Subsystem (`BackgroundAutomator.Core.Keyboard` & `BackgroundAutomator.Win32`)

Dispatches keyboard inputs with explicit delivery strategies:
- **[KeyDeliveryMode](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Keyboard/KeyDeliveryMode.cs)**:
  - `BackgroundPostMessage`: Dispatches `WM_KEYDOWN` and `WM_KEYUP` to target HWND without focus changes (used by `PressKeyAction` for standard Win32/WinForms controls).
  - `ForegroundPulse`: Used for modern terminal surfaces (Windows Terminal / ConPTY) where background window messages are ignored by virtual terminal input processors.
- **[IForegroundKeyboard](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Keyboard/IForegroundKeyboard.cs)** & **[ForegroundKeyboardEngine](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Keyboard/ForegroundKeyboardEngine.cs)**:
  - Abstraction over Win32 `SendInput` hardware keystroke synthesizer.
  - Dispatches `SendEnterAsync` (Enter keydown + keyup).
- **[IWindowForegroundService](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Keyboard/IWindowForegroundService.cs)** & **[Win32WindowForegroundService](file:///d:/personal-project/background-clicker/src/BackgroundAutomator.Core/Keyboard/Win32WindowForegroundService.cs)**:
  - Encapsulates window focus management: `GetForegroundWindow()`, `SetForegroundWindow(hwnd)`, `IsIconic(hwnd)` (minimized check).
  - Mockable interface ensuring 100% testability of focus transitions and race conditions without stealing focus during tests.

---

### 3.14. Safe Auto-Confirm Macro Action (`BackgroundAutomator.Core.Macro.SafeAutoConfirmAction`)

Combines UIA detection, prompt parsing, rule evaluation, and a verified foreground pulse into a hardened, fail-closed macro action:
1. **Background Phase & Privilege Guard**:
   - Queries `ProcessElevationService.CheckElevationCompatibility(targetHwnd)`. If BackgroundAutomator is standard user and target is Administrator, fails closed immediately with `ApprovalBlockReason.UipiMismatch` (zero activation, zero keystrokes).
   - Queries UIA for visible viewport text (`VisibleViewportOnly`).
   - Parses snapshot using `CommandPromptParser` (handles multiline continuation via `|`, `` ` ``, `\`, indentation, and tool wrappers). If multiple independent candidate commands are detected, snapshot is marked `IsAmbiguous` and evaluator blocks with `ApprovalBlockReason.AmbiguousPrompt` (fail-closed).
   - Evaluates approval rule against target process, window class, prompt, selected option, and exact command.
   - If blocked, logs reason and aborts with `MacroActionStatus.ApprovalBlocked`.
   - If in `ObserveOnly` mode, logs `[ObserveOnly] WOULD APPROVE: <command>` and completes with `Success` without performing any activation or input injection.
2. **Foreground Transition Phase**:
   - Checks if target window is minimized (`IsIconic`). If minimized, aborts with `TargetUnavailable`.
   - Records currently focused window (`previousForeground = GetForegroundWindow()`).
   - **Already-Foreground Optimization**: If `previousForeground == targetRootHwnd`, skips activation and restoration entirely, avoiding artificial focus transitions.
   - Otherwise, requests foreground activation for the target root window (`SetForegroundWindow`) and polls until target is confirmed active foreground window (with timeout).
3. **Revalidation & Keystroke Phase**:
   - Re-queries root HWND, process identity, and window class to detect target swaps or closure.
   - Re-checks UIPI elevation compatibility.
   - Re-reads UIA visible viewport text to guard against UI changes during activation.
   - Re-parses prompt snapshot and re-evaluates rule.
   - **Focus Race Mitigation**: Win32 window focus is cooperative; the OS cannot provide a single atomic "test-focus-and-send-input" kernel primitive across processes. BackgroundAutomator minimizes this race window to near-zero by synchronously checking `GetForegroundWindow() == targetRootHwnd` directly before `SendInput` with **zero intervening awaits or thread context switches**. If focus was stolen by another window, it aborts immediately with `ApprovalFailed` without sending keystrokes.
   - Dispatches exactly one Enter key-down/key-up pair (`SendInput`). Never retries blindly on the same action invocation.
4. **Immediate Restoration & Background Acknowledgement (`FastPulse`)**:
   - **`FocusBehavior.FastPulse` (Default)**: Restores `previousForeground` immediately after `SendInput` (before prompt dismissal polling begins). UI Automation continues reading terminal text in the background to acknowledge prompt dismissal, minimizing visible screen pulse time down to milliseconds.
   - **`FocusBehavior.KeepTargetForeground`**: Keeps target terminal window focused after dispatching Enter.
   - Restoring focus failure does not mask a successful command approval (`EnterAccepted = true`).
   - Logs timing diagnostics: `ForegroundPulse: activation confirmed at +Xms, Enter sent at +Yms, previous foreground restored at +Zms, pulse duration = Zms`.
   - **Fingerprint-Based Acknowledgment**: Polls (up to 2,000ms) for prompt disappearance using the specific prompt snapshot fingerprint (expected command text and selected option). If the initial prompt disappears or is superseded by a subsequent prompt, Prompt A is safely recognized as acknowledged without approving Prompt B.
5. **Cleanup Phase (`finally`)**:
   - Ensures previous foreground window is restored if an unexpected exception occurs after activation but before normal restoration.

---

## 4. Key Execution Flows & Diagrams

### 4.1. Crosshair Window Targeting Flow
```mermaid
sequenceDiagram
    autonumber
    actor User
    participant App as MainWindow / TargetPage
    participant Win32 as User32 / Kernel32
    participant Svc as WindowTargetService

    User->>App: MouseDown on Crosshair Button
    App->>App: CaptureMouse() & Set Cross Cursor
    loop While Dragging Mouse
        User->>App: MouseMove
        App->>Win32: GetCursorPos(out POINT pt)
        App->>Svc: ResolveTargetFromScreenPoint(pt)
        Svc->>Win32: WindowFromPoint(pt)
        Svc->>Win32: GetAncestor(hTarget, GA_ROOT)
        Svc->>Win32: RealChildWindowFromPoint(hRoot, pt)
        Svc-->>App: WindowTargetCandidate
        App->>App: Update UI Preview (Title, Class, Coords)
    end
    User->>App: MouseUp
    App->>App: ReleaseMouseCapture()
    App->>App: Lock WindowTarget & Check UIPI Elevation
```

---

### 4.2. Simple Mode Background Click Loop
```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Hotkey as GlobalHotkeyManager (F6)
    participant Runner as ClickRunner
    participant Engine as BackgroundClickerEngine
    participant Win32 as User32.PostMessage
    participant Target as Target Window HWND

    User->>Hotkey: Press F6
    Hotkey->>Runner: Start(config)
    Runner->>Runner: State = Running, spawn Task.Run
    loop For each cycle
        loop For each ClickPoint in Points
            Runner->>Runner: Check CancellationToken
            Runner->>Win32: IsWindow(point.Hwnd)
            alt Window is valid
                Runner->>Engine: Click(TargetPoint)
                Engine->>Win32: PostMessage(WM_MOUSEMOVE, lParam)
                Engine->>Win32: PostMessage(WM_LBUTTONDOWN, MK_LBUTTON, lParam)
                Engine->>Win32: PostMessage(WM_LBUTTONUP, 0, lParam)
                Win32-->>Target: Dispatched to message queue
                Runner->>Runner: Task.Delay(IntervalMilliseconds)
            else Window closed / destroyed
                Runner->>Runner: Fire ErrorOccurred("Target unavailable")
                Runner->>Runner: Abort Loop -> State = Idle
            end
        end
    end
```

---

### 4.3. Macro Pipeline with WaitColor Execution
```mermaid
flowchart TD
    Start([Start MacroRunner.RunAsync]) --> ActionLoop[Get Next IMacroAction]
    ActionLoop --> ActionType{Action Type?}

    ActionType -->|ClickAction| ExecClick[PostMessage WM_MOUSEMOVE/DOWN/UP]
    ExecClick --> CheckResult{Success?}

    ActionType -->|DelayAction| ExecDelay[await Task.Delay ct]
    ExecDelay --> CheckResult

    ActionType -->|WaitColorAction| CheckHung[User32.IsHungAppWindow]
    CheckHung -->|Hung / Closed| FailColor[Return TargetUnavailable / CaptureFailed]
    CheckHung -->|Responsive| Capture[CaptureClientAreaAsync via PrintWindow]
    Capture --> MatchColor{Color matches Expected within Tolerance?}
    MatchColor -->|Yes| SuccessColor[Action Success]
    MatchColor -->|No| CheckTimeout{Timeout exceeded?}
    CheckTimeout -->|No| DelayPoll[Wait 100ms] --> CheckHung
    CheckTimeout -->|Yes| TimeoutColor[Return TimedOut]

    SuccessColor --> CheckResult
    FailColor --> CheckResult
    TimeoutColor --> CheckResult

    CheckResult -->|Success & More Actions| ActionLoop
    CheckResult -->|Failure / Cancel / Timeout| StopMacro[Abort Runner & Emit ExecutionCompleted]
    ActionLoop -->|All Actions Done| FinishMacro[Emit ExecutionCompleted with Succeeded = true]
```

---

### 4.4. Profile Save & Dynamic Re-Resolution
```mermaid
sequenceDiagram
    autonumber
    participant App as MainWindow / ProfilesPage
    participant Storage as ProfileStorageService
    participant Resolver as TargetResolver
    participant Win32 as EnumWindows / EnumChildWindows

    Note over App,Storage: Saving Profile
    App->>App: Extract TargetDescriptor (No live HWNDs!)
    App->>Storage: SaveProfile(model)
    Storage->>Storage: Serialize to <guid>.tmp
    Storage->>Storage: Flush to disk (WriteThrough)
    Storage->>Storage: File.Move(temp, profile.json, overwrite: true)

    Note over App,Storage: Loading & Re-resolving across App Restarts
    App->>Storage: LoadProfile(profileName)
    Storage-->>App: ProfileModel
    App->>Resolver: Resolve(model.Target)
    Resolver->>Win32: EnumWindows()
    alt Exactly 1 match found
        Resolver-->>App: TargetResolutionResult.Success(liveHwnd)
        App->>App: Bind fresh HWND to Runner/Macro
    else Multiple matches found
        Resolver-->>App: TargetResolutionResult.Ambiguous(candidates)
        App->>App: Warn User: Prompt to select specific window
    else No matches found
        Resolver-->>App: TargetResolutionResult.NotFound
        App->>App: Warn User: Target application is not running
    end
```

---

### 4.5. Safe Auto-Confirm Double-Validation Flow (Terminal Foreground Pulse)
```mermaid
sequenceDiagram
    autonumber
    participant Macro as SafeAutoConfirmAction
    participant UIA as UiAutomationTextDetectionService
    participant Parser as CommandPromptParser
    participant Eval as CommandApprovalEvaluator
    participant FgSvc as IWindowForegroundService
    participant Key as IForegroundKeyboard
    participant Target as Terminal Window

    Note over Macro,Target: 1. Background Discovery & Validation
    Macro->>UIA: DetectTextAsync(VisibleViewportOnly)
    UIA-->>Macro: TextDetectionResult (RawText)
    Macro->>Parser: Parse(RawText, target)
    Parser-->>Macro: CommandPromptSnapshot
    Macro->>Eval: Evaluate(snapshot, rule)
    alt Rule Blocked (Reason)
        Eval-->>Macro: Decision: Blocked (e.g. CommandNotAllowed)
        Macro-->>Macro: Abort -> MacroActionStatus.ApprovalBlocked
    else ObserveOnly Mode
        Eval-->>Macro: Decision: Allowed
        Macro-->>Macro: Log WOULD APPROVE -> MacroActionStatus.Success (No input)
    else Confirm Mode
        Note over Macro,Target: 2. Foreground Pulse & Window State Verification
        Macro->>FgSvc: IsIconic(rootHwnd)
        alt Window is Minimized
            Macro-->>Macro: Abort -> MacroActionStatus.TargetUnavailable
        end
        Macro->>FgSvc: GetForegroundWindow() (Save previous Hwnd)
        Macro->>FgSvc: SetForegroundWindow(rootHwnd)
        Macro->>FgSvc: Poll GetForegroundWindow() == rootHwnd

        Note over Macro,Target: 3. Re-Validation in Foreground State
        Macro->>UIA: DetectTextAsync(VisibleViewportOnly)
        UIA-->>Macro: Fresh RawText
        Macro->>Parser: Parse(Fresh RawText, target)
        Macro->>Eval: Evaluate(freshSnapshot, rule)
        alt Rule No Longer Matches
            Macro-->>Macro: Abort -> MacroActionStatus.ApprovalBlocked
        else Still Allowed
            Note over Macro,Target: 4. Final Focus Race Guard & Keystroke
            Macro->>FgSvc: GetForegroundWindow() == rootHwnd
            alt Focus Stolen by Another App
                Macro-->>Macro: Abort -> MacroActionStatus.ApprovalFailed
            else Still Foreground
                Macro->>Key: SendEnterAsync()
                Key-->>Target: SendInput(VK_RETURN Down + Up)
                Note over Macro,Target: 5. Acknowledgment Wait
                loop Until Prompt Disappears (max 2000ms)
                    Macro->>UIA: DetectTextAsync(VisibleViewportOnly)
                end
            end
        end
        Note over Macro,Target: 6. Always Restore Previous Foreground (finally)
        Macro->>FgSvc: SetForegroundWindow(previousForegroundHwnd)
    end
```

---

## 5. Repository File Map & Responsibilities

| Project / Directory | File | Single Responsibility |
| :--- | :--- | :--- |
| **`BackgroundAutomator.Win32`** | `User32.cs` | Win32 windowing, messaging, capture, foreground management, and hook P/Invoke signatures |
| | `Gdi32.cs` | GDI context and bitmap allocation/deletion P/Invoke signatures |
| | `Kernel32.cs` | Win32 process handles and thread query P/Invoke signatures |
| | `Advapi32.cs` | Windows security tokens and elevation P/Invoke signatures |
| | `SendInputHelper.cs` | Synthesizes hardware keyboard input events via native `SendInput` |
| | `KeyboardMessageHelper.cs` | Virtual key mapping and 32-bit `LPARAM` generation for `WM_KEYDOWN`/`WM_KEYUP` |
| | `MouseMessageHelper.cs` | Signed 16-bit `LPARAM` coordinate packing and unpacking |
| | `NativeConstants.cs` | Centralized constants (`WM_*`, `MK_*`, `PW_*`, `INPUT_*`, `KEYEVENTF_*`, etc.) |
| | `NativeTypes.cs` | `POINT`, `RECT`, and `INPUT` (with 64-bit union layout) interop structs |
| **`BackgroundAutomator.Core`** | `Targeting/WindowTargetService.cs` | Window enumeration and recursive screen-to-child HWND resolution |
| | `Targeting/TargetPoint.cs` | Immutable `(HWND, ClientX, ClientY)` coordinate model |
| | `Targeting/TargetDescriptor.cs` | Durable persistent target metadata without ephemeral HWNDs |
| | `Targeting/TargetResolver.cs` | Re-resolves target descriptors to live HWNDs with ambiguity detection |
| | `Coordinates/CoordinateService.cs` | Screen-to-client translations and round-trip verification |
| | `Clicking/BackgroundClickerEngine.cs` | `PostMessage` single and double click message generator |
| | `Runner/ClickRunner.cs` | Asynchronous simple click loop with CancellationToken support |
| | `Macro/MacroRunner.cs` | Sequential macro action orchestrator |
| | `Macro/WaitColorAction.cs` | Polled client-area color matching with tolerance and timeout |
| | `Macro/PressKeyAction.cs` | Dispatches discrete background keystrokes (`WM_KEYDOWN`/`WM_KEYUP`) |
| | `Macro/SafeAutoConfirmAction.cs` | Double-validated approval automation with foreground pulse |
| | `Approval/CommandPromptParser.cs` | Extracts active CLI prompts, option markers, and command lines |
| | `Approval/CommandApprovalEvaluator.cs` | Evaluates prompts against strict multi-condition allowlist rules |
| | `Approval/CommandApprovalRule.cs` | Model defining exact allowlist command rules |
| | `Keyboard/ForegroundKeyboardEngine.cs` | Synthesized hardware keystroke engine implementing `IForegroundKeyboard` |
| | `Keyboard/Win32WindowForegroundService.cs` | Window activation, state checks, and restoration implementing `IWindowForegroundService` |
| | `TextDetection/UiAutomationTextDetectionService.cs` | UIA text extraction with visible viewport authority |
| | `Capture/GdiWindowCaptureService.cs` | Safe client-area capture with single-flight gate and strict GDI lifecycle |
| | `Capture/WindowCapture.cs` | Managed bitmap wrapper with color tolerance math |
| | `Profiles/ProfileStorageService.cs` | Atomic `.tmp`-to-replace profile storage in `%LOCALAPPDATA%` |
| | `Security/ProcessElevationService.cs` | UIPI diagnostics and process token elevation queries |
| **`BackgroundAutomator.App`** | `App.xaml` / `App.xaml.cs` | Application entry, DPI initialization (`PerMonitorV2`), Fluent theme loading |
| | `app.manifest` | Declares DPI awareness (`PerMonitorV2`) to Windows OS |
| | `MainWindow.xaml` | Shell window hosting Fluent `NavigationView` and status strip |
| | `Views/` | Fluent pages: `TargetPage`, `SimplePage`, `MacroPage`, `ProfilesPage`, `DiagnosticsPage` |
| | `ViewModels/` | MVVM view models for each page and main application state |
| | `Hotkeys/GlobalHotkeyManager.cs` | Registers and handles system-wide `F6` and `F7` hotkeys |
| **`BackgroundAutomator.TestTarget`**| `Forms/TestTargetForm.cs` | Real test harness with click counters, message log, and color panels |
| **`BackgroundAutomator.Tests`** | `CommandPromptParserTests.cs` | Verifies parser regexes against real Cascadia terminal snapshots |
| | `CommandApprovalEvaluatorTests.cs` | Validates multi-condition allowlist evaluator and fail-closed reasons |
| | `SafeAutoConfirmActionTests.cs` | Mocks focus races, revalidation failures, timeouts, and duplicate protection |
| | `SafeAutoConfirmProfileTests.cs` | Verifies round-trip profile serialization and legacy schema compatibility |
| | `WindowCaptureIntegrationTests.cs` | Validates GDI capture against real `TestTarget` |
| | `MacroIntegrationTests.cs` | End-to-end macro execution and `WaitColor` verification |
| | `ResourceSoakTests.cs` | 5,000-iteration leak tests verifying zero GDI and USER handle leaks |
| | `TargetRestartIntegrationTests.cs`| Verifies profile reload and HWND re-resolution across process restarts |
| | `RealWorldTerminalDetectionDiagnosticTests.cs` | Opt-in live Windows Terminal diagnostics (`BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1`) |

---

## 6. Compatibility & Limitations Guide

When explaining or diagnosing targeting behavior, keep these platform realities in mind:

1. **Standard Win32 / WinForms**:
   - **Full Support**: Native buttons, text boxes, and panels have distinct HWNDs. `PostMessage` clicks, key presses, and `PrintWindow` captures work reliably in the background.
2. **Chromium / Electron / Modern Web Apps (Chrome, Edge, VS Code, Discord)**:
   - **Single Surface HWND**: These applications host their entire client area inside a single top-level HWND (e.g. `Chrome_RenderWidgetHostHWND`). Internal buttons do not have individual HWNDs.
   - Targeting must bind to the root/render HWND, and coordinates are relative to that entire surface.
3. **Windows Terminal & ConPTY Surfaces**:
   - Background `PostMessage(WM_KEYDOWN/WM_KEYUP)` is ignored by Windows Terminal / ConPTY virtual terminal input pipelines.
   - Text is read accurately in the background via Windows UI Automation `TextPattern.GetVisibleRanges()`.
   - Input delivery requires the hardened `ForegroundPulse` strategy (`SendInput` after verifying active foreground HWND), restoring previous focus immediately.
4. **Hardware-Polled Applications & DirectInput Games**:
   - Games utilizing DirectInput, Raw Input, or exclusive-mode DirectX/Vulkan surfaces bypass the Windows message queue entirely and read directly from USB hardware drivers. Window-level `PostMessage` calls will be ignored by these targets.
5. **UIPI (User Interface Privilege Isolation)**:
   - If the target application is running as Administrator, BackgroundAutomator must also be run as Administrator; otherwise, Windows kernel will drop the click messages.

---

## 7. Development & Verification Commands

### Build Solution
```powershell
dotnet build BackgroundAutomator.sln
```

### Run Full Test Suite (246 tests, Headless & CI Friendly)
```powershell
dotnet test BackgroundAutomator.sln
```

### Run Opt-In Interactive Real-World Terminal Diagnostics
```powershell
$env:BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS="1"
dotnet test BackgroundAutomator.sln --filter "FullyQualifiedName~RealWorld"
$env:BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=""
```

### Run Targeted Test Fixtures
```powershell
# Safe Auto-Confirm and Command Approval tests
dotnet test tests/BackgroundAutomator.Tests/BackgroundAutomator.Tests.csproj --filter "FullyQualifiedName~SafeAutoConfirm"

# GDI and USER handle leak soak tests (5,000 iterations)
dotnet test tests/BackgroundAutomator.Tests/BackgroundAutomator.Tests.csproj --filter "FullyQualifiedName~ResourceSoakTests"

# End-to-end macro and WaitColor tests
dotnet test tests/BackgroundAutomator.Tests/BackgroundAutomator.Tests.csproj --filter "FullyQualifiedName~MacroIntegrationTests"

# Re-resolution across target restarts
dotnet test tests/BackgroundAutomator.Tests/BackgroundAutomator.Tests.csproj --filter "FullyQualifiedName~TargetRestartIntegrationTests"
```

### Run Applications
```powershell
# Run BackgroundAutomator
dotnet run --project src/BackgroundAutomator.App/BackgroundAutomator.App.csproj

# Run TestTarget Bench
dotnet run --project tests/BackgroundAutomator.TestTarget/BackgroundAutomator.TestTarget.csproj
```

### Publish Portable Self-Contained Binary
```powershell
dotnet publish src/BackgroundAutomator.App/BackgroundAutomator.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o ./publish
```
Produces single-file executable at `./publish/BackgroundAutomator.App.exe` with zero external dependencies.
