using MTS2000.Core.Protocol;

namespace MTS2000.Tests;

/// <summary>End-to-end tests of <see cref="RadioProgrammingSession"/> against an in-memory fake radio (no hardware).</summary>
public class RadioProgrammingSessionTests
{
    [Fact]
    public void QueryFirmwareVersion_ReturnsDecodedBcdVersion()
    {
        using var transport = new FakeRadioTransport();
        using var session = new RadioProgrammingSession(transport);

        var version = session.QueryFirmwareVersion();

        Assert.Equal(16.20M, version);
    }

    [Fact]
    public void ReadEeprom_ReturnsBytesFromFakeRadioMemory()
    {
        using var transport = new FakeRadioTransport();
        var expected = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        expected.CopyTo(transport.Eeprom, 0x1000);

        using var session = new RadioProgrammingSession(transport);

        var actual = session.ReadEeprom(0x1000, expected.Length);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteEeprom_ThenReadEeprom_RoundTrips()
    {
        using var transport = new FakeRadioTransport();
        using var session = new RadioProgrammingSession(transport);
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        session.WriteEeprom(0x2000, data);
        var readBack = session.ReadEeprom(0x2000, data.Length);

        Assert.Equal(data, readBack);
    }

    [Fact]
    public void EnterProgrammingMode_ThenReadEeprom_Succeeds()
    {
        using var transport = new FakeRadioTransport();
        transport.Eeprom[0x0050] = 0x7A;

        using var session = new RadioProgrammingSession(transport);
        session.EnterProgrammingMode();

        var result = session.ReadEeprom(0x0050, 1);

        Assert.Equal(0x7A, result[0]);
    }

    [Fact]
    public void ReadEeprom_WithoutExplicitEnterProgrammingMode_EntersItAutomatically()
    {
        using var transport = new FakeRadioTransport();
        using var session = new RadioProgrammingSession(transport);

        session.ReadEeprom(0x0000, 1);

        Assert.True(transport.SawEnterProgrammingMode);
    }

    [Fact]
    public void WriteEeprom_WithoutExplicitEnterProgrammingMode_EntersItAutomatically()
    {
        using var transport = new FakeRadioTransport();
        using var session = new RadioProgrammingSession(transport);

        session.WriteEeprom(0x0000, [0x01]);

        Assert.True(transport.SawEnterProgrammingMode);
    }

    [Fact]
    public void ReadEeprom_RejectsRangeThatCrossesTheAddressSpace()
    {
        using var transport = new FakeRadioTransport();
        using var session = new RadioProgrammingSession(transport);

        Assert.Throws<ArgumentException>(() => session.ReadEeprom(0xFFFF, 2));
    }

    [Fact]
    public void ReadEeprom_HonorsCancellationBeforeTransportActivity()
    {
        using var transport = new FakeRadioTransport();
        using var session = new RadioProgrammingSession(transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => session.ReadEeprom(0x0000, 1, cancellation.Token));
        Assert.False(transport.SawEnterProgrammingMode);
    }
}
