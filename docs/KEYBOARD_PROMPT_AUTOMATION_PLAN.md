# BackgroundAutomator — Keyboard + Prompt Detection Plan

> **Baseline:** The WPF + Fluent Design migration has already been committed:
>
> `refactor: migrate desktop UI to WPF Fluent Design`
>
> This plan starts **after** that commit.

---

## 1. Final Goal

The practical target workflow is:

```text
Agent / CLI is running
        ↓
A confirmation prompt appears, e.g.

Run this command?
> 1. Yes, run command

        ↓
BackgroundAutomator detects the prompt
        ↓
If the configured safety rule matches
        ↓
Press Enter
```

The important distinction is:

```text
PressKey
```

alone is not enough.

The project also needs a **condition/detector** that can tell when the expected prompt is visible.

Therefore implement this in separate commits/phases.

---

# 2. Commit / Phase Order

## Already completed

```text
refactor: migrate desktop UI to WPF Fluent Design
```

Do not redo this work.

---

## Commit 1 — Rename project

Suggested commit:

```text
chore: rename BackgroundClicker to BackgroundAutomator
```

Rename:

```text
BackgroundClicker.sln
→ BackgroundAutomator.sln

BackgroundClicker.App
→ BackgroundAutomator.App

BackgroundClicker.Core
→ BackgroundAutomator.Core

BackgroundClicker.Win32
→ BackgroundAutomator.Win32

BackgroundClicker.Tests
→ BackgroundAutomator.Tests

BackgroundClicker.TestTarget
→ BackgroundAutomator.TestTarget
```

Also update:

- namespaces
- assembly/root namespace
- project references
- XAML namespaces
- executable/app title
- documentation
- profile storage path if desired

Run:

```powershell
dotnet build BackgroundAutomator.sln
dotnet test BackgroundAutomator.sln
```

Do not add new keyboard behavior in this commit.

---

## Commit 2 — Add discrete background key presses

Suggested commit:

```text
feat: add background key press macro action
```

Primary goal:

```text
PressKey(Enter)
```

Supported initial keys:

```text
Enter
Tab
Escape
Space
ArrowUp
ArrowDown
ArrowLeft
ArrowRight
```

Do not add text typing or key chords yet.

---

## Commit 3 — Add prompt detection condition

Suggested commit:

```text
feat: add wait-for-text prompt detection
```

Primary goal:

```text
Wait until configured prompt text is visible
```

Example:

```text
Run this command?
```

or more specifically:

```text
Run this command?
Yes, run command
```

After detection, a later macro action may execute `PressKey(Enter)`.

---

# 3. Important Safety Boundary

The confirmation UI shown by agent/CLI tools is a human approval boundary.

Do **not** implement:

```text
Whenever any "Run this command?" appears
→ blindly press Enter
```

as a global default.

Instead, auto-confirm must be an explicit user-created rule and should require all configured conditions to match.

Recommended rule shape:

```text
Application/process matches
AND
Window title matches
AND
Prompt text matches
AND
Optional command text / command prefix matches
THEN
Press Enter
```

Example:

```text
Process:
Antigravity / target IDE process

Prompt:
Run this command?

Selected option:
Yes, run command

Allowed command prefix:
dotnet test
```

or:

```text
Allowed command prefix:
Get-ChildItem -Path "D:\workspace\voicebot"
```

If the requested command cannot be identified or is not allowlisted, do not auto-confirm.

This avoids turning the utility into a blanket permission-bypass mechanism.

---

# 4. Keyboard Phase Architecture

Keep:

```text
BackgroundAutomator.App
        |
        v
BackgroundAutomator.Core
        |
        v
BackgroundAutomator.Win32
```

Suggested additions:

```text
BackgroundAutomator.Win32/
└── KeyboardMessageHelper.cs

BackgroundAutomator.Core/
├── Keyboard/
│   ├── BackgroundKey.cs
│   ├── IBackgroundKeyboard.cs
│   └── BackgroundKeyboardEngine.cs
└── Macro/
    └── PressKeyAction.cs
```

---

# 5. PressKey Implementation

Use target-bound keyboard messages first:

```text
WM_KEYDOWN
WM_KEYUP
```

Primary key:

```text
Enter -> VK_RETURN
```

Do not use:

```text
SendKeys
SetForegroundWindow
global SendInput
```

in the initial background implementation.

The goal is to preserve the current non-intrusive behavior.

---

# 6. KeyboardMessageHelper

Build correct `lParam` values for:

```text
WM_KEYDOWN
WM_KEYUP
```

Include:

```text
repeat count
scan code
extended-key bit
previous-key-state bit
transition-state bit
```

Use `MapVirtualKey` when needed.

Any new P/Invoke declaration belongs only in:

```text
BackgroundAutomator.Win32
```

---

# 7. Macro UI

Extend Add Action:

```text
Mouse
  Click
  Double Click

Keyboard
  Press Key

Timing
  Delay

Condition
  Wait for Color
```

Press Key configuration:

```text
Key
[ Enter ▼ ]

[ Add Action ]
```

