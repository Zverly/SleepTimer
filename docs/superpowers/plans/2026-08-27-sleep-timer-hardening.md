# Sleep Timer Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows timer predictable and recoverable across real task-scheduler states, portable-path changes, reminders, and release packaging.

**Architecture:** Keep `TimerService` as the single stateful operation boundary and make the Windows adapter produce/consume one explicit Task Scheduler XML definition. The WPF shell periodically reconciles with that service, while reminders and preference behavior remain presentation-layer concerns.

**Tech Stack:** C#, .NET 8, WPF, Windows Task Scheduler via `schtasks.exe`, xUnit, PowerShell.

**Spec:** `docs/superpowers/specs/2026-08-26-sleep-shutdown-timer-design.md`

## Global Constraints

- Target Windows 10/11 x64 and keep source, caches, tests, and artifacts on `E:\codex_project`.
- Preserve the 30/60/90/120-minute preset-first flow and offline portable delivery.
- Never execute real shutdown or sleep in automated tests.
- Normal shutdown must remain separate from force shutdown; force shutdown requires confirmation.
- Only the fixed `SleepTimer.Current` task may be read or changed, and unknown same-name definitions must not be overwritten.
- Task changes must query, replace, and confirm the real Windows task state.

---

### Task 1: Harden Task Scheduler XML and Ownership

**Files:**
- Modify: `src/SleepTimer.Windows/WindowsScheduledTaskStore.cs`
- Modify: `src/SleepTimer.Windows/ProcessRunner.cs`
- Test: `tests/SleepTimer.Windows.Tests/WindowsScheduledTaskStoreTests.cs`
- Test: `tests/SleepTimer.Windows.Tests/ProcessRunnerTests.cs`

- [ ] Write tests for `/XML ONE`, quoted command normalization, future-date XML boundaries, wake-to-run policy, and rejection of foreign definitions before `/Create /F`.
- [ ] Replace `/SD` and `/ST` creation arguments with a single generated XML definition containing the exact local start boundary, one time trigger, application command/arguments, principal, and action-specific `WakeToRun`.
- [ ] Query and validate the existing fixed task before replacement; refuse replacement when URI, command, arguments, trigger shape, or principal is not application-owned.
- [ ] Add cancellation termination and a bounded timeout to `ProcessRunner`, preserving the caller cancellation contract.
- [ ] Run Windows adapter tests and inspect generated command/XML strings without creating a real power task.

### Task 2: Reconcile Real State and Time Semantics

**Files:**
- Modify: `src/SleepTimer.Core/TimerService.cs`
- Modify: `src/SleepTimer.Core/TimerCalculator.cs`
- Modify: `src/SleepTimer.App/MainWindow.xaml.cs`
- Test: `tests/SleepTimer.Core.Tests/TimerServiceTests.cs`
- Test: `tests/SleepTimer.Core.Tests/TimerCalculatorTests.cs`
- Test: `tests/SleepTimer.App.Tests/MainWindowTests.cs`

- [ ] Add explicit adjustment results containing requested and actual target times so a 2-minute safety floor cannot be reported as a full 30-minute reduction.
- [ ] Round scheduler targets to minute precision before create/adjust and always display the final date and time.
- [ ] Require confirmation when a specific time rolls to tomorrow; cancel leaves the existing task untouched.
- [ ] Reconcile the system task before start, adjust, cancel, return-to-settings, and at a bounded periodic interval; preserve the last known task on query failure.
- [ ] Make startup path mismatch recoverable with an actionable migration/repair state instead of terminating the WPF process.

### Task 3: Add Reminders, Preferences, and Tray Robustness

**Files:**
- Modify: `src/SleepTimer.App/MainWindow.xaml.cs`
- Modify: `src/SleepTimer.App/MainWindow.xaml`
- Modify: `src/SleepTimer.App/App.xaml.cs`
- Modify: `src/SleepTimer.App/AppPresentation.cs`
- Test: `tests/SleepTimer.App.Tests/MainWindowTests.cs`

- [ ] Add deduplicated 10-minute, 1-minute, and 30-second reminder state keyed by task identity and target time; recompute after adjustment and restore.
- [ ] Honor the remember-selection preference: disabled uses shutdown plus 60 minutes and does not overwrite remembered selection.
- [ ] Add editable persistent preset values while retaining 30/60/90/120 as defaults and showing a clear selected state.
- [ ] Ensure close-to-tray and explicit exit behavior are mutually consistent and that tray restore never calls `Show()` on a disposed window.
- [ ] Unify tray feedback with main-window feedback and display the new target time after successful adjustment.

### Task 4: Release Audit and Windows Verification Harness

**Files:**
- Modify: `scripts/publish.ps1`
- Create: `tests/SleepTimer.Windows.Tests/WindowsIntegrationChecklist.md`
- Modify: `README.md`

- [ ] Validate x64 PE architecture, self-contained output, recursive absence of PDB/test logs/bin/obj, and ZIP contents.
- [ ] Ensure runtime data is created only below the portable E-drive directory.
- [ ] Document the non-destructive Windows integration procedure for create/query/adjust/cancel, lock-screen, sleep, battery, permissions, path migration, locale, DST, and scaling.
- [ ] Run Release restore, tests, build, publish, and launch smoke tests; report real Windows power-action verification separately from automated evidence.
