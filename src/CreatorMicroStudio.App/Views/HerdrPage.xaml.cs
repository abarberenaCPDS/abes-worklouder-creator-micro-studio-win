using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Views;

public partial class HerdrPage : ContentView
{
    private readonly HerdrViewModel _viewModel;

    public HerdrPage(IBridgeService bridge)
    {
        InitializeComponent();
        _viewModel = new HerdrViewModel(bridge);
        BindingContext = _viewModel;
        _viewModel.Attach();
    }
}
