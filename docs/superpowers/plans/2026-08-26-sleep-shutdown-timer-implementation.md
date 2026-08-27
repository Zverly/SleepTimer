# 睡前定时关机软件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first Windows x64 WPF slice of the sleep shutdown timer with tested scheduling logic and a mouse-first home/countdown flow.

**Architecture:** Keep domain logic independent of Windows: immutable timer requests, next-day time calculation, and injected scheduler/power interfaces. Add a Windows adapter around Task Scheduler and power commands, then bind a small WPF shell to the same services. Persist only the app-owned task summary and preferences under the portable directory.

**Tech Stack:** C#, .NET 8, WPF, xUnit, Windows Task Scheduler COM (`Microsoft.Win32.TaskScheduler` or a thin process adapter).

---

### Task 1: Bootstrap solution and domain tests

**Files:**
- Create: `SleepTimer.sln`, `src/SleepTimer.Core/SleepTimer.Core.csproj`, `tests/SleepTimer.Core.Tests/SleepTimer.Core.Tests.csproj`
- Create: `src/SleepTimer.Core/TimerModels.cs`, `src/SleepTimer.Core/TimerCalculator.cs`
- Test: `tests/SleepTimer.Core.Tests/TimerCalculatorTests.cs`

- [ ] Write tests for the four default presets, duration-to-target conversion, and specified times that roll to tomorrow.
- [ ] Run `dotnet test tests/SleepTimer.Core.Tests` and confirm the tests fail because the core types do not exist.
- [ ] Implement `TimerAction`, `TimerRequest`, `Preset`, `DefaultPresets`, and `TimerCalculator` with injectable `DateTimeOffset now` inputs.
- [ ] Run the focused tests, then the whole test project, and commit the bootstrap.

### Task 2: Define scheduler and power boundaries

**Files:**
- Create: `src/SleepTimer.Core/TaskSchedulerAbstractions.cs`, `src/SleepTimer.Core/PowerAbstractions.cs`, `src/SleepTimer.Core/TimerService.cs`
- Test: `tests/SleepTimer.Core.Tests/TimerServiceTests.cs`

- [ ] Write tests proving start creates one app-owned task, extension replaces its target, cancel removes only that task, and scheduler failures leave state unchanged.
- [ ] Run the focused tests and verify the expected red failures.
- [ ] Implement `IScheduledTaskStore`, `IPowerExecutor`, `ScheduledTaskSummary`, and `TimerService` using fake-safe interfaces and no direct process calls.
- [ ] Run all core tests and commit the service layer.

### Task 3: Add portable state persistence

**Files:**
- Create: `src/SleepTimer.Core/StateStore.cs`
- Test: `tests/SleepTimer.Core.Tests/StateStoreTests.cs`

- [ ] Write tests for atomic preference writes and corrupt-file backup/default recovery.
- [ ] Verify the tests fail before implementation.
- [ ] Implement temp-file plus replace semantics, keeping `data/preferences.json`, `data/task.json`, and a `.bak` on recovery.
- [ ] Run all tests and commit persistence.

### Task 4: Add Windows adapters and WPF shell

**Files:**
- Create: `src/SleepTimer.Windows/SleepTimer.Windows.csproj`, `src/SleepTimer.Windows/WindowsScheduledTaskStore.cs`, `src/SleepTimer.Windows/WindowsPowerExecutor.cs`
- Create: `src/SleepTimer.App/SleepTimer.App.csproj`, `src/SleepTimer.App/App.xaml`, `src/SleepTimer.App/MainWindow.xaml`, `src/SleepTimer.App/MainWindow.xaml.cs`

- [ ] Add the WPF executable targeting `net8.0-windows`, x64 publish settings, and an app-owned task name prefix.
- [ ] Implement normal shutdown, forced shutdown, and sleep adapters behind the core interfaces; never upgrade normal shutdown to forced mode.
- [ ] Build a dark home view with action toggle, four editable presets, specified-time entry, and a single start button.
- [ ] Build the countdown view with add/subtract-30-minutes, cancel, and hide-to-tray commands.
- [ ] Add single-instance activation and tray lifecycle without cancelling tasks when the window is hidden.

### Task 5: Verify portable Windows delivery

**Files:**
- Modify: `src/SleepTimer.App/SleepTimer.App.csproj`
- Create: `README.md`, `scripts/publish.ps1`

- [ ] Add a publish script that writes all output under `E:\codex_project\artifacts\win-x64` and never uses a user profile data path.
- [ ] Run core tests, build/publish checks, and inspect the output for expected files and no test logs/tasks.
- [ ] Document launch, tray behavior, safety confirmation, and the limitation that real power actions require Windows integration testing.

