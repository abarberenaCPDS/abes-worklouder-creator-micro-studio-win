using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Views;

public partial class ControlsPage : ContentView
{
    private readonly ControlsViewModel _viewModel;

    public ControlsPage(IBridgeService bridge)
    {
        InitializeComponent();
        _viewModel = new ControlsViewModel(bridge);
        BindingContext = _viewModel;
        _viewModel.Attach();
    }
}
