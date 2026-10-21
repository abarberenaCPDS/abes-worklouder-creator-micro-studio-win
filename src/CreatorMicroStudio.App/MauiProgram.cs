using Microsoft.Maui.Controls.Hosting;
using H.NotifyIcon;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;
using AbesWorkLouderCreatorMicroStudio.Windows.Views;

namespace AbesWorkLouderCreatorMicroStudio.Windows;

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
