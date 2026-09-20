using CommunityToolkit.Mvvm.ComponentModel;

namespace MTS2000.Core.Models;

/// <summary>Radio-wide settings that apply regardless of the selected zone/channel.</summary>
public partial class RadioSettings : ObservableObject
{
    [ObservableProperty]
    private string _radioAlias = "MTS2000";

    [ObservableProperty]
    private string _radioId = "1";

    [ObservableProperty]
    private bool _keypadLockEnabled;

    [ObservableProperty]
    private int _squelchLevel = 5;

    [ObservableProperty]
    private bool _powerOnSelfTestEnabled = true;
}
