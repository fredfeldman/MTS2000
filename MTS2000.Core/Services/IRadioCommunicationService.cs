namespace MTS2000.Core.Services;

/// <summary>Connection state of the serial link to a radio.</summary>
public enum RadioConnectionState
{
    Disconnected,
    Connected,
}

/// <summary>
/// Serial communication with an MTS2000 radio. Motorola's ASTRO/RSS programming protocol is
/// proprietary and undocumented publicly, so <see cref="ReadCodeplugAsync"/> and
/// <see cref="WriteCodeplugAsync"/> are stubbed out. Replace their bodies with the real
/// request/response byte sequences once a protocol reference is available.
/// </summary>
public interface IRadioCommunicationService
{
    RadioConnectionState State { get; }

    /// <summary>Raised with granular protocol-level status text (retries, timeouts, mode transitions).</summary>
    event EventHandler<string>? StatusChanged;

    IReadOnlyList<string> GetAvailablePortNames();

    void Connect(string portName, int baudRate = 9600);

    void Disconnect();

    Task<decimal> GetFirmwareVersionAsync(CancellationToken cancellationToken = default);

    Task<byte[]> ReadMemoryAsync(int address, int length, CancellationToken cancellationToken = default);

    Task WriteMemoryAsync(int address, byte[] data, CancellationToken cancellationToken = default);

    Task<Models.Codeplug> ReadCodeplugAsync(CancellationToken cancellationToken = default);

    Task WriteCodeplugAsync(Models.Codeplug codeplug, CancellationToken cancellationToken = default);
}
