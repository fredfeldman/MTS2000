using System.IO;
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
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _rawMemoryAddressHex = "0000";

    [ObservableProperty]
    private int _rawMemoryLength = 256;

    /// <summary>True when a radio command can be safely started: connected and no other radio operation is already in flight.</summary>
    public bool CanOperateRadio => IsConnected && !IsBusy;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(CanOperateRadio));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanOperateRadio));

    public IReadOnlyList<string> AvailablePortNames => _radioService.GetAvailablePortNames();

    public MainViewModel() : this(new SerialRadioCommunicationService())
    {
    }

    public MainViewModel(IRadioCommunicationService radioService)
    {
        _radioService = radioService;
        _radioService.StatusChanged += OnRadioStatusChanged;
        SelectedZone = Codeplug.Zones.FirstOrDefault();
        SelectedChannel = SelectedZone?.Channels.FirstOrDefault();

        if (_appSettings.LastPortName is { } lastPort && AvailablePortNames.Contains(lastPort))
        {
            SelectedPortName = lastPort;
        }
    }

    /// <summary>Forwards low-level protocol status (retries, timeouts, mode transitions) to the status bar.
    /// Raised from a background thread by the radio service, so it must hop to the UI thread.</summary>
    private void OnRadioStatusChanged(object? sender, string status)
    {
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

    [RelayCommand]
    private void AddZone()
    {
        var zone = new Zone { Name = $"Zone {Codeplug.Zones.Count + 1}" };
        Codeplug.Zones.Add(zone);
        SelectedZone = zone;
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
    }

    private static bool ConfirmDelete(string message) =>
        MessageBox.Show(message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    [RelayCommand]
    private void OpenCodeplug()
    {
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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load codeplug: {ex.Message}";
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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save codeplug: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        OnPropertyChanged(nameof(AvailablePortNames));
    }

    [RelayCommand]
    private void Connect()
    {
        if (string.IsNullOrEmpty(SelectedPortName))
        {
            StatusMessage = "Select a COM port first.";
            return;
        }

        try
        {
            _radioService.Connect(SelectedPortName);
            IsConnected = true;
            StatusMessage = $"Connected to {SelectedPortName}.";
            _appSettings.LastPortName = SelectedPortName;
            _appSettings.Save();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to connect: {ex.Message}";
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
    }

    [RelayCommand]
    private async Task ReadFromRadioAsync()
    {
        if (!TryStartBusy())
        {
            return;
        }

        try
        {
            StatusMessage = "Reading codeplug from radio...";
            Codeplug = await _radioService.ReadCodeplugAsync();
            SelectedZone = Codeplug.Zones.FirstOrDefault();
            SelectedChannel = SelectedZone?.Channels.FirstOrDefault();
            StatusMessage = "Read complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
            StatusMessage = "Requesting firmware version...";
            var version = await _radioService.GetFirmwareVersionAsync();
            StatusMessage = $"Radio firmware version: {version}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Firmware version request failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
            StatusMessage = "Writing codeplug to radio...";
            await _radioService.WriteCodeplugAsync(Codeplug);
            StatusMessage = "Write complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Write failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var chunkSize = Math.Min(0xFF, length - offset);
                StatusMessage = $"Reading 0x{address + offset:X4} ({offset + chunkSize}/{length})...";
                var chunk = await _radioService.ReadMemoryAsync(address + offset, chunkSize);
                chunk.CopyTo(buffer, offset);
                offset += chunkSize;
            }

            await File.WriteAllBytesAsync(dialog.FileName, buffer);
            StatusMessage = $"Saved {length} bytes from 0x{address:X4} to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Raw read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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

        if (!ConfirmDelete($"Write {new FileInfo(dialog.FileName).Length} bytes to radio EEPROM starting at 0x{address:X4}? This can corrupt the radio's programming if the range is wrong."))
        {
            return;
        }

        if (!TryStartBusy())
        {
            return;
        }

        try
        {
            var buffer = await File.ReadAllBytesAsync(dialog.FileName);
            var offset = 0;
            while (offset < buffer.Length)
            {
                var chunkSize = Math.Min(0xFF, buffer.Length - offset);
                StatusMessage = $"Writing 0x{address + offset:X4} ({offset + chunkSize}/{buffer.Length})...";
                await _radioService.WriteMemoryAsync(address + offset, buffer[offset..(offset + chunkSize)]);
                offset += chunkSize;
            }

            StatusMessage = $"Wrote {buffer.Length} bytes from {Path.GetFileName(dialog.FileName)} to 0x{address:X4}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Raw write failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryStartBusy()
    {
        if (IsBusy)
        {
            StatusMessage = "Another radio operation is already in progress.";
            return false;
        }

        IsBusy = true;
        return true;
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

        if (length < 1 || address + length > 0x10000)
        {
            StatusMessage = "Length must be positive and must not run past address 0xFFFF.";
            return false;
        }

        return true;
    }
}
