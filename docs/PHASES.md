# BackgroundAutomator Development Phases

## Project goal

A lightweight .NET 8 WinForms utility capable of targeting another HWND
and running compatible mouse/macro automation without moving the user's
physical mouse cursor.

Background messaging compatibility depends on the target application's
input model and is not guaranteed for every application.

---

## Phase 1 — Foundation + Target Window Inspector

Status: COMPLETE

Scope:

- .NET 8 WinForms solution
- centralized Win32 interop
- PerMonitorV2 DPI awareness
- top-level window enumeration
- crosshair target picker
- root/parent/child HWND resolution
- PID/process/class/title inspection
- screen/client coordinate conversion
- TargetPoint bound to HWND
- target validity handling
- diagnostic logging
- deterministic TestTarget
- Phase 1 automated/integration tests

Definition of Done:

- target dropdown works
- crosshair resolves correct child HWND
- screen/client coordinates are correct
- move/resize remains correct
- target closure does not crash
- TestTarget usable for deterministic regression testing

---

## Phase 2 — Background Click MVP

Status: COMPLETE

Scope:

- IBackgroundAutomator
- PostMessage mouse engine
- single click
- double click
- LPARAM helper
- Simple Mode
- multiple ordered click points
- asynchronous ClickRunner
- CancellationToken stop
- repeat forever / repeat count
- F6 Start/Stop
- F7 Emergency Stop
- invalid target detection while running
- deterministic TestTarget message verification

Definition of Done:

- target receives background clicks
- physical cursor does not move
- UI remains responsive
- N-point execution preserves order
- cancellation is clean
- hotkeys work while app is minimized
- closing target stops runner safely

---

## Phase 3 — Macro Engine + Window Capture

Status: COMPLETE

Scope:

- IMacroAction interface and MacroActionResult result model
- MacroExecutionContext runtime dependencies (Clicker, CaptureService, Target HWND)
- MacroRunner sequential execution orchestrator with thread-safe state lifecycle (Idle -> Running -> Stopping -> Idle)
- ClickAction (background single click via Win32 PostMessage)
- DoubleClickAction (background double click via Win32 PostMessage)
- DelayAction (asynchronous non-blocking Task.Delay with CancellationToken)
- WaitColorAction (deterministic polling with per-channel RGB tolerance and timeout)
- GdiWindowCaptureService (PrintWindow PW_CLIENTONLY client-area capture with strict GDI lifecycle management)
- WindowCapture (strict bounds checked pixel access and color tolerance matching in client space)
- Action sequence editor and runner UI in Macro Mode tab
- TestTarget extensions with dedicated ColorPanel, RGB buttons, and delayed color transitions
- 89 automated unit and deterministic integration tests verifying capture, move invariance, and end-to-end macro pipelines

Important rule:

Window capture coordinates and click coordinates strictly share the exact
same client coordinate system, where (0, 0) represents the top-left corner of the client area.

Definition of Done:

- deterministic action pipeline works (verified by integration tests)
- WaitColor succeeds when expected (tested with immediate and delayed color changes)
- WaitColor times out safely (tested with non-matching color and bounded timeouts)
- cancellation works while waiting (verified via CancellationToken and Stop command)
- multi-runner state remains independent (verified via runner lifecycle unit tests)
- physical mouse cursor remains untouched throughout all macro actions and captures

---

## Phase 4 — Hardening + Persistence + Release

Status: COMPLETE

Scope:

- Window capture hardening:
  - Off-UI-thread asynchronous execution (`CaptureClientAreaAsync`)
  - Target hung-state detection via `User32.IsHungAppWindow` pre-check
  - Single-flight capture gate (`SemaphoreSlim(1, 1)`) preventing accumulation of worker threads on sluggish targets
  - Explicit `CaptureFailed` and `TargetUnavailable` status propagation in `WaitColorAction` and runner short-circuiting
- Durable Target Descriptors & Re-resolution (`BackgroundAutomator.Core.Targeting`):
  - `TargetDescriptor` capturing `ProcessName`, `WindowTitle`, `WindowClass`, and `TitleMatchMode`
  - `ChildTargetDescriptor` capturing child control class, text, and index
  - Invariant enforced: No ephemeral live HWND handles are ever stored in persistent profiles
  - `TargetResolver` supporting Exact, Contains, StartsWith, and Any title matching
  - Ambiguous target detection when multiple windows match, preventing unintended click dispatch
  - Child control re-resolution via Win32 `EnumChildWindows`
