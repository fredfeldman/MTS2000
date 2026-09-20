using System.Diagnostics;
using System.IO.Ports;

namespace MTS2000.Core.Protocol;

/// <summary>
/// Coordinates a programming session with an MTS2000-family radio over a RIB (Radio
/// Interface Box) cable: switching the radio into programming mode, querying its firmware
/// version, and reading/writing EEPROM memory once the extended protocol is active.
/// The RIB shorts the TX and RX lines together, so every byte this class writes is read back
/// as its own echo before any genuine reply from the radio arrives.
/// </summary>
public sealed class RadioProgrammingSession : IDisposable
{
    private const int MaxRetries = 4;

    private readonly SerialPort _port;
    private bool _extendedModeActive;

    public event EventHandler<string>? StatusChanged;

    public RadioProgrammingSession(string comPort)
    {
        _port = new SerialPort(comPort) { BaudRate = 9600, ReadTimeout = 2500 };
        _port.Open();
        Report($"Opened {comPort}.");
    }

    public void EnterProgrammingMode()
    {
        Transact(ControlBusCommands.EnterProgrammingMode, awaitReply: false);
        Thread.Sleep(1000);
    }

    public decimal QueryFirmwareVersion()
    {
        DeactivateExtendedModeIfNeeded();

        var reply = Transact(ControlBusCommands.FirmwareVersionQuery, awaitReply: true)
            ?? throw new InvalidOperationException("Radio did not answer the firmware version query.");
        Thread.Sleep(400); // The radio echoes this reply a few times; let the extras drain.

        var major = (reply.DataA >> 4) * 10 + (reply.DataA & 0x0f);
        var minor = (reply.DataB >> 4) * 10 + (reply.DataB & 0x0f);
        return major + minor * 0.01M;
    }

    public void ResetRadio() => Transact(ControlBusCommands.ResetRadio, awaitReply: false);

    public byte[] ReadEeprom(int address, int count)
    {
        ValidateRange(address, count);
        ActivateExtendedModeIfNeeded();

        var (msb, lsb) = SplitAddress(address);
        var reply = ExchangeExtendedFrame(new ExtendedProtocolFrame(0x11, (byte)count, 0x00, msb, lsb));

        if (reply.Payload[0] != 0x00 || reply.Payload[1] != msb || reply.Payload[2] != lsb)
        {
            throw new InvalidOperationException("EEPROM read reply echoed a different address than requested.");
        }

        if (reply.Payload.Length - 3 != count)
        {
            throw new InvalidOperationException("EEPROM read reply carried a different byte count than requested.");
        }

        return reply.Payload[3..];
    }

    public void WriteEeprom(int address, byte[] data)
    {
        ValidateRange(address, data.Length);
        ActivateExtendedModeIfNeeded();

        var (msb, lsb) = SplitAddress(address);
        var payload = new byte[data.Length + 3];
        payload[1] = msb;
        payload[2] = lsb;
        data.CopyTo(payload, 3);

        var reply = ExchangeExtendedFrame(new ExtendedProtocolFrame(0x17, payload));
        if (reply.Payload[0] != 0x00 || reply.Payload[1] != msb || reply.Payload[2] != lsb)
        {
            throw new InvalidOperationException("EEPROM write reply echoed a different address than requested.");
        }
    }

    private static void ValidateRange(int address, int count)
    {
        if (address is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Address must be within 0x0000-0xFFFF.");
        }

        if (count is < 1 or > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Byte count must be within 1-255.");
        }
    }

    private static (byte Msb, byte Lsb) SplitAddress(int address) => ((byte)(address / 0x100), (byte)(address % 0x100));

    private void ActivateExtendedModeIfNeeded()
    {
        if (_extendedModeActive)
        {
            return;
        }

        Transact(ControlBusCommands.EnterExtendedMode, awaitReply: false);
        _port.DtrEnable = true;
        _port.RtsEnable = true;
        Thread.Sleep(250);
        _port.DiscardInBuffer();
        _extendedModeActive = true;
        Report("Extended protocol active.");
    }

