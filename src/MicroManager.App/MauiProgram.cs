using Microsoft.Maui.Controls.Hosting;
using H.NotifyIcon;
using MicroManager.App.Services;
using MicroManager.App.ViewModels;
using MicroManager.App.Views;

namespace MicroManager.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .UseNotifyIcon()
            ;

        builder.Services.AddSingleton<IBridgeService, BridgeService>();
        builder.Services.AddSingleton<TrayIconViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
