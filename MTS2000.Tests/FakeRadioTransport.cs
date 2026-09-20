using MTS2000.Core.Protocol;

namespace MTS2000.Tests;

/// <summary>
/// An in-memory stand-in for a real MTS2000 wired to a RIB: echoes every written byte (as the
/// shorted TX/RX lines would) and, where the real radio would answer, synthesizes the same
/// reply frames a real unit would send, backed by a simple in-memory "EEPROM" byte array.
/// This lets <see cref="RadioProgrammingSession"/>'s retry/echo/framing logic be exercised
/// end-to-end without any hardware.
/// </summary>
public sealed class FakeRadioTransport : ISerialTransport
{
    public byte[] Eeprom { get; } = new byte[0x10000];

    private readonly Queue<byte> _toSession = new();

    public bool DtrEnable { get; set; }
    public bool RtsEnable { get; set; }
    public int BytesToRead => _toSession.Count;

    public void Write(byte[] buffer, int offset, int count)
    {
        var sent = buffer[offset..(offset + count)];
        foreach (var b in sent)
        {
            _toSession.Enqueue(b); // the RIB shorts TX/RX, so the sender always hears its own bytes
        }

        if (count == 5)
        {
            RespondToControlBusFrame(sent);
        }
        else
        {
            RespondToExtendedFrame(sent);
        }
    }

    private void RespondToControlBusFrame(byte[] sent)
    {
        var frame = ControlBusFrame.TryParse(sent);

        // Whether the radio replies is implicit in the command itself (not carried on the
        // wire): only the firmware-version query gets a follow-up frame back.
        if (frame is null || frame.IsMalformed || frame.Device != 0x08 || frame.Command != 0xC0)
        {
            return;
        }

        // Simulate a firmware version reply of "16.20" encoded as two BCD bytes.
        var reply = new ControlBusFrame(frame.Device, 0x16, 0x20, frame.Command);
        Enqueue(reply.ToWireBytes());
    }

    private void RespondToExtendedFrame(byte[] sent)
    {
        var frame = ExtendedProtocolFrame.Decode(sent, sent.Length);
        if (frame.IsIncomplete || frame.IsInvalid)
        {
            return;
        }

        switch (frame.Command)
        {
            case 0x11:
                RespondToRead(frame.Payload);
                break;
            case 0x17:
                RespondToWrite(frame.Payload);
                break;
            // 0x10 (exit extended mode) expects only the echo, handled above.
        }
    }

    private void RespondToRead(byte[] payload)
    {
        var count = payload[0];
        var address = payload[2] * 0x100 + payload[3];
        var replyPayload = new byte[3 + count];
        replyPayload[1] = payload[2];
        replyPayload[2] = payload[3];
        Array.Copy(Eeprom, address, replyPayload, 3, count);

        Enqueue(new ExtendedProtocolFrame(0x11, replyPayload).Encoded);
    }

    private void RespondToWrite(byte[] payload)
    {
        var address = payload[1] * 0x100 + payload[2];
        var data = payload[3..];
        data.CopyTo(Eeprom, address);

        Enqueue(new ExtendedProtocolFrame(0x17, payload[0], payload[1], payload[2]).Encoded);
    }

    private void Enqueue(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            _toSession.Enqueue(b);
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var actual = Math.Min(count, _toSession.Count);
        for (var i = 0; i < actual; i++)
        {
            buffer[offset + i] = _toSession.Dequeue();
        }

        return actual;
    }

    public int ReadByte() => _toSession.Count > 0 ? _toSession.Dequeue() : -1;

    public void DiscardInBuffer() => _toSession.Clear();

    public void DiscardOutBuffer()
    {
    }

    public void Dispose()
    {
    }
}