- Profile persistence & storage (`BackgroundAutomator.Core.Profiles`):
  - `ProfileModel` with schema versioning (`1.0`), timestamps, mode (`Simple` or `Macro`), settings, and points/actions
  - `ProfileStorageService` with atomic file writes (write-to-temporary `.tmp` + flush + `File.Move` overwrite)
  - Corruption-resilient profile listing (`ListProfiles`) that skips damaged JSON without crashing
- Privilege / UIPI diagnostics (`BackgroundAutomator.Core.Security`):
  - `ProcessElevationService` querying process tokens via `OpenProcessToken` and `GetTokenInformation(TokenElevation)`
  - Detection of User Interface Privilege Isolation (UIPI) mismatches (non-elevated BackgroundAutomator targeting elevated process)
  - UI warning badges and "Restart as Administrator" UX action
- Multi-monitor & negative coordinate support:
  - Signed Win32 coordinate translations verified for secondary displays located above or to the left of primary display
- Resource & GDI soak testing (`ResourceSoakTests`):
  - 5,000-iteration capture soak test verifying zero GDI (`GR_GDIOBJECTS`) and USER (`GR_USEROBJECTS`) handle leaks and bounded memory
  - 5,000-iteration click soak test verifying zero handle leaks and bounded memory
- Target restart integration testing (`TargetRestartIntegrationTests`):
  - Launch TestTarget 1 -> save profile -> terminate -> launch TestTarget 2 (new HWND) -> load profile -> re-resolve -> click verified in TestTarget 2 log
- Portable self-contained release publishing:
  - Target runtime: `win-x64`
  - `PublishSingleFile=true`, `PublishTrimmed=false`, `--self-contained true`
  - Smoke-tested executable runs successfully out of `./publish/BackgroundAutomator.App.exe`
- Automated test coverage:
  - 140 unit and integration tests passing across all test fixtures

Definition of Done:

- profile reload resolves fresh HWND (verified by automated tests and target restart integration test)
- privilege mismatch is understandable (UIPI warning and Restart as Admin implemented)
- target lifecycle is robust (hung window detection, capture throttling, and target closure handled)
- idle CPU is near zero (timer-based non-blocking execution)
- long-running loop has no abnormal memory growth (verified by 5,000-iteration GDI resource soak test)
- portable BackgroundAutomator.exe is produced and verified (153MB self-contained single-file binary)

---

## Native Win32 Non-Cancellability Architectural Note

When a native Win32 API call such as `PrintWindow` or synchronous window message dispatch enters unmanaged Windows kernel mode, a managed .NET `CancellationToken` cannot forcibly terminate or abort the in-flight Win32 call without risking process corruption.

To guarantee system stability and responsiveness, BackgroundAutomator implements a defense-in-depth architecture:
1. **Pre-Check Responsiveness**: `User32.IsHungAppWindow(hWnd)` pre-checks target responsiveness before calling `PrintWindow`, immediately skipping hung applications.
2. **Worker Thread Isolation**: All captures execute asynchronously on worker threads via `CaptureClientAreaAsync`, ensuring the UI message pump and main thread are never blocked.
3. **Single-Flight Concurrency Throttling**: A single-flight gate (`SemaphoreSlim(1, 1)`) with `WaitAsync(0, ct)` prevents backlog accumulation. If a previous capture is still executing, subsequent capture requests are dropped or safely rejected without spawning an unbounded number of worker threads.
4. **Failure State Propagation**: Failures produce explicit `CaptureFailed` or `TargetUnavailable` results that halt the macro runner safely.

---

## Phase 5 — Background Key Press Macro Action

Status: COMPLETE (Commit `66605c2`)

Scope:
- `BackgroundKey` enum (`Enter`, `Tab`, `Escape`, `Space`, `ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`)
- `KeyboardMessageHelper` virtual key mapping and 32-bit `lParam` bit packing for `WM_KEYDOWN` and `WM_KEYUP`
- `PressKeyAction` macro action executing background keystrokes via unmanaged `PostMessage`
- `IBackgroundKeyboard` and `BackgroundKeyboardEngine` abstractions
- Macro UI and Profile JSON serialization (`actionType: "PressKey"`)
- Verified against `TestTarget` raw Windows message logger

---

## Phase 6 — Prompt Text Detection via UI Automation

Status: COMPLETE (Commit `89b98e9`)

Scope:
- Evaluation of detection backends: Windows UI Automation (UIA) COM accessibility chosen over OCR for high reliability and zero OCR hallucinations on terminal controls (`TermControl`).
- `IUiAutomationTextDetectionService` and `UiAutomationTextDetectionService` using `TextPattern`.
- `WaitForTextAction` polling visible window text with configurable match mode (`Contains`, `Exact`), polling intervals, and timeouts.
- Macro page UI integration and profile persistence.

---

