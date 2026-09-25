using MTS2000.Core.Models;
using MTS2000.Core.Services;

namespace MTS2000.Tests;

/// <summary>
/// A minimal <see cref="IRadioCommunicationService"/> whose <see cref="GetFirmwareVersionAsync"/>
/// blocks until <see cref="Release"/> is called, so tests can hold an "operation in flight" open
/// long enough to try (and expect to be blocked from) starting a second one.
/// </summary>
public sealed class GatedRadioCommunicationService : IRadioCommunicationService
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int GetFirmwareVersionCallCount { get; private set; }

    public bool FailFirmwareVersion { get; set; }

    public bool FailConnect { get; set; }

    public RadioConnectionState State { get; private set; } = RadioConnectionState.Disconnected;

#pragma warning disable CS0067 // required by IRadioCommunicationService, unused in this fake
    public event EventHandler<string>? StatusChanged;
#pragma warning restore CS0067

    public IReadOnlyList<string> GetAvailablePortNames() => ["FAKE"];

    public void Connect(string portName, int baudRate = 9600)
    {
        if (FailConnect)
        {
            throw new InvalidOperationException("simulated connection failure");
        }

        State = RadioConnectionState.Connected;
    }

    public void Disconnect() => State = RadioConnectionState.Disconnected;

    public async Task<decimal> GetFirmwareVersionAsync(CancellationToken cancellationToken = default)
    {
        GetFirmwareVersionCallCount++;
        if (FailFirmwareVersion)
        {
            throw new InvalidOperationException("simulated radio failure");
        }

        await _gate.Task.WaitAsync(cancellationToken);
        return 1.00M;
    }

    public Task<byte[]> ReadMemoryAsync(int address, int length, CancellationToken cancellationToken = default) =>
        Task.FromResult(new byte[length]);

    public Task WriteMemoryAsync(int address, byte[] data, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<Codeplug> ReadCodeplugAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task WriteCodeplugAsync(Codeplug codeplug, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Lets the in-flight <see cref="GetFirmwareVersionAsync"/> call complete.</summary>
    public void Release() => _gate.SetResult();
}
