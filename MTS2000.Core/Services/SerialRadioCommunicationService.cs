using System.IO.Ports;
using MTS2000.Core.Models;
using MTS2000.Core.Protocol;

namespace MTS2000.Core.Services;

/// <inheritdoc cref="IRadioCommunicationService"/>
public class SerialRadioCommunicationService : IRadioCommunicationService, IDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private RadioProgrammingSession? _session;

    public RadioConnectionState State { get; private set; } = RadioConnectionState.Disconnected;

    public event EventHandler<string>? StatusChanged;

    public IReadOnlyList<string> GetAvailablePortNames() => SerialPort.GetPortNames();

    public void Connect(string portName, int baudRate = 9600)
    {
        _operationGate.Wait();
        try
        {
            DetachSession();
            State = RadioConnectionState.Disconnected;

            var session = new RadioProgrammingSession(portName, baudRate);
            session.StatusChanged += OnSessionStatusChanged;
            _session = session;
            State = RadioConnectionState.HandshakePending;
        }
        catch
        {
            State = RadioConnectionState.Disconnected;
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Disconnect()
    {
        _operationGate.Wait();
        try
        {
            DetachSession();
            State = RadioConnectionState.Disconnected;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.StatusChanged -= OnSessionStatusChanged;
        _session.Dispose();
        _session = null;
    }

    private void OnSessionStatusChanged(object? sender, string status) => StatusChanged?.Invoke(this, status);

    /// <summary>Reads back the radio's firmware version, proving the control-bus link is alive.
    /// Puts the radio in programming mode first if that hasn't happened yet this session.</summary>
    public async Task<decimal> GetFirmwareVersionAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var session = EnsureSession();
            var version = await Task.Run(() => session.QueryFirmwareVersion(cancellationToken), cancellationToken);
            State = RadioConnectionState.Connected;
            return version;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<byte[]> ReadMemoryAsync(int address, int length, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var session = EnsureConnected();
            return await Task.Run(() => session.ReadEeprom(address, length, cancellationToken), cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task WriteMemoryAsync(int address, byte[] data, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var session = EnsureConnected();
            await Task.Run(() => session.WriteEeprom(address, data, cancellationToken), cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<Codeplug> ReadCodeplugAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // The control-bus/extended-protocol transport above is real and working (see
        // RadioProgrammingSession), but decoding the ~33KB EEPROM blob into channels/zones
        // requires the MTS2000's full codeplug block map (internal/external block hierarchy,
        // vector refs, per-block checksums, and the Toolproof auth code), not implemented here.
        throw new NotSupportedException(
            "Reading a codeplug from the radio requires the MTS2000 codeplug block format, which is not yet implemented. " +
            "Low-level memory access is available via ReadMemoryAsync.");
    }

    public Task WriteCodeplugAsync(Codeplug codeplug, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        throw new NotSupportedException(
            "Writing a codeplug to the radio requires the MTS2000 codeplug block format, which is not yet implemented. " +
            "Low-level memory access is available via WriteMemoryAsync.");
    }

    private RadioProgrammingSession EnsureConnected()
    {
        if (State != RadioConnectionState.Connected || _session is null)
        {
            throw new InvalidOperationException("Not connected to a radio. Call Connect first.");
        }

        return _session;
    }

    private RadioProgrammingSession EnsureSession()
    {
        if (State is not RadioConnectionState.Connected and not RadioConnectionState.HandshakePending || _session is null)
        {
            throw new InvalidOperationException("Not connected to a radio. Call Connect first.");
        }

        return _session;
    }

    public void Dispose()
    {
        Disconnect();
        _operationGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
