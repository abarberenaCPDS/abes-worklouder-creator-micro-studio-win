using Microsoft.Maui.Controls.Hosting;
using MicroManager.App.Services;
using MicroManager.App.ViewModels;
using MicroManager.App.Views;

namespace MicroManager.App;

public partial class App : Application
{
    public App(IBridgeService bridgeService, TrayIconViewModel viewModel)
    {
        InitializeComponent();

        Resources = new ResourceDictionary
        {
            { "PrimaryColor", Color.FromArgb("#007ACC") },
            { "SecondaryColor", Color.FromArgb("#6C757D") },
            { "BlockedColor", Color.FromArgb("#FF2D2D") },
            { "WorkingColor", Color.FromArgb("#FFA000") },
            { "DoneColor", Color.FromArgb("#00B0FF") },
            { "IdleColor", Color.FromArgb("#00C853") },
            { "UnknownColor", Color.FromArgb("#00C853") },
        };

        MainPage = new MainPage
        {
            BindingContext = viewModel
        };

        _bridgeService = bridgeService;
        _viewModel = viewModel;
    }

    private readonly IBridgeService _bridgeService;
    private readonly TrayIconViewModel _viewModel;

    protected override async void OnStart()
    {
        base.OnStart();

        await _bridgeService.InitializeAsync();

        _bridgeService.OnAgentStateChanged += agents =>
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                _viewModel.UpdateAgents(agents);
            });
        };

        _bridgeService.OnDeviceConnected += () =>
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                _viewModel.IsConnected = true;
            });
        };

        _bridgeService.OnDeviceDisconnected += () =>
        {
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                _viewModel.IsConnected = false;
            });
        };
    }

    protected override void OnSleep()
    {
        base.OnSleep();
    }

    protected override void OnResume()
    {
        base.OnResume();
    }
}
