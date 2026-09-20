using System.Collections.ObjectModel;

namespace MTS2000.Core.Models;

/// <summary>The full set of programmable data for one radio, analogous to a Motorola "codeplug".</summary>
public class Codeplug
{
    public RadioSettings Settings { get; set; } = new();

    public ObservableCollection<Zone> Zones { get; } = new();

    public static Codeplug CreateDefault()
    {
        var codeplug = new Codeplug();
        var zone = new Zone { Name = "Zone 1" };
        zone.Channels.Add(new Channel { Name = "Channel 1" });
        codeplug.Zones.Add(zone);
        return codeplug;
    }
}
