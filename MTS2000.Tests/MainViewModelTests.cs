using System.IO;
using MTS2000.App.Services;
using MTS2000.App.ViewModels;
using MTS2000.Core.Services;

namespace MTS2000.Tests;

/// <summary>Regression tests for the <c>IsBusy</c>/<c>CanOperateRadio</c> guard against overlapping radio commands.</summary>
public class MainViewModelTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"mts2000-vm-log-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetFirmwareVersionCommand_SecondInvocationWhileBusy_IsBlocked()
    {
        var radioService = new GatedRadioCommunicationService();
        radioService.Connect("FAKE");
        var viewModel = new MainViewModel(radioService, new DebugLogService(_logDirectory)) { IsConnected = true };

        // Two "clicks" back-to-back, like a user double-clicking the button, before the first
        // call has a chance to complete.
        var firstCall = viewModel.GetFirmwareVersionCommand.ExecuteAsync(null);
        var secondCall = viewModel.GetFirmwareVersionCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanOperateRadio, "IsBusy should be set as soon as the first call starts.");

        radioService.Release();
        await firstCall;
        await secondCall;

        Assert.Equal(1, radioService.GetFirmwareVersionCallCount);
        Assert.True(viewModel.CanOperateRadio, "IsBusy should clear once the operation finishes.");
    }

    [Fact]
    public async Task CancelOperationCommand_CancelsInFlightRadioOperation()
    {
        var radioService = new GatedRadioCommunicationService();
        radioService.Connect("FAKE");
        var viewModel = new MainViewModel(radioService, new DebugLogService(_logDirectory)) { IsConnected = true };

        var operation = viewModel.GetFirmwareVersionCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanCancelOperation);

        viewModel.CancelOperationCommand.Execute(null);
        await operation;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.CanCancelOperation);
    }

    [Fact]
    public async Task FirmwareFailure_DisconnectsAndClearsConnectionState()
    {
        var radioService = new GatedRadioCommunicationService { FailFirmwareVersion = true };
        radioService.Connect("FAKE");
        var viewModel = new MainViewModel(radioService, new DebugLogService(_logDirectory)) { IsConnected = true };

        await viewModel.GetFirmwareVersionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConnected);
        Assert.Equal(RadioConnectionState.Disconnected, radioService.State);
    }

    [Fact]
    public async Task ConnectFailure_ClearsConnectionState()
    {
        var radioService = new GatedRadioCommunicationService { FailConnect = true };
        var viewModel = new MainViewModel(radioService, new DebugLogService(_logDirectory))
        {
            SelectedPortName = "FAKE",
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConnected);
        Assert.Equal(RadioConnectionState.Disconnected, radioService.State);
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }
}
