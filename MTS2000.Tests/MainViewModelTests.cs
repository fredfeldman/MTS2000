using MTS2000.App.ViewModels;

namespace MTS2000.Tests;

/// <summary>Regression tests for the <c>IsBusy</c>/<c>CanOperateRadio</c> guard against overlapping radio commands.</summary>
public class MainViewModelTests
{
    [Fact]
    public async Task GetFirmwareVersionCommand_SecondInvocationWhileBusy_IsBlocked()
    {
        var radioService = new GatedRadioCommunicationService();
        radioService.Connect("FAKE");
        var viewModel = new MainViewModel(radioService) { IsConnected = true };

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
}
