using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MTS2000.Core.Models;

/// <summary>A zone groups a set of channels shown together on the radio's channel knob/selector.</summary>
public partial class Zone : ObservableObject
{
    [ObservableProperty]
    private string _name = "New Zone";

    public ObservableCollection<Channel> Channels { get; set; } = new();
}
