using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MTS2000.App.Services;
using MTS2000.Core.Models;
using MTS2000.Core.Services;

namespace MTS2000.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CodeplugFileService _fileService = new();
    private readonly IRadioCommunicationService _radioService;
    private readonly AppSettings _appSettings = AppSettings.Load();
    private readonly DebugLogService _debugLog;
    private CancellationTokenSource? _operationCancellationSource;

    [ObservableProperty]
    private Codeplug _codeplug = Codeplug.CreateDefault();

    [ObservableProperty]
    private Zone? _selectedZone;

    [ObservableProperty]
    private Channel? _selectedChannel;

    [ObservableProperty]
    private string? _selectedPortName;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _rawMemoryAddressHex = "0000";

    [ObservableProperty]
    private int _rawMemoryLength = 256;

    [ObservableProperty]
    private string _fixtureModel = string.Empty;

    [ObservableProperty]
    private string _fixtureBand = string.Empty;

    [ObservableProperty]
    private string _fixtureSerialNumber = string.Empty;

    [ObservableProperty]
    private string _fixtureFirmwareSignatureHex = string.Empty;

    /// <summary>True once the codeplug has been edited since it was created/loaded/saved.</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>True when a radio command can be safely started: connected and no other radio operation is already in flight.</summary>
    public bool CanOperateRadio => IsConnected && !IsBusy;

    /// <summary>True when it's safe to attempt a connection: not already connected and nothing else in flight.</summary>
    public bool CanConnect => !IsConnected && !IsBusy;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOperateRadio));
        OnPropertyChanged(nameof(CanConnect));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOperateRadio));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanCancelOperation));
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<string> AvailablePortNames => _radioService.GetAvailablePortNames();

    public bool HasNoAvailablePorts => AvailablePortNames.Count == 0;

    private CancellationToken OperationCancellationToken =>
        _operationCancellationSource?.Token ?? CancellationToken.None;

    public bool CanCancelOperation => IsBusy;

    public string LogFilePath => _debugLog.CurrentLogPath;

    public MainViewModel() : this(new SerialRadioCommunicationService())
    {
    }

    public MainViewModel(IRadioCommunicationService radioService, DebugLogService? debugLog = null)
    {
        _radioService = radioService;
        _debugLog = debugLog ?? new DebugLogService();
        _debugLog.LogWritten += OnLogWritten;
        _debugLog.Info("Application session initialized.");
        RefreshLogText();
        _radioService.StatusChanged += OnRadioStatusChanged;
        AttachDirtyTracking(Codeplug);
        SelectedZone = Codeplug.Zones.FirstOrDefault();
        SelectedChannel = SelectedZone?.Channels.FirstOrDefault();

        if (_appSettings.LastPortName is { } lastPort && AvailablePortNames.Contains(lastPort))
        {
            SelectedPortName = lastPort;
        }
    }

    partial void OnCodeplugChanged(Codeplug? oldValue, Codeplug newValue)
    {
        if (oldValue is not null)
        {
            DetachDirtyTracking(oldValue);
        }

        AttachDirtyTracking(newValue);
        IsDirty = false;
    }

    private void AttachDirtyTracking(Codeplug codeplug)
    {
        codeplug.Settings.PropertyChanged += MarkDirty;
        codeplug.Zones.CollectionChanged += OnZonesCollectionChanged;
        foreach (var zone in codeplug.Zones)
        {
            AttachZone(zone);
        }
    }

    private void DetachDirtyTracking(Codeplug codeplug)
    {
        codeplug.Settings.PropertyChanged -= MarkDirty;
        codeplug.Zones.CollectionChanged -= OnZonesCollectionChanged;
        foreach (var zone in codeplug.Zones)
        {
            DetachZone(zone);
        }
    }

    private void AttachZone(Zone zone)
    {
        zone.PropertyChanged += MarkDirty;
        zone.Channels.CollectionChanged += OnChannelsCollectionChanged;
        foreach (var channel in zone.Channels)
        {
            AttachChannel(channel);
        }
    }

    private void DetachZone(Zone zone)
    {
        zone.PropertyChanged -= MarkDirty;
        zone.Channels.CollectionChanged -= OnChannelsCollectionChanged;
        foreach (var channel in zone.Channels)
        {
            DetachChannel(channel);
        }
    }

    private void AttachChannel(Channel channel) => channel.PropertyChanged += MarkDirty;

    private void DetachChannel(Channel channel) => channel.PropertyChanged -= MarkDirty;

    private void OnZonesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (Zone zone in e.NewItems)
            {
                AttachZone(zone);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (Zone zone in e.OldItems)
            {
                DetachZone(zone);
            }
        }

        IsDirty = true;
    }

    private void OnChannelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (Channel channel in e.NewItems)
            {
                AttachChannel(channel);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (Channel channel in e.OldItems)
            {
                DetachChannel(channel);
            }
        }

        IsDirty = true;
    }

    private void MarkDirty(object? sender, PropertyChangedEventArgs e) => IsDirty = true;

    /// <summary>Prompts to confirm discarding unsaved changes if there are any; returns true if it's OK to proceed.</summary>
    private bool ConfirmDiscardIfDirty(string action)
    {
        if (!IsDirty)
        {
            return true;
        }

        return MessageBox.Show(
            $"You have unsaved codeplug changes. {action} anyway?",
            "Unsaved changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>Called from the window's Closing handler; returns false if the close should be cancelled.</summary>
    public bool ConfirmClose()
    {
        if (IsBusy)
        {
            MessageBox.Show(
                "A radio operation is still in progress. Wait for it to finish before closing.",
                "Operation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return ConfirmDiscardIfDirty("Close the application");
    }

    /// <summary>Forwards low-level protocol status (retries, timeouts, mode transitions) to the status bar.
    /// Raised from a background thread by the radio service, so it must hop to the UI thread.</summary>
    private void OnRadioStatusChanged(object? sender, string status)
    {
        _debugLog.Info($"Radio status: {status}");
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            StatusMessage = status;
        }
        else
        {
            dispatcher.BeginInvoke(() => StatusMessage = status);
        }
    }

    private void OnLogWritten(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshLogText();
        }
        else
        {
            dispatcher.BeginInvoke(RefreshLogText);
        }
    }

    private void RefreshLogText()
    {
        LogText = _debugLog.ReadCurrentLog();
        OnPropertyChanged(nameof(LogFilePath));
    }

    [RelayCommand]
    private void RefreshLog()
    {
        RefreshLogText();
    }

    [RelayCommand]
    private void AddZone()
    {
        var zone = new Zone { Name = $"Zone {Codeplug.Zones.Count + 1}" };
        Codeplug.Zones.Add(zone);
        SelectedZone = zone;
        _debugLog.Info($"Zone added: {zone.Name}.");
    }

    [RelayCommand]
    private void RemoveZone(Zone? zone)
    {
        zone ??= SelectedZone;
        if (zone is null)
        {
            return;
        }

        if (!ConfirmDelete($"Delete zone \"{zone.Name}\" and all its channels?"))
        {
            return;
        }

        Codeplug.Zones.Remove(zone);
        SelectedZone = Codeplug.Zones.FirstOrDefault();
        _debugLog.Info($"Zone removed: {zone.Name}.");
    }

    [RelayCommand]
    private void AddChannel()
    {
        if (SelectedZone is null)
        {
            return;
        }

        var channel = new Channel { Name = $"Channel {SelectedZone.Channels.Count + 1}" };
        SelectedZone.Channels.Add(channel);
        SelectedChannel = channel;
        _debugLog.Info($"Channel added: {channel.Name} in zone {SelectedZone.Name}.");
    }

    [RelayCommand]
    private void RemoveChannel(Channel? channel)
    {
        channel ??= SelectedChannel;
        if (SelectedZone is null || channel is null)
        {
            return;
        }

        if (!ConfirmDelete($"Delete channel \"{channel.Name}\"?"))
        {
            return;
        }

        SelectedZone.Channels.Remove(channel);
        SelectedChannel = SelectedZone.Channels.FirstOrDefault();
        _debugLog.Info($"Channel removed: {channel.Name}.");
    }

    private static bool ConfirmDelete(string message) =>
        MessageBox.Show(message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    [RelayCommand]
    private void OpenCodeplug()
    {
        if (!ConfirmDiscardIfDirty("Discard your unsaved changes and open a different codeplug"))
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Codeplug files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Codeplug = _fileService.Load(dialog.FileName);
            SelectedZone = Codeplug.Zones.FirstOrDefault();
            SelectedChannel = SelectedZone?.Channels.FirstOrDefault();
            StatusMessage = $"Loaded {Path.GetFileName(dialog.FileName)}.";
            _debugLog.Info($"Codeplug loaded from {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load codeplug: {ex.Message}";
            _debugLog.Error($"Codeplug load failed: {dialog.FileName}.", ex);
        }
    }

    [RelayCommand]
    private void SaveCodeplug()
    {
        var dialog = new SaveFileDialog { Filter = "Codeplug files (*.json)|*.json|All files (*.*)|*.*", FileName = "codeplug.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _fileService.Save(Codeplug, dialog.FileName);
            StatusMessage = $"Saved {Path.GetFileName(dialog.FileName)}.";
            IsDirty = false;
            _debugLog.Info($"Codeplug saved to {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save codeplug: {ex.Message}";
            _debugLog.Error($"Codeplug save failed: {dialog.FileName}.", ex);
        }
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        OnPropertyChanged(nameof(AvailablePortNames));
        OnPropertyChanged(nameof(HasNoAvailablePorts));
        _debugLog.Info($"Serial ports refreshed. Available: {AvailablePortNames.Count}.");
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrEmpty(SelectedPortName))
        {
            StatusMessage = "Select a COM port first.";
            return;
        }

        if (!TryStartBusy())
        {
            return;
        }

        var portName = SelectedPortName;
        var cancellationToken = OperationCancellationToken;
        try
        {
            _debugLog.Info($"Connect started for {portName}.");
            StatusMessage = $"Connecting to {portName}...";
            await Task.Run(() => _radioService.Connect(portName), cancellationToken);
            StatusMessage = "Verifying radio link...";
            await _radioService.GetFirmwareVersionAsync(cancellationToken);
            IsConnected = true;
            StatusMessage = $"Connected to {portName}.";
            _appSettings.LastPortName = portName;
            _appSettings.Save();
            _debugLog.Info($"Connect completed for {portName}. Firmware handshake succeeded.");
        }
        catch (OperationCanceledException)
        {
            InvalidateRadioConnection();
            StatusMessage = "Connection cancelled.";
            _debugLog.Warning($"Connect cancelled for {portName}.");
        }
        catch (Exception ex)
        {
            InvalidateRadioConnection();
            StatusMessage = $"Failed to connect: {ex.Message}";
            _debugLog.Error($"Connect failed for {portName}.", ex);
        }
        finally
        {
            FinishBusy();
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        if (IsBusy)
        {
            StatusMessage = "Cannot disconnect while an operation is in progress.";
            return;
        }

        _radioService.Disconnect();
        IsConnected = false;
        StatusMessage = "Disconnected.";
        _debugLog.Info("Radio disconnected by user.");
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        _operationCancellationSource?.Cancel();
        StatusMessage = "Cancelling radio operation...";
        _debugLog.Warning("Radio operation cancellation requested by user.");
    }

    /// <summary>
    /// Called when the app is closing: releases the COM port if still connected. Skips
    /// disconnecting while an operation is in flight (<see cref="IsBusy"/>) to avoid disposing
    /// the port out from under a background read/write; the process exiting will reclaim the
    /// handle anyway. Never throws, since this runs synchronously from <c>Window.Closing</c>.
    /// </summary>
    public void Shutdown()
    {
        if (!IsConnected || IsBusy)
        {
            return;
        }

        try
        {
            _radioService.Disconnect();
            _debugLog.Info("Application shutdown disconnected the radio.");
        }
        catch
        {
            // Best-effort on the way out; the process is exiting regardless.
            _debugLog.Warning("Application shutdown could not disconnect the radio.");
        }

        IsConnected = false;
    }

    [RelayCommand]
    private async Task ReadFromRadioAsync()
    {
        if (!ConfirmDiscardIfDirty("Discard your unsaved changes and read a codeplug from the radio"))
        {
            return;
        }

        if (!TryStartBusy())
        {
            return;
        }

        try
        {
            _debugLog.Info("Structured codeplug read started.");
            StatusMessage = "Reading codeplug from radio...";
            Codeplug = await _radioService.ReadCodeplugAsync(OperationCancellationToken);
            SelectedZone = Codeplug.Zones.FirstOrDefault();
            SelectedChannel = SelectedZone?.Channels.FirstOrDefault();
            StatusMessage = "Read complete.";
            _debugLog.Info("Structured codeplug read completed.");
        }
        catch (OperationCanceledException)
        {
            InvalidateRadioConnection();
            StatusMessage = "Read cancelled.";
            _debugLog.Warning("Structured codeplug read cancelled.");
        }
        catch (Exception ex)
        {
            InvalidateRadioConnection();
            StatusMessage = $"Read failed: {ex.Message}";
            _debugLog.Error("Structured codeplug read failed.", ex);
        }
        finally
        {
            FinishBusy();
        }
    }

    [RelayCommand]
    private async Task GetFirmwareVersionAsync()
    {
        if (!TryStartBusy())
        {
            return;
        }

        try
        {
            _debugLog.Info("Firmware version query started.");
            StatusMessage = "Requesting firmware version...";
            var version = await _radioService.GetFirmwareVersionAsync(OperationCancellationToken);
            StatusMessage = $"Radio firmware version: {version}";
            _debugLog.Info($"Firmware version query completed: {version}.");
        }
        catch (OperationCanceledException)
        {
            InvalidateRadioConnection();
            StatusMessage = "Firmware version request cancelled.";
            _debugLog.Warning("Firmware version query cancelled.");
        }
        catch (Exception ex)
        {
            InvalidateRadioConnection();
            StatusMessage = $"Firmware version request failed: {ex.Message}";
            _debugLog.Error("Firmware version query failed.", ex);
        }
        finally
        {
            FinishBusy();
        }
    }

    [RelayCommand]
    private async Task WriteToRadioAsync()
    {
        if (!TryStartBusy())
        {
            return;
        }

        try
        {
            _debugLog.Info("Structured codeplug write started.");
            StatusMessage = "Writing codeplug to radio...";
            await _radioService.WriteCodeplugAsync(Codeplug, OperationCancellationToken);
            StatusMessage = "Write complete.";
            _debugLog.Info("Structured codeplug write completed.");
        }
        catch (OperationCanceledException)
        {
            InvalidateRadioConnection();
            StatusMessage = "Write cancelled.";
            _debugLog.Warning("Structured codeplug write cancelled.");
        }
        catch (Exception ex)
        {
            InvalidateRadioConnection();
            StatusMessage = $"Write failed: {ex.Message}";
            _debugLog.Error("Structured codeplug write failed.", ex);
        }
        finally
        {
            FinishBusy();
        }
    }

    /// <summary>
    /// Dumps a raw range of EEPROM to a .bin file. This does not attempt to interpret the bytes
    /// as channels/zones (that codeplug block format is not implemented yet) — it's a raw backup
    /// using the working low-level transport, chunked into &lt;=255-byte reads per protocol frame.
    /// </summary>
    [RelayCommand]
    private async Task ReadRawMemoryAsync()
    {
        if (!TryParseRawMemoryRange(out var address, out var length))
        {
            return;
        }

        var dialog = new SaveFileDialog { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*", FileName = "eeprom-dump.bin" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!TryStartBusy())
        {
            return;
        }

        try
        {
            _debugLog.Info($"Raw memory read started: address=0x{address:X4}, length={length}.");
            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                OperationCancellationToken.ThrowIfCancellationRequested();
                var chunkSize = Math.Min(0xFF, length - offset);
                StatusMessage = $"Reading 0x{address + offset:X4} ({offset + chunkSize}/{length})...";
                var chunk = await _radioService.ReadMemoryAsync(address + offset, chunkSize, OperationCancellationToken);
                if (chunk.Length != chunkSize)
                {
                    throw new InvalidDataException("Radio returned an unexpected byte count during the raw read.");
                }

                chunk.CopyTo(buffer, offset);
                offset += chunkSize;
            }

            await File.WriteAllBytesAsync(dialog.FileName, buffer, OperationCancellationToken);
            StatusMessage = $"Saved {length} bytes from 0x{address:X4} to {Path.GetFileName(dialog.FileName)}.";
            _debugLog.Info($"Raw memory read completed: address=0x{address:X4}, length={length}, file={dialog.FileName}.");
        }
        catch (OperationCanceledException)
        {
            InvalidateRadioConnection();
            StatusMessage = "Raw read cancelled.";
            _debugLog.Warning($"Raw memory read cancelled: address=0x{address:X4}, length={length}.");
        }
        catch (Exception ex)
        {
            InvalidateRadioConnection();
            StatusMessage = $"Raw read failed: {ex.Message}";
            _debugLog.Error($"Raw memory read failed: address=0x{address:X4}, length={length}.", ex);
        }
        finally
        {
            FinishBusy();
        }
    }

    [RelayCommand]
    private async Task WriteRawMemoryAsync()
    {
        if (!TryParseRawMemoryAddress(out var address))
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        byte[] buffer;
        try
        {
            buffer = await File.ReadAllBytesAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Raw write failed: {ex.Message}";
            return;
        }

        if (!TryValidateRawMemoryRange(address, buffer.Length))
        {
            return;
        }

        if (!ConfirmDelete($"Write {buffer.Length} bytes to radio EEPROM starting at 0x{address:X4}? A backup will be saved beside the source file first."))
        {
            return;
        }

        if (!TryStartBusy())
        {
            return;
        }

        try
        {
            _debugLog.Info($"Raw memory write started: address=0x{address:X4}, length={buffer.Length}, source={dialog.FileName}.");
            var backup = new byte[buffer.Length];
            var backupOffset = 0;
            while (backupOffset < backup.Length)
            {
                OperationCancellationToken.ThrowIfCancellationRequested();
                var chunkSize = Math.Min(0xFF, backup.Length - backupOffset);
                var chunk = await _radioService.ReadMemoryAsync(address + backupOffset, chunkSize, OperationCancellationToken);
                if (chunk.Length != chunkSize)
                {
                    throw new InvalidDataException("Radio returned an unexpected byte count while creating the pre-write backup.");
                }

                chunk.CopyTo(backup, backupOffset);
                backupOffset += chunkSize;
            }

            var backupPath = Path.Combine(
                Path.GetDirectoryName(dialog.FileName)!,
                $"{Path.GetFileNameWithoutExtension(dialog.FileName)}.before-write-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bin");
            await File.WriteAllBytesAsync(backupPath, backup, OperationCancellationToken);

            var offset = 0;
            while (offset < buffer.Length)
            {
                OperationCancellationToken.ThrowIfCancellationRequested();
                var chunkSize = Math.Min(0xFF, buffer.Length - offset);
                StatusMessage = $"Writing 0x{address + offset:X4} ({offset + chunkSize}/{buffer.Length})...";
                await _radioService.WriteMemoryAsync(address + offset, buffer[offset..(offset + chunkSize)], OperationCancellationToken);
                offset += chunkSize;
            }

            var verifyOffset = 0;
            while (verifyOffset < buffer.Length)
            {
                OperationCancellationToken.ThrowIfCancellationRequested();
                var chunkSize = Math.Min(0xFF, buffer.Length - verifyOffset);
                var actual = await _radioService.ReadMemoryAsync(address + verifyOffset, chunkSize, OperationCancellationToken);
                if (!actual.SequenceEqual(buffer[verifyOffset..(verifyOffset + chunkSize)]))
                {
                    throw new InvalidDataException($"Read-back verification failed at 0x{address + verifyOffset:X4}.");
                }

                verifyOffset += chunkSize;
            }

            StatusMessage = $"Wrote and verified {buffer.Length} bytes. Backup: {Path.GetFileName(backupPath)}.";
            _debugLog.Info($"Raw memory write completed and verified: address=0x{address:X4}, length={buffer.Length}, backup={backupPath}.");
        }
        catch (OperationCanceledException)
        {
            InvalidateRadioConnection();
            StatusMessage = "Raw write cancelled.";
            _debugLog.Warning($"Raw memory write cancelled: address=0x{address:X4}, length={buffer.Length}.");
        }
        catch (Exception ex)
        {
            InvalidateRadioConnection();
            StatusMessage = $"Raw write failed: {ex.Message}";
            _debugLog.Error($"Raw memory write failed: address=0x{address:X4}, length={buffer.Length}.", ex);
        }
        finally
        {
            FinishBusy();
        }
    }

    /// <summary>
    /// Captures a read-only fixture package for implementing and validating the codeplug codec.
    /// The package contains the complete EEPROM image, radio metadata, firmware signature input,
    /// and the editor's current expected decoded model. It never writes to the radio.
    /// </summary>
    [RelayCommand]
    private async Task CaptureCodeplugFixtureAsync()
    {
        if (!TryValidateFixtureMetadata())
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Fixture manifest (*.json)|*.json",
            FileName = "mts2000-fixture.json",
        };
        if (dialog.ShowDialog() != true || !TryStartBusy())
        {
            return;
        }

        var manifestPath = dialog.FileName;
        var directory = Path.GetDirectoryName(manifestPath)!;
        var stem = Path.GetFileNameWithoutExtension(manifestPath);
        var dumpPath = Path.Combine(directory, $"{stem}.eeprom.bin");
        var expectedPath = Path.Combine(directory, $"{stem}.expected-codeplug.json");

        try
        {
            _debugLog.Info($"Codeplug fixture capture started: manifest={manifestPath}.");
            StatusMessage = "Querying firmware version for fixture...";
            var firmwareVersion = await _radioService.GetFirmwareVersionAsync(OperationCancellationToken);

            const int imageLength = 0x8200;
            var image = new byte[imageLength];
            var offset = 0;
            while (offset < image.Length)
            {
                OperationCancellationToken.ThrowIfCancellationRequested();
                var chunkSize = Math.Min(0xFF, image.Length - offset);
                StatusMessage = $"Capturing EEPROM 0x{offset:X4} ({offset + chunkSize}/{image.Length})...";
                var chunk = await _radioService.ReadMemoryAsync(offset, chunkSize, OperationCancellationToken);
                if (chunk.Length != chunkSize)
                {
                    throw new InvalidDataException("Radio returned an unexpected byte count during fixture capture.");
                }

                chunk.CopyTo(image, offset);
                offset += chunkSize;
            }

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllBytesAsync(dumpPath, image, OperationCancellationToken);
            await File.WriteAllTextAsync(expectedPath, JsonSerializer.Serialize(Codeplug, jsonOptions), OperationCancellationToken);

            var manifest = new
            {
                Format = "MTS2000 codeplug fixture v1",
                CapturedAt = DateTimeOffset.Now,
                Model = FixtureModel.Trim(),
                Band = FixtureBand.Trim(),
                SerialNumber = FixtureSerialNumber.Trim(),
                FirmwareVersion = firmwareVersion,
                FirmwareSignatureHex = FixtureFirmwareSignatureHex.Trim().Replace(" ", string.Empty),
                EepromImageLength = image.Length,
                EepromImageFile = Path.GetFileName(dumpPath),
                ExpectedDecodedCodeplugFile = Path.GetFileName(expectedPath),
                WriteValidation = "Read-only capture. Do not enable writes until decode/encode round-trip matches the EEPROM image.",
            };

            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, jsonOptions), OperationCancellationToken);
            StatusMessage = $"Fixture captured: {Path.GetFileName(manifestPath)}.";
            _debugLog.Info($"Codeplug fixture capture completed: manifest={manifestPath}, imageLength={image.Length}.");
        }
        catch (OperationCanceledException)
        {
            InvalidateRadioConnection();
            StatusMessage = "Fixture capture cancelled.";
            _debugLog.Warning("Codeplug fixture capture cancelled.");
        }
        catch (Exception ex)
        {
            InvalidateRadioConnection();
            StatusMessage = $"Fixture capture failed: {ex.Message}";
            _debugLog.Error("Codeplug fixture capture failed.", ex);
        }
        finally
        {
            FinishBusy();
        }
    }

    private bool TryValidateFixtureMetadata()
    {
        if (string.IsNullOrWhiteSpace(FixtureModel) ||
            string.IsNullOrWhiteSpace(FixtureBand) ||
            string.IsNullOrWhiteSpace(FixtureSerialNumber))
        {
            StatusMessage = "Enter the fixture model, band, and serial number first.";
            return false;
        }

        var signature = FixtureFirmwareSignatureHex.Replace(" ", string.Empty);
        if (!signature.All(Uri.IsHexDigit) || signature.Length == 0 || signature.Length % 2 != 0)
        {
            StatusMessage = "Enter firmware signature bytes as an even-length hexadecimal string.";
            return false;
        }

        return true;
    }

    private bool TryStartBusy()
    {
        if (IsBusy)
        {
            StatusMessage = "Another radio operation is already in progress.";
            return false;
        }

        _operationCancellationSource = new CancellationTokenSource();
        IsBusy = true;
        return true;
    }

    private void FinishBusy()
    {
        _operationCancellationSource?.Dispose();
        _operationCancellationSource = null;
        IsBusy = false;
    }

    private void InvalidateRadioConnection()
    {
        try
        {
            _radioService.Disconnect();
        }
        catch
        {
            // Preserve the original operation failure while making the UI fail closed.
        }

        IsConnected = false;
    }

    private bool TryParseRawMemoryAddress(out int address)
    {
        if (!int.TryParse(RawMemoryAddressHex, System.Globalization.NumberStyles.HexNumber, null, out address) || address is < 0 or > 0xFFFF)
        {
            StatusMessage = "Enter a valid hex address between 0000 and FFFF.";
            address = 0;
            return false;
        }

        return true;
    }

    private bool TryParseRawMemoryRange(out int address, out int length)
    {
        length = RawMemoryLength;
        if (!TryParseRawMemoryAddress(out address))
        {
            return false;
        }

        if (!TryValidateRawMemoryRange(address, length))
        {
            StatusMessage = "Length must be positive and must not run past address 0xFFFF.";
            return false;
        }

        return true;
    }

    private bool TryValidateRawMemoryRange(int address, int length)
    {
        if (length < 1 || (long)address + length > 0x10000)
        {
            StatusMessage = "The file length must be positive and must not run past address 0xFFFF.";
            return false;
        }

        return true;
    }
}
