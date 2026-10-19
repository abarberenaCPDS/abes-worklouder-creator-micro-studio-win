using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Views;

public partial class StudioLightingPage : ContentView
{
    private readonly StudioLightingViewModel _viewModel;
    private bool _attached;

    public StudioLightingPage(IBridgeService bridge)
    {
        InitializeComponent();
        _viewModel = new StudioLightingViewModel(bridge);
        BindingContext = _viewModel;
    }

    public void EnterPage()
    {
        if (_attached) return;
        _attached = true;
        _viewModel.Attach();
    }

    public void LeavePage()
    {
        if (!_attached) return;
        _attached = false;
        _viewModel.Detach();
    }
}
