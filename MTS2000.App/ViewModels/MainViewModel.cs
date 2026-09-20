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

    public IReadOnlyList<string> AvailablePortNames => _radioService.GetAvailablePortNames();

    public MainViewModel() : this(new SerialRadioCommunicationService())
    {
    }

    public MainViewModel(IRadioCommunicationService radioService)
    {
        _radioService = radioService;
        SelectedZone = Codeplug.Zones.FirstOrDefault();
        SelectedChannel = SelectedZone?.Channels.FirstOrDefault();

        if (_appSettings.LastPortName is { } lastPort && AvailablePortNames.Contains(lastPort))
        {
            SelectedPortName = lastPort;
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
        _radioService.Disconnect();
        IsConnected = false;
        StatusMessage = "Disconnected.";
    }

    [RelayCommand]
    private async Task ReadFromRadioAsync()
    {
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
    }

    [RelayCommand]
    private async Task GetFirmwareVersionAsync()
    {
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
    }

    [RelayCommand]
    private async Task WriteToRadioAsync()
    {
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
    }
}