## Phase 7 (Prompt Phase 4) — Safe Auto-Confirm + Terminal Foreground Input

Status: COMPLETE

Scope:
- **Phase 3 Hardening & Safety Invariants**:
  - `TextDetectionScope.VisibleViewportOnly`: Enforced visible viewport authority via `TextPattern.GetVisibleRanges()`. Never silently falls back to document history for approval automation.
  - Raw visible text preservation (`RawText` alongside normalized `ObservedText`).
  - Gated all real-world desktop/terminal tests behind `BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1`. Default `dotnet test` suite runs 100% headless with zero live desktop dependencies.
- **Command Prompt Parsing (`ICommandPromptParser`)**:
  - Real-world Cascadia terminal buffer unwrapping for CLI/agent tools (`● Bash(...)`, `run_command(...)`, `Command: ...`, raw lines).
  - Selected option detection (`> 1. Yes, run command`).
  - `CommandPromptSnapshot` runtime model.
- **Strict Allowlist Rule Evaluator (`CommandApprovalEvaluator`)**:
  - Multi-condition verification: Process + WindowClass + Prompt + SelectedOption + Exact Allowlisted Command (`CommandMatchMode.Exact`).
  - Fail-closed design: Returns explicit block reasons (`CommandNotAllowed`, `OptionNotSelected`, `TargetMismatch`, etc.).
  - `AutoConfirmExecutionMode`: `ObserveOnly` (dry-run, logs `WOULD APPROVE`) and `Confirm` (live execution).
- **Foreground Pulse Delivery (`IForegroundKeyboard`, `IWindowForegroundService`)**:
  - Encapsulated Win32 `SendInput` (`SendInputHelper`) and `SetForegroundWindow`/`GetForegroundWindow`/`IsIconic`.
  - Target minimized check (`IsIconic`) failing closed.
  - Double-validation loop: initial background evaluation -> activate target root window -> verify foreground -> re-read UIA visible viewport -> re-parse and re-evaluate -> verify foreground immediately before `SendInput` (focus race guard) -> dispatch Enter -> poll prompt disappearance (duplicate protection) -> restore previous foreground window in `finally` block.
- **Presentation & Persistence**:
  - Macro page UI tab for Safe Auto-Confirm with rule editor and mode toggle (`ObserveOnly` vs `Confirm`).
  - Backward-compatible profile JSON persistence with atomic storage.
- **Test Coverage**:
  - 246 total automated tests (0 failures, 0 skipped). Includes 34 new unit and mock integration tests covering all parsing variants, rule evaluator block reasons, focus races, revalidation failures, timeouts, duplicate prevention, and profile serialization.

---

## Backlog / Optional Enhancements

The following features were identified during development but intentionally omitted from the core MVP to maintain stability and simplicity:
- **Randomized Coordinate Jitter**: Adding configurable `+/- N` pixel jitter to click coordinates to simulate human variation.
- **Concurrent Multi-Runners**: Simultaneously running multiple independent macro sequences against different target windows concurrently.
- **Visual Region Selector**: Drag-and-drop bounding box UI for selecting capture sub-regions interactively.

---

## Development workflow

### 1. Build Solution
```powershell
dotnet build BackgroundAutomator.sln
```

### 2. Run Test Suite
```powershell
dotnet test BackgroundAutomator.sln
```
Or run specific test fixtures:
```powershell
dotnet test tests/BackgroundAutomator.Tests/BackgroundAutomator.Tests.csproj --filter "FullyQualifiedName~ResourceSoakTests"
dotnet test tests/BackgroundAutomator.Tests/BackgroundAutomator.Tests.csproj --filter "FullyQualifiedName~MacroIntegrationTests"
dotnet test tests/BackgroundAutomator.Tests/BackgroundAutomator.Tests.csproj --filter "FullyQualifiedName~TargetRestartIntegrationTests"
```

### 3. Run TestTarget (Deterministic Target Application)
```powershell
dotnet run --project tests/BackgroundAutomator.TestTarget/BackgroundAutomator.TestTarget.csproj
```

### 4. Run BackgroundAutomator App in Development Mode
```powershell
dotnet run --project src/BackgroundAutomator.App/BackgroundAutomator.App.csproj
```

### 5. Build Portable Single-File Release
```powershell
dotnet publish src/BackgroundAutomator.App/BackgroundAutomator.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o ./publish
```

---

## Global constraints

- Windows only
- C# / .NET 8
- WinForms
- x64
- no unnecessary UI framework
- no physical cursor movement for background mode
- no Thread.Abort
- no busy-spin scheduler
- no anti-cheat or anti-detection functionality
- TestTarget remains the canonical deterministic regression target
- target compatibility must never be assumed universally

