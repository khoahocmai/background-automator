# BackgroundClicker Development Phases

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

- IBackgroundClicker
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
- Durable Target Descriptors & Re-resolution (`BackgroundClicker.Core.Targeting`):
  - `TargetDescriptor` capturing `ProcessName`, `WindowTitle`, `WindowClass`, and `TitleMatchMode`
  - `ChildTargetDescriptor` capturing child control class, text, and index
  - Invariant enforced: No ephemeral live HWND handles are ever stored in persistent profiles
  - `TargetResolver` supporting Exact, Contains, StartsWith, and Any title matching
  - Ambiguous target detection when multiple windows match, preventing unintended click dispatch
  - Child control re-resolution via Win32 `EnumChildWindows`
- Profile persistence & storage (`BackgroundClicker.Core.Profiles`):
  - `ProfileModel` with schema versioning (`1.0`), timestamps, mode (`Simple` or `Macro`), settings, and points/actions
  - `ProfileStorageService` with atomic file writes (write-to-temporary `.tmp` + flush + `File.Move` overwrite)
  - Corruption-resilient profile listing (`ListProfiles`) that skips damaged JSON without crashing
- Privilege / UIPI diagnostics (`BackgroundClicker.Core.Security`):
  - `ProcessElevationService` querying process tokens via `OpenProcessToken` and `GetTokenInformation(TokenElevation)`
  - Detection of User Interface Privilege Isolation (UIPI) mismatches (non-elevated BackgroundClicker targeting elevated process)
  - UI warning badges and "Restart as Administrator" UX action
- Multi-monitor & negative coordinate support:
  - Signed Win32 coordinate translations verified for secondary displays located above or to the left of primary display
- Resource & GDI soak testing (`ResourceSoakTests`):
  - 500-iteration capture soak test verifying zero GDI (`GR_GDIOBJECTS`) and USER (`GR_USEROBJECTS`) handle leaks
- Target restart integration testing (`TargetRestartIntegrationTests`):
  - Launch TestTarget 1 -> save profile -> terminate -> launch TestTarget 2 (new HWND) -> load profile -> re-resolve -> click verified in TestTarget 2 log
- Portable self-contained release publishing:
  - Target runtime: `win-x64`
  - `PublishSingleFile=true`, `PublishTrimmed=false`, `--self-contained true`
  - Smoke-tested executable runs successfully out of `./publish/BackgroundClicker.App.exe`
- Automated test coverage:
  - 128 unit and integration tests passing across all test fixtures

Definition of Done:

- profile reload resolves fresh HWND (verified by automated tests and target restart integration test)
- privilege mismatch is understandable (UIPI warning and Restart as Admin implemented)
- target lifecycle is robust (hung window detection, capture throttling, and target closure handled)
- idle CPU is near zero (timer-based non-blocking execution)
- long-running loop has no abnormal memory growth (verified by 500-iteration GDI resource soak test)
- portable BackgroundClicker.exe is produced and verified (153MB self-contained single-file binary)

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
