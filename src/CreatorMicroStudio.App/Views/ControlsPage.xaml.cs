using AbesWorkLouderCreatorMicroStudio.Windows.Controls;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Views;

public partial class ControlsPage : ContentView
{
    private readonly ControlsViewModel _viewModel;
    private bool _attached;

    public ControlsPage(IBridgeService bridge)
    {
        InitializeComponent();
        _viewModel = new ControlsViewModel(bridge);
        BindingContext = _viewModel;

        _viewModel.Tested += OnTested;
        PadMap.InputTapped += OnPadMapInputTapped;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        Attach();
    }

    private void OnLoaded(object? sender, EventArgs e) => Attach();

    private void OnUnloaded(object? sender, EventArgs e) => Detach();

    private void Attach()
    {
        if (_attached) return;
        _attached = true;
        _viewModel.Attach();
    }

    private void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _viewModel.Detach();
    }

    private void OnPadMapInputTapped(object? sender, PadMapInputEventArgs e) =>
        _viewModel.ToggleFromPad(e.InputId);

    private void OnTested(int inputId) => PadMap.Flash(inputId);
}