Configured action:

```text
3. Press Key
   Enter
```

---

# 8. Profile Persistence

Persist semantic names:

```json
{
  "actionType": "PressKey",
  "key": "Enter"
}
```

Do not save raw VK integers in user-facing profile schema unless required internally.

Existing profiles must remain loadable.

---

# 9. TestTarget Keyboard Verification

Use the existing keyboard test area / raw Windows message logger.

Minimum integration test:

```text
Resolve keyboard target
        ↓
Press Enter
        ↓
Verify:
WM_KEYDOWN VK_RETURN
WM_KEYUP   VK_RETURN
```

Also smoke-test:

```text
Tab
Escape
Space
ArrowDown
```

---

# 10. Real CLI Compatibility Warning

The actual final target is an IDE/CLI permission prompt.

Modern terminal surfaces may be implemented using:

```text
Chromium / Electron
Windows Terminal
ConPTY
custom renderer
```

Such applications may not process background `WM_KEYDOWN` / `WM_KEYUP` the same way as WinForms controls.

Therefore:

```text
TestTarget success
≠
guaranteed CLI success
```

After the basic PressKey implementation, test against the actual Antigravity/CLI application.

If background key messages are not accepted, do **not** immediately rewrite the engine.

Create a separate compatibility phase.

---

# 11. Prompt Detection Phase

The final workflow needs a new macro condition such as:

```text
WaitForTextAction
```

Conceptually:

```text
WaitForText("Run this command?")
        ↓
PressKey(Enter)
```

But the detector backend must be chosen based on what the target application exposes.

Evaluate detection strategies in this order.

---

## Strategy A — Windows UI Automation / Accessibility

Try first if the target application exposes the prompt text through the Windows accessibility tree.

Advantages:

- no OCR errors
- text matching is cheap
- can work even when UI layout changes
- can identify controls semantically

Disadvantages:

- Chromium/terminal surfaces may expose incomplete or inconvenient trees
- terminal text may not be available as normal controls

Do a feasibility spike before adopting this backend.

Do not build a large UIA framework before confirming the target exposes useful text.

---

## Strategy B — OCR over a configured screen/capture region

If UI Automation cannot see the prompt, use OCR on a small configured region.

Concept:

```text
Capture target region
        ↓
OCR
        ↓
Normalize text
        ↓
Contains "Run this command?"
        ↓
Condition succeeds
```

Important:

- OCR only the configured prompt area, not the entire desktop.
- Poll at a reasonable interval such as 300–1000 ms.
- Use timeout.
- Normalize whitespace before matching.
- Avoid exact pixel-position assumptions where possible.

Potential action:

```text
WaitForTextAction
- Region
- ExpectedText
- MatchMode
- PollInterval
- Timeout
```

Initial match modes:

```text
Contains
Exact
```

Do not add regex/fuzzy NLP unless a real need appears.

---

## Strategy C — Image/template matching

Use only if OCR proves unreliable and the prompt has a stable visual pattern.

Possible target:

```text
Run this command?
> 1. Yes, run command
```

Template matching is more layout/theme/DPI-sensitive than text matching.

Treat it as fallback, not first choice.

---

# 12. Important Capture Constraint

The existing project captures target HWNDs using Win32/GDI/PrintWindow.

For Chromium/Electron/modern terminal surfaces, background capture can be incomplete or fail.

Therefore feasibility must be tested against the actual application.

The detection phase should begin with a small spike:

```text
Can we capture the prompt text from the actual target while it is visible?
```

If yes:

```text
use existing capture pipeline + OCR
```

If no:

evaluate:

```text
Windows Graphics Capture
or
foreground screen-region capture
```

as a separate technical decision.

Do not replace the existing capture engine until necessary.

---

# 13. Recommended Final Macro Model

Once both phases work:

```text
1. Wait For Text
   Process: target IDE
   Text: "Run this command?"
   Timeout: 60 sec

2. Wait For Text
   Text: "Yes, run command"

3. Press Key
   Enter
```

For safer command approval, add a rule condition before step 3:

```text
Command matches allowlist
```

Example:

```text
Allowed:
dotnet test *
dotnet build *
Get-ChildItem -Path "D:\workspace\..."
```

Do not make `Enter` fire on every generic confirmation dialog.

---

# 14. Optional Future Rule Type

A future abstraction may be:

```text
AutoConfirmRule
```

Example:

```text
Rule Name:
Approve safe local read commands

Target:
Antigravity

Prompt Contains:
Run this command?

Command Starts With:
Get-ChildItem

Action:
Press Enter
```

This abstraction is NOT required for the first PressKey commit.

It belongs after prompt detection is proven.

---

# 15. Out of Scope for PressKey Commit

Do not implement:

```text
OCR
WaitForText
image matching
template matching
UI Automation
text typing
Ctrl/Alt/Shift chords
clipboard
console APIs
ConPTY injection
focus stealing
SendInput
if/else
variables
node workflow editor
```

The key-press commit must remain small.

---

# 16. Out of Scope for Initial Prompt Detection Commit

