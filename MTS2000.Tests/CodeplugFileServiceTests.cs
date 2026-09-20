using MTS2000.Core.Models;
using MTS2000.Core.Services;

namespace MTS2000.Tests;

public class CodeplugFileServiceTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"mts2000-test-{Guid.NewGuid()}.json");

    [Fact]
    public void Save_ThenLoad_RoundTripsZonesAndChannels()
    {
        var codeplug = Codeplug.CreateDefault();
        codeplug.Settings.RadioAlias = "Test Radio";
        codeplug.Zones[0].Channels[0].Name = "Repeater 1";
        codeplug.Zones[0].Channels[0].ReceiveFrequencyMhz = 146.520;
        codeplug.Zones[0].Channels[0].Bandwidth = ChannelBandwidth.Narrow12_5kHz;

        var service = new CodeplugFileService();
        service.Save(codeplug, _tempFile);
        var loaded = service.Load(_tempFile);

        Assert.Equal("Test Radio", loaded.Settings.RadioAlias);
        Assert.Single(loaded.Zones);
        Assert.Single(loaded.Zones[0].Channels);
        Assert.Equal("Repeater 1", loaded.Zones[0].Channels[0].Name);
        Assert.Equal(146.520, loaded.Zones[0].Channels[0].ReceiveFrequencyMhz);
        Assert.Equal(ChannelBandwidth.Narrow12_5kHz, loaded.Zones[0].Channels[0].Bandwidth);
    }

    [Fact]
    public void Load_WithMissingFile_Throws()
    {
        var service = new CodeplugFileService();
        Assert.Throws<FileNotFoundException>(() => service.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.json")));
    }

    public void Dispose()
    {
        File.Delete(_tempFile);
        GC.SuppressFinalize(this);
    }
}
