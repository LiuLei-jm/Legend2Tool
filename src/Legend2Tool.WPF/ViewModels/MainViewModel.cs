using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.State;
using System.Collections.ObjectModel;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IRecipient<ProgressChangedMessage>
    {
        private ProgressStore _progressStore;
        public MenuViewModel MenuViewModel { get; }
        public ObservableCollection<ViewModelBase> ViewModels { get; }
        public int ProgressPercentage
        {
            get => _progressStore.ProgressPercentage;
            set => SetProperty(_progressStore.ProgressPercentage, value, _progressStore, (m, v) => _progressStore.ProgressPercentage = v);
        }
        public string ProgressText
        {
            get => _progressStore.ProgressText;
            set => SetProperty(_progressStore.ProgressText, value, _progressStore, (m, v) => _progressStore.ProgressText = v);
        }

        [ObservableProperty]
        private ViewModelBase? _selectedViewModel;
        public MainViewModel(IDialogService dialogService, MenuViewModel menuViewModel, PortConfViewModel portConfViewModel, ScriptOptimizationViewModel scriptOptimizationViewModel, ProgressStore progressStore)
        {
            MenuViewModel = menuViewModel;
            ViewModels = new ObservableCollection<ViewModelBase> { portConfViewModel, scriptOptimizationViewModel };
            SelectedViewModel = portConfViewModel;
            _progressStore = progressStore;
            WeakReferenceMessenger.Default.Register<ProgressChangedMessage>(this);
        }

        public void Receive(ProgressChangedMessage message)
        {
            OnPropertyChanged(string.Empty);
        }
    }
}
