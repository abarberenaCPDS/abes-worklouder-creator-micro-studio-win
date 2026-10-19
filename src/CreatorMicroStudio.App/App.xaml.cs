using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.ApplicationModel;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;
using AbesWorkLouderCreatorMicroStudio.Windows.Views;

namespace AbesWorkLouderCreatorMicroStudio.Windows;

public partial class App : Application
{
    public App(IBridgeService bridgeService, TrayIconViewModel viewModel)
    {
        InitializeComponent();

        // The Studio's palette lives in Resources/Styles/StudioTheme.xaml, merged
        // by App.xaml. Do not replace Resources here: doing so would drop the
        // theme's styles and the merged dictionary with it.
        UserAppTheme = AppTheme.Dark;

        MainPage = new MainPage(bridgeService, viewModel);

        bridgeService.AskUser = async (prompt, defaultText) =>
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return null;
            return await MainThread.InvokeOnMainThreadAsync(() =>
                page.DisplayPromptAsync("Herdr command", prompt, "Run", "Cancel", initialValue: defaultText));
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
