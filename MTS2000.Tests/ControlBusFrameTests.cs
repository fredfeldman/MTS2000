using MTS2000.Core.Protocol;

namespace MTS2000.Tests;

public class ControlBusFrameTests
{
    [Fact]
    public void ToWireBytes_ThenTryParse_RoundTripsWithoutMalformedFlag()
    {
        var frame = new ControlBusFrame(0x08, 0x00, 0x00, 0xC0, expectsReply: true);

        var parsed = ControlBusFrame.TryParse(frame.ToWireBytes());

        Assert.NotNull(parsed);
        Assert.False(parsed.IsMalformed);
        Assert.Equal(frame.Device, parsed.Device);
        Assert.Equal(frame.DataA, parsed.DataA);
        Assert.Equal(frame.DataB, parsed.DataB);
        Assert.Equal(frame.Command, parsed.Command);
        Assert.Equal(frame.Checksum, parsed.Checksum);
    }

    [Fact]
    public void TryParse_WithCorruptedChecksum_IsMarkedMalformed()
    {
        var frame = new ControlBusFrame(0x01, 0x02, 0x00, 0x40);
        var bytes = frame.ToWireBytes();
        bytes[4] ^= 0xFF; // corrupt the checksum byte

        var parsed = ControlBusFrame.TryParse(bytes);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsMalformed);
    }

    [Fact]
    public void TryParse_WithWrongLength_ReturnsNull()
    {
        Assert.Null(ControlBusFrame.TryParse(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void KnownCommands_ProduceStableChecksums()
    {
        // Regression guard: these values must stay stable since the radio expects them exactly.
        Assert.Equal(0x06, ControlBusCommands.EnterExtendedMode.ToWireBytes()[3]);
        Assert.Equal(0x40, ControlBusCommands.EnterProgrammingMode.ToWireBytes()[3]);
        Assert.Equal(0x08, ControlBusCommands.ResetRadio.ToWireBytes()[3]);
        Assert.True(ControlBusCommands.FirmwareVersionQuery.ExpectsReply);
    }
}
