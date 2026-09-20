# Sprint: Workflow Fixes

Source: full code review (2026-09-20), focused on runtime workflow (connect -> command -> radio I/O).
Goal: fix the correctness/safety issues found, in priority order. Each task should build, pass
`dotnet test MTS2000.slnx`, and be committed/pushed individually so CI validates each step.

## High priority

### 1. Enter programming mode before any EEPROM access
**Problem:** `ReadMemoryAsync`/`WriteMemoryAsync` in `SerialRadioCommunicationService` call
`session.ReadEeprom`/`WriteEeprom` directly, which only activates extended mode
(`ActivateExtendedModeIfNeeded`) and never calls `EnterProgrammingMode()`. Only
`GetFirmwareVersionAsync` does that today. The Raw Memory tab's buttons are enabled as soon as
`IsConnected` is true, so a user can read/write EEPROM without the radio ever having been put in
programming mode.

**Fix:**
- [x] Add an internal "has programming mode been entered this session" flag to
      `RadioProgrammingSession` (or just make `EnterProgrammingMode()` idempotent per-session and
      call it from `ReadEeprom`/`WriteEeprom` before `ActivateExtendedModeIfNeeded()`).
- [ ] Alternative/simpler: call `session.EnterProgrammingMode()` once inside
      `SerialRadioCommunicationService.Connect()` right after opening the session, so every
      subsequent call (read, write, firmware query) can assume programming mode is already active.
- [x] Add a `RadioProgrammingSessionTests` case using `FakeRadioTransport` asserting
      `ReadEeprom`/`WriteEeprom` work without an explicit prior `EnterProgrammingMode()` call (i.e.
      the session handles it internally), or asserting `Connect()` triggers it once.

### 2. Guard against overlapping async radio commands
**Problem:** No `IsBusy`/`CanExecute` guard on `ReadFromRadioCommand`, `WriteToRadioCommand`,
`GetFirmwareVersionCommand`, `ReadRawMemoryCommand`, `WriteRawMemoryCommand`, `ConnectCommand`, or
`DisconnectCommand`. Buttons are only gated on `IsConnected`. Two quick clicks, or a `Disconnect`
during an in-flight read, can run overlapping operations against the same
`RadioProgrammingSession`/`SerialPort`, or race a `Dispose()` against an in-flight
`Task.Run` closure.

**Fix:**
- [x] Add `[ObservableProperty] private bool _isBusy;` to `MainViewModel`.
- [x] Set `IsBusy = true` at the start / `false` at the end (try/finally) of every async radio
      command (`ReadFromRadioAsync`, `WriteToRadioAsync`, `GetFirmwareVersionAsync`,
      `ReadRawMemoryAsync`, `WriteRawMemoryAsync`).
- [x] Bind `IsEnabled` on Connect/Disconnect/Get Firmware Version/Read/Write/Raw Memory buttons to
      also require `!IsBusy` (combine with existing `IsConnected` binding via a multi-binding
      converter, or add a computed `CanOperateRadio => IsConnected && !IsBusy` property with
      `OnPropertyChanged` raised from both `IsConnected` and `IsBusy` setters).
- [x] Make `Disconnect()` refuse (or wait) while `IsBusy` is true, or at minimum show a status
      message instead of disposing mid-operation.

## Medium priority

### 3. Surface `RadioProgrammingSession.StatusChanged` to the UI
**Problem:** The session raises granular status strings but `SerialRadioCommunicationService`
never subscribes, so retries/timeouts are invisible until a terminal exception.

**Fix:**
- [x] Add `event EventHandler<string>? StatusChanged;` to `IRadioCommunicationService`.
- [x] In `SerialRadioCommunicationService.Connect()`, subscribe to
      `_session.StatusChanged` and re-raise it.
- [x] In `MainViewModel`, subscribe once (in the constructor) and set `StatusMessage` from the
      forwarded event.

### 4. Disconnect on window close
**Problem:** `MainWindow` never disposes `MainViewModel`'s radio service; closing the app with the
radio connected leaves the COM port open until process exit.

**Fix:**
- [ ] Add a `Closing` (or `Closed`) handler in `MainWindow.xaml.cs` that calls
      `DisconnectCommand.Execute(null)` (or exposes a `Shutdown()` method on `MainViewModel`).

### 5. Make `Connect()` non-blocking
**Problem:** Unlike every other radio operation, `Connect()` calls `_radioService.Connect(...)`
synchronously on the UI thread, which can hang the UI if the serial port is slow to open.

**Fix:**
- [ ] Change `ConnectCommand` to `async Task ConnectAsync()` and wrap the connect call in
      `Task.Run`, consistent with the other radio commands.

## Low priority / cleanup

### 6. Remove or use the unused `IProgress<string>` parameters
**Problem:** `ReadCodeplugAsync`/`WriteCodeplugAsync` accept `IProgress<string>?` that nothing
passes or reports through.

**Fix:**
- [ ] Either wire progress reporting through once codeplug block decoding exists, or drop the
      parameter until then to avoid dead API surface.

### 7. Add a view-model-level regression test for #2
**Fix:**
- [ ] Once `IsBusy` exists, add a `MainViewModelTests` (new test file) using `FakeRadioTransport`
      that asserts a second command invocation while `IsBusy` is true is a no-op / blocked.

## Definition of done
- All checkboxes above checked.
- `dotnet build MTS2000.slnx` and `dotnet test MTS2000.slnx` green locally and in CI.
- Manual smoke test: launch app, connect (fake/real), click a radio command twice quickly with no
  crash or interleaved output, close the app while "connected" with no unhandled exception.
