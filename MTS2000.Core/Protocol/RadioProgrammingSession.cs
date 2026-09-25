using System.Diagnostics;

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

    private readonly ISerialTransport _port;
    private bool _extendedModeActive;
    private bool _programmingModeEntered;

    public event EventHandler<string>? StatusChanged;

    public RadioProgrammingSession(string comPort) : this(comPort, 9600)
    {
    }

    public RadioProgrammingSession(string comPort, int baudRate) : this(new SerialPortTransport(comPort, baudRate))
    {
    }

    /// <summary>Constructs a session over an arbitrary transport, e.g. a fake radio in tests.</summary>
    public RadioProgrammingSession(ISerialTransport transport)
    {
        _port = transport;
        Report("Transport ready.");
    }

    public void EnterProgrammingMode(CancellationToken cancellationToken = default)
    {
        Transact(ControlBusCommands.EnterProgrammingMode, awaitReply: false, cancellationToken);
        Delay(1000, cancellationToken);
        _programmingModeEntered = true;
    }

    private void EnsureProgrammingMode(CancellationToken cancellationToken)
    {
        if (!_programmingModeEntered)
        {
            EnterProgrammingMode(cancellationToken);
        }
    }

    public decimal QueryFirmwareVersion(CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureProgrammingMode(cancellationToken);
            DeactivateExtendedModeIfNeeded(cancellationToken);

            var reply = Transact(ControlBusCommands.FirmwareVersionQuery, awaitReply: true, cancellationToken)
                ?? throw new InvalidOperationException("Radio did not answer the firmware version query.");
            Delay(400, cancellationToken); // The radio echoes this reply a few times; let the extras drain.

            var major = (reply.DataA >> 4) * 10 + (reply.DataA & 0x0f);
            var minor = (reply.DataB >> 4) * 10 + (reply.DataB & 0x0f);
            return major + minor * 0.01M;
        }
        catch (OperationCanceledException)
        {
            CleanupAfterCancellation();
            throw;
        }
    }

    public void ResetRadio(CancellationToken cancellationToken = default) =>
        Transact(ControlBusCommands.ResetRadio, awaitReply: false, cancellationToken);

    public byte[] ReadEeprom(int address, int count, CancellationToken cancellationToken = default)
    {
        ValidateRange(address, count);
        try
        {
            ActivateExtendedModeIfNeeded(cancellationToken);

            var (msb, lsb) = SplitAddress(address);
            var reply = ExchangeExtendedFrame(new ExtendedProtocolFrame(0x11, (byte)count, 0x00, msb, lsb), cancellationToken: cancellationToken);

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
        catch (OperationCanceledException)
        {
            CleanupAfterCancellation();
            throw;
        }
    }

    public void WriteEeprom(int address, byte[] data, CancellationToken cancellationToken = default)
    {
        ValidateRange(address, data.Length);
        try
        {
            ActivateExtendedModeIfNeeded(cancellationToken);

            var (msb, lsb) = SplitAddress(address);
            var payload = new byte[data.Length + 3];
            payload[1] = msb;
            payload[2] = lsb;
            data.CopyTo(payload, 3);

            var reply = ExchangeExtendedFrame(
                new ExtendedProtocolFrame(0x17, payload),
                allowRetries: false,
                cancellationToken: cancellationToken);
            if (reply.Payload[0] != 0x00 || reply.Payload[1] != msb || reply.Payload[2] != lsb)
            {
                throw new InvalidOperationException("EEPROM write reply echoed a different address than requested.");
            }
        }
        catch (OperationCanceledException)
        {
            CleanupAfterCancellation();
            throw;
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

        if ((long)address + count > 0x10000)
        {
            throw new ArgumentException("The requested range must not run past address 0xFFFF.", nameof(count));
        }
    }

    private static (byte Msb, byte Lsb) SplitAddress(int address) => ((byte)(address / 0x100), (byte)(address % 0x100));

    private void ActivateExtendedModeIfNeeded(CancellationToken cancellationToken)
    {
        EnsureProgrammingMode(cancellationToken);

        if (_extendedModeActive)
        {
            return;
        }

        Transact(ControlBusCommands.EnterExtendedMode, awaitReply: false, cancellationToken);
        _port.DtrEnable = true;
        _port.RtsEnable = true;
        _extendedModeActive = true;
        Delay(250, cancellationToken);
        _port.DiscardInBuffer();
        Report("Extended protocol active.");
    }

    private void DeactivateExtendedModeIfNeeded(CancellationToken cancellationToken)
    {
        if (!_extendedModeActive)
        {
            return;
        }

        ExchangeExtendedFrame(
            new ExtendedProtocolFrame(0x10, awaitsAcknowledgement: false),
            requireAck: false,
            cancellationToken: cancellationToken);
        _port.DtrEnable = false;
        _port.RtsEnable = false;
        Delay(1500, cancellationToken);
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        _extendedModeActive = false;
        Report("Extended protocol deactivated.");
    }

    private ControlBusFrame? Transact(ControlBusFrame outgoing, bool awaitReply, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeactivateExtendedModeIfNeeded(cancellationToken);

            _port.DtrEnable = true;
            _port.RtsEnable = true;
            Delay(30, cancellationToken);
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            var wire = outgoing.ToWireBytes();
            _port.Write(wire, 0, wire.Length);

            if (ReadControlBusFrame(cancellationToken, wire) is null)
            {
                FailOrRetry(attempt, "No echo from the interface cable. Check the cable/RIB and COM port.", cancellationToken);
                continue;
            }

            if (!awaitReply || !outgoing.ExpectsReply)
            {
                _port.DtrEnable = false;
                _port.RtsEnable = false;
                return null;
            }

            var reply = ReadControlBusFrame(cancellationToken);
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            Delay(30, cancellationToken);
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            if (reply is null)
            {
                FailOrRetry(attempt, "The radio did not respond. Confirm it is powered on and connected.", cancellationToken);
                continue;
            }

            return reply;
        }
    }

    private ExtendedProtocolFrame ExchangeExtendedFrame(
        ExtendedProtocolFrame outgoing,
        bool requireAck = true,
        bool allowRetries = true,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.Write(outgoing.Encoded, 0, outgoing.Encoded.Length);

            if (!WaitForEcho(outgoing.Encoded, cancellationToken))
            {
                if (!allowRetries)
                {
                    throw new InvalidOperationException("The write was not echoed by the interface cable; it was not retried.");
                }

                FailOrRetry(attempt, "RIB or interface cable does not appear to be connected.", cancellationToken);
                continue;
            }

            if (!outgoing.AwaitsAcknowledgement)
            {
                return outgoing;
            }

            var reply = ReadExtendedFrame(cancellationToken);
            if (reply is not null && !reply.IsIncomplete && !reply.IsInvalid)
            {
                return reply;
            }

            if (!allowRetries)
            {
                throw new InvalidOperationException("The radio write acknowledgement was ambiguous; the write was not retried.");
            }

            if (!requireAck && WaitForSingleAcknowledgementByte(cancellationToken))
            {
                return outgoing;
            }

            FailOrRetry(attempt, "The radio failed to acknowledge the command. Try power-cycling it.", cancellationToken);
        }
    }

    private bool WaitForEcho(byte[] expectedEcho, CancellationToken cancellationToken)
    {
        var echo = new byte[expectedEcho.Length];
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 1000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_port.BytesToRead < echo.Length)
            {
                Delay(10, cancellationToken);
                continue;
            }

            _port.Read(echo, 0, echo.Length);
            return echo.SequenceEqual(expectedEcho);
        }

        return false;
    }

    private bool WaitForSingleAcknowledgementByte(CancellationToken cancellationToken)
    {
        const byte AckByte = 0x50;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 1000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_port.BytesToRead < 1)
            {
                Delay(10, cancellationToken);
                continue;
            }

            return _port.ReadByte() == AckByte;
        }

        return false;
    }

    private void FailOrRetry(int attempt, string message, CancellationToken cancellationToken)
    {
        if (attempt >= MaxRetries)
        {
            throw new InvalidOperationException(message);
        }

        Delay(500, cancellationToken);
    }

    private ControlBusFrame? ReadControlBusFrame(CancellationToken cancellationToken, byte[]? expectedWire = null)
    {
        var buffer = new byte[5];
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 100)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_port.BytesToRead < buffer.Length)
            {
                Delay(10, cancellationToken);
                continue;
            }

            _port.Read(buffer, 0, buffer.Length);
            if (expectedWire is not null && !buffer.SequenceEqual(expectedWire))
            {
                return null;
            }

            return ControlBusFrame.TryParse(buffer);
        }

        return null;
    }

    private readonly byte[] _receiveScratch = new byte[1024];

    private ExtendedProtocolFrame? ReadExtendedFrame(CancellationToken cancellationToken)
    {
        var received = 0;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 1000 && received < _receiveScratch.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var available = _port.BytesToRead;
            if (available == 0)
            {
                Delay(20, cancellationToken);
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

    private void CleanupAfterCancellation()
    {
        if (!_extendedModeActive)
        {
            return;
        }

        try
        {
            DeactivateExtendedModeIfNeeded(CancellationToken.None);
        }
        catch
        {
            // Preserve the cancellation result; disposal remains the final fallback.
        }
    }

    private static void Delay(int milliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne(milliseconds);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        try
        {
            DeactivateExtendedModeIfNeeded(CancellationToken.None);
        }
        catch
        {
            // Best-effort on the way out.
        }

        _port.Dispose();
    }
}