    private void DeactivateExtendedModeIfNeeded()
    {
        if (!_extendedModeActive)
        {
            return;
        }

        ExchangeExtendedFrame(new ExtendedProtocolFrame(0x10, awaitsAcknowledgement: false), requireAck: false);
        _port.DtrEnable = false;
        _port.RtsEnable = false;
        Thread.Sleep(1500);
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        _extendedModeActive = false;
        Report("Extended protocol deactivated.");
    }

    private ControlBusFrame? Transact(ControlBusFrame outgoing, bool awaitReply)
    {
        for (var attempt = 0; ; attempt++)
        {
            DeactivateExtendedModeIfNeeded();

            _port.DtrEnable = true;
            _port.RtsEnable = true;
            Thread.Sleep(30);
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            var wire = outgoing.ToWireBytes();
            _port.Write(wire, 0, wire.Length);

            if (ReadControlBusFrame() is null)
            {
                FailOrRetry(attempt, "No echo from the interface cable. Check the cable/RIB and COM port.");
                continue;
            }

            if (!awaitReply || !outgoing.ExpectsReply)
            {
                _port.DtrEnable = false;
                _port.RtsEnable = false;
                return null;
            }

            var reply = ReadControlBusFrame();
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            Thread.Sleep(30);
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            if (reply is null)
            {
                FailOrRetry(attempt, "The radio did not respond. Confirm it is powered on and connected.");
                continue;
            }

            return reply;
        }
    }

    private ExtendedProtocolFrame ExchangeExtendedFrame(ExtendedProtocolFrame outgoing, bool requireAck = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.Write(outgoing.Encoded, 0, outgoing.Encoded.Length);

            if (!WaitForEcho(outgoing.Encoded.Length))
            {
                FailOrRetry(attempt, "RIB or interface cable does not appear to be connected.");
                continue;
            }

            if (!outgoing.AwaitsAcknowledgement)
            {
                return outgoing;
            }

            var reply = ReadExtendedFrame();
            if (reply is not null && !reply.IsIncomplete && !reply.IsInvalid)
            {
                return reply;
            }

            if (!requireAck && WaitForSingleAcknowledgementByte())
            {
                return outgoing;
            }

            FailOrRetry(attempt, "The radio failed to acknowledge the command. Try power-cycling it.");
        }
    }

    private bool WaitForEcho(int expectedByteCount)
    {
        var echo = new byte[expectedByteCount];
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 1000)
        {
            if (_port.BytesToRead < echo.Length)
            {
                Thread.Sleep(10);
                continue;
            }

            _port.Read(echo, 0, echo.Length);
            return echo.Any(b => b != 0x00);
        }

        return false;
    }

    private bool WaitForSingleAcknowledgementByte()
    {
        const byte AckByte = 0x50;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 1000)
        {
            if (_port.BytesToRead < 1)
            {
                Thread.Sleep(10);
                continue;
            }

            return _port.ReadByte() == AckByte;
        }

        return false;
    }

    private void FailOrRetry(int attempt, string message)
    {
        if (attempt >= MaxRetries)
        {
            throw new InvalidOperationException(message);
        }

        Thread.Sleep(500);
    }

    private ControlBusFrame? ReadControlBusFrame()
    {
        var buffer = new byte[5];
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 100)
        {
            if (_port.BytesToRead < buffer.Length)
            {
                Thread.Sleep(10);
                continue;
            }

            _port.Read(buffer, 0, buffer.Length);
            return ControlBusFrame.TryParse(buffer);
        }

        return null;
    }

    private readonly byte[] _receiveScratch = new byte[1024];

    private ExtendedProtocolFrame? ReadExtendedFrame()
    {
        var received = 0;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 1000 && received < _receiveScratch.Length)
        {
            var available = _port.BytesToRead;
            if (available == 0)
            {
                Thread.Sleep(20);
                continue;
            }

            received += _port.Read(_receiveScratch, received, available);
            var frame = ExtendedProtocolFrame.Decode(_receiveScratch, received);
            if (!frame.IsIncomplete)
            {
                return frame;
            }

            stopwatch.Restart();
        }

        Report("Timed out waiting for a complete reply.");
        return null;
    }

    private void Report(string status) => StatusChanged?.Invoke(this, status);

    public void Dispose()
    {
        try
        {
            DeactivateExtendedModeIfNeeded();
        }
        catch
        {
            // Best-effort on the way out.
        }

        _port.Dispose();
    }
}
