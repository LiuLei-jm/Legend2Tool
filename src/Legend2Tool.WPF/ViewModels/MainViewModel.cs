using Legend2Tool.WPF.Services;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class MainViewModel
    {
        public MenuViewModel MenuViewModel { get; }
        public List<ViewModelBase> ViewModels { get; }

        public MainViewModel(IDialogService dialogService, MenuViewModel menuViewModel, PortConfViewModel portConfViewModel, ScriptOptimizationViewModel scriptOptimizationViewModel)
        {
            MenuViewModel = menuViewModel;
            ViewModels = new List<ViewModelBase> { portConfViewModel, scriptOptimizationViewModel };
        }
    }
}
