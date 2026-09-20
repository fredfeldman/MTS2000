using MTS2000.Core.Protocol;

namespace MTS2000.Tests;

public class ExtendedProtocolFrameTests
{
    [Fact]
    public void Encode_ThenDecode_RoundTripsShortPayload()
    {
        var original = new ExtendedProtocolFrame(0x11, 0x08, 0x00, 0x12, 0x34);

        var decoded = ExtendedProtocolFrame.Decode(original.Encoded, original.Encoded.Length);

        Assert.False(decoded.IsIncomplete);
        Assert.False(decoded.IsInvalid);
        Assert.Equal(original.Command, decoded.Command);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact]
    public void Encode_ThenDecode_RoundTripsPayloadNeedingLongForm()
    {
        // A payload of 15+ bytes forces the length field out of the single nibble into its own byte(s).
        var payload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        var original = new ExtendedProtocolFrame(0x17, payload);

        var decoded = ExtendedProtocolFrame.Decode(original.Encoded, original.Encoded.Length);

        Assert.False(decoded.IsIncomplete);
        Assert.False(decoded.IsInvalid);
        Assert.Equal(payload, decoded.Payload);
    }

    [Fact]
    public void Decode_WithFewerBytesThanExpected_IsIncomplete()
    {
        var original = new ExtendedProtocolFrame(0x11, 0x08, 0x00, 0x12, 0x34);

        var decoded = ExtendedProtocolFrame.Decode(original.Encoded, original.Encoded.Length - 1);

        Assert.True(decoded.IsIncomplete);
    }

    [Fact]
    public void Decode_WithCorruptedChecksum_IsInvalid()
    {
        var original = new ExtendedProtocolFrame(0x11, 0x08, 0x00, 0x12, 0x34);
        var corrupted = (byte[])original.Encoded.Clone();
        corrupted[^1] ^= 0xFF;

        var decoded = ExtendedProtocolFrame.Decode(corrupted, corrupted.Length);

        Assert.True(decoded.IsInvalid);
    }
}