Do not implement all detector types at once.

Pick the first backend that works reliably against the real target.

Do not build:

```text
UIA + OCR + template matching
```

simultaneously unless evidence shows all are needed.

YAGNI.

---

# 17. Manual Test — PressKey

## Test 1: TestTarget

```text
Target keyboard test area
        ↓
Macro: PressKey Enter
        ↓
Run
```

Expected:

```text
WM_KEYDOWN Enter
WM_KEYUP Enter
```

---

## Test 2: actual CLI

Open a harmless prompt where pressing Enter has a reversible/non-destructive effect.

Verify:

```text
PressKey Enter
```

works against the actual CLI.

If not, record:

```text
target process
target HWND/class
foreground/background state
whether the raw key message is received
```

Do not hack around it in the same commit.

---

# 18. Manual Test — Prompt Detection

Use a harmless command prompt.

Expected flow:

```text
No prompt
→ action remains waiting

Prompt appears
→ detector matches

Configured allowlist matches
→ Press Enter executes

Prompt disappears
→ macro completes
```

Negative cases:

```text
different prompt
→ must not press Enter

same prompt but command not allowlisted
→ must not press Enter

wrong application
→ must not press Enter
```

---

# 19. Acceptance Criteria — Rename Commit

```text
[ ] Solution renamed
[ ] Projects renamed
[ ] Namespaces renamed
[ ] Documentation renamed
[ ] Build passes
[ ] Existing tests pass
[ ] WPF app still launches
```

---

# 20. Acceptance Criteria — PressKey Commit

```text
[x] PressKeyAction exists
[x] Enter works in TestTarget
[x] WM_KEYDOWN is correct
[x] WM_KEYUP is correct
[x] Tab/Escape/Space/arrow keys supported
[x] Macro UI supports Press Key
[x] Profiles persist Press Key
[x] Existing click/macro behavior unchanged
[x] Build passes
[x] Full tests pass
```

---

# 21. Acceptance Criteria — Prompt Detection Commit

```text
[x] Feasibility tested against actual IDE/CLI (UI Automation TextPattern on TermControl)
[x] One reliable detection backend selected (Windows UI Automation COM accessibility)
[x] WaitForText-style condition implemented (WaitForTextAction)
[x] Configurable expected text
[x] Timeout implemented
[x] Poll interval implemented
[x] Negative/non-match case does nothing
[x] Auto-confirm requires explicit configured rule
[x] Optional command allowlist supported before unattended approval
[x] PressKey Enter runs only after successful match
[x] Diagnostic log explains match / timeout / blocked approval
[x] Full tests pass
```

---

# 22. Acceptance Criteria — Safe Auto-Confirm + Terminal Foreground Pulse (Phase 4)

```text
[x] VisibleViewportOnly enforced as authoritative (never silently falls back to DocumentRange)
[x] CommandPromptParser reliably extracts command text from tool calls (● Bash, ● run_command, Command:, raw lines)
[x] Selected option marker detection (> 1. Yes, run command)
[x] CommandApprovalEvaluator enforces multi-condition allowlist (Process, WindowClass, Prompt, Option, Exact command)
[x] Fail-closed design with explicit block reasons (TargetMismatch, PromptNotVisible, OptionNotSelected, CommandNotFound, CommandNotAllowed)
[x] Observe-Only dry-run mode (logs WOULD APPROVE without sending keystrokes)
[x] Double-validation runtime loop: Background check -> Activate root HWND -> Re-validate in foreground state
[x] Immediate focus race guard (checks GetForegroundWindow() right before SendInput)
[x] Minimized target detection (IsIconic -> TargetUnavailable)
[x] Prompt disappearance acknowledgment wait (prevents duplicate Enter dispatch)
[x] Always restores previous foreground window in finally block
[x] Interactive diagnostic tests opt-in via BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1
[x] Solution build succeeds with 0 warnings, 0 errors
[x] Full test suite (246 tests) passes in headless environment
```

---

# 23. Documentation

Update incrementally after each phase:

```text
README.md
docs/ARCHITECTURE.md
docs/PHASES.md
docs/USER_GUIDE.md
```

Do not document future functionality as if it already exists.

---

# 23. Agent Execution Rule

Before each phase:

1. Read current documentation.
2. Inspect actual code.
3. Run existing tests.
4. Check `git status`.
5. Keep the working tree focused on one phase.

After each phase:

1. Build.
2. Run full tests.
3. Run feature-specific tests.
4. Manual smoke test.
5. Update docs.
6. Commit separately.

---

## Final Direction

The project is no longer just:

```text
Background clicker
```

The intended direction is:

```text
BackgroundAutomator

Observe target state
        ↓
Wait for a condition
        ↓
Perform a small background action
```

For the immediate real-world use case:

```text
Detect an expected CLI confirmation prompt
        ↓
Verify it matches an explicitly configured safe rule
        ↓
Press Enter
```

Build this incrementally rather than trying to solve keyboard input, OCR, terminal compatibility, and workflow branching in one commit.
