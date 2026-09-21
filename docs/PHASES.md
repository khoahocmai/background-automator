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

Status: PLANNED

Scope:

- UIPI / privilege mismatch diagnostics
- optional restart-as-administrator UX
- profile persistence
- target re-resolution
- process/title/class matching
- no persisted session HWND
- edge-case error handling
- mixed DPI verification
- multi-monitor testing
- capability diagnostics
- memory/CPU soak tests
- portable self-contained publish
- win-x64
- PublishSingleFile=true
- PublishTrimmed=false

Definition of Done:

- profile reload resolves fresh HWND
- privilege mismatch is understandable
- target lifecycle is robust
- idle CPU is near zero
- long-running loop has no abnormal memory growth
- portable BackgroundClicker.exe is produced

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
