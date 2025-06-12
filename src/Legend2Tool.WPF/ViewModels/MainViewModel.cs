using Legend2Tool.WPF.Services;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class MainViewModel
    {
        public MenuViewModel MenuViewModel { get; } 
        public PortConfViewModel PortConfViewModel { get; } 

        public MainViewModel(IDialogService dialogService, PortConfViewModel portConfViewModel, MenuViewModel menuViewModel)
        {
            PortConfViewModel = portConfViewModel;
            MenuViewModel = menuViewModel;
        }
    }
}
