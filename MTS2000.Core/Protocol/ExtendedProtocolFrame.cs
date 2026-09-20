namespace MTS2000.Core.Protocol;

/// <summary>
/// A variable-length frame in the extended memory-access protocol layered on top of the
/// control bus once the radio is in programming mode. Used for EEPROM reads (command 0x11),
/// writes (command 0x17), and to drop back out of extended mode (command 0x10).
/// Header packing: the length nibble/byte and command are packed into 1-4 leading bytes
/// depending on how large the command and payload length are, followed by the payload and a
/// trailing checksum byte such that the sum of every transmitted byte is 0xFF (mod 256).
/// </summary>
public sealed class ExtendedProtocolFrame
{
    private const int ShortFieldLimit = 0x0e;

    public byte Command { get; }
    public byte[] Payload { get; } = [];
    public byte[] Encoded { get; } = [];
    public bool AwaitsAcknowledgement { get; }
    public bool IsIncomplete { get; private init; }
    public bool IsInvalid { get; private init; }

    public ExtendedProtocolFrame(byte command, params byte[] payload) : this(command, awaitsAcknowledgement: true, payload)
    {
    }

    public ExtendedProtocolFrame(byte command, bool awaitsAcknowledgement, params byte[] payload)
    {
        Command = command;
        Payload = payload;
        AwaitsAcknowledgement = awaitsAcknowledgement;
        Encoded = Encode(command, payload);
    }

    private ExtendedProtocolFrame(byte command, byte[] payload, byte[] encoded, bool incomplete, bool invalid)
    {
        Command = command;
        Payload = payload;
        Encoded = encoded;
        IsIncomplete = incomplete;
        IsInvalid = invalid;
    }

    private static byte[] Encode(byte command, byte[] payload)
    {
        // The wire "length" counts the payload plus the trailing checksum byte.
        var wireLength = payload.Length + 1;

        // Each nibble saturates at 0x0f as a sentinel meaning "see the following byte(s) instead".
        var header = new List<byte>(4)
        {
            (byte)(Math.Min(0x0f, (int)command) * 16 + Math.Min(0x0f, wireLength)),
        };

        if (command > ShortFieldLimit)
        {
            header.Add(command);
        }

        if (wireLength > ShortFieldLimit)
        {
            header.Add((byte)(wireLength / 256));
            header.Add((byte)(wireLength % 256));
        }

        var frame = new byte[header.Count + payload.Length + 1];
        header.CopyTo(frame);
        payload.CopyTo(frame, header.Count);
        frame[^1] = ChecksumFor(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    /// <summary>Attempts to decode a frame from the first <paramref name="receivedLength"/> bytes of <paramref name="buffer"/>.</summary>
    public static ExtendedProtocolFrame Decode(byte[] buffer, int receivedLength)
    {
        try
        {
            return DecodeCore(buffer, receivedLength);
        }
        catch (Exception)
        {
            return new ExtendedProtocolFrame(0, [], [], incomplete: false, invalid: true);
        }
    }

    private static ExtendedProtocolFrame DecodeCore(byte[] buffer, int receivedLength)
    {
        var high = buffer[0] >> 4;
        var low = buffer[0] & 0x0f;

        byte command;
        int wireLength;
        int headerSize;

        if (high < 0x0f && low < 0x0f)
        {
            (command, wireLength, headerSize) = ((byte)high, low, 1);
        }
        else if (high == 0x0f && low < 0x0f)
        {
            (command, wireLength, headerSize) = (buffer[1], low, 2);
        }
        else if (high < 0x0f && low == 0x0f)
        {
            (command, wireLength, headerSize) = ((byte)high, buffer[1], 3);
        }
        else
        {
            (command, wireLength, headerSize) = (buffer[1], buffer[2] * 256 + buffer[3], 4);
        }

        var totalFrameSize = headerSize + wireLength;
        if (receivedLength < totalFrameSize)
        {
            return new ExtendedProtocolFrame(command, [], [], incomplete: true, invalid: false);
        }

        if (receivedLength > totalFrameSize)
        {
            return new ExtendedProtocolFrame(command, [], [], incomplete: false, invalid: true);
        }

        var isValid = ChecksumFor(buffer.AsSpan(0, totalFrameSize - 1)) == buffer[totalFrameSize - 1];

        var payload = new byte[wireLength - 1];
        Array.Copy(buffer, headerSize, payload, 0, payload.Length);

        var encoded = new byte[totalFrameSize];
        Array.Copy(buffer, encoded, totalFrameSize);

        return new ExtendedProtocolFrame(command, payload, encoded, incomplete: false, invalid: !isValid);
    }

    private static byte ChecksumFor(ReadOnlySpan<byte> bytesBeforeChecksum)
    {
        var sum = 0;
        foreach (var b in bytesBeforeChecksum)
        {
            sum += b;
        }

        return (byte)(0xff - (sum & 0xff));
    }
}
