using CommunityToolkit.Mvvm.ComponentModel;

namespace MTS2000.Core.Models;

/// <summary>A single radio channel entry within a zone.</summary>
public partial class Channel : ObservableObject
{
    [ObservableProperty]
    private string _name = "New Channel";

    [ObservableProperty]
    private double _receiveFrequencyMhz = 154.0000;

    [ObservableProperty]
    private double _transmitFrequencyMhz = 154.0000;

    [ObservableProperty]
    private string _receiveTone = "None";

    [ObservableProperty]
    private string _transmitTone = "None";

    [ObservableProperty]
    private ChannelBandwidth _bandwidth = ChannelBandwidth.Wide25kHz;

    [ObservableProperty]
    private bool _isTransmitAllowed = true;

    [ObservableProperty]
    private ScanListMode _scanListMode = ScanListMode.None;
}

public enum ChannelBandwidth
{
    Narrow12_5kHz,
    Wide25kHz,
}

public enum ScanListMode
{
    None,
    Scan,
    Priority,
}
