using Microsoft.Maui.Controls;
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
        BindingContext = viewModel;
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
