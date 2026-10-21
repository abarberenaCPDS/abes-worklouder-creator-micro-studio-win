using Microsoft.Maui.Controls;
using AbesWorkLouderCreatorMicroStudio.Windows.Controls;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;
using System.ComponentModel;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Views;

public partial class MainPage : ContentPage
{
    private StudioLightingPage? _lightingPage;
    private TrayIconViewModel? _viewModel;

    public MainPage(IBridgeService bridge, TrayIconViewModel viewModel)
    {
        InitializeComponent();
        _lightingPage = new StudioLightingPage(bridge);
        ControlsHost.Content = new ControlsPage(bridge);
        LightingHost.Content = _lightingPage;
        HerdrHost.Content = new HerdrPage(bridge);
        DashboardPadMap.InputTapped += OnDashboardInputTapped;
        BindingContext = viewModel;
    }

    /// <summary>
    /// A tap on the dashboard map runs the input through the bridge, like the
    /// pad's own key, and lights the tapped control so the click has feedback.
    /// </summary>
    private void OnDashboardInputTapped(object? sender, PadMapInputEventArgs e)
    {
        DashboardPadMap.Flash(e.InputId);

        if (BindingContext is TrayIconViewModel viewModel
            && viewModel.RunInputCommand.CanExecute(e.InputId))
        {
            viewModel.RunInputCommand.Execute(e.InputId);
        }
    }

    protected override void OnBindingContextChanged()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        base.OnBindingContextChanged();
        _viewModel = BindingContext as TrayIconViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += ViewModelPropertyChanged;
        SyncPageLifecycle();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayIconViewModel.SelectedPage)) SyncPageLifecycle();
    }

    private void SyncPageLifecycle()
    {
        if (_lightingPage is null) return;
        if (_viewModel?.SelectedPage == "Lighting") _lightingPage.EnterPage();
        else _lightingPage.LeavePage();
    }
}
