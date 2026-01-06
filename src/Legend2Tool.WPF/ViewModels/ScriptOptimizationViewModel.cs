using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HandyControl.Controls;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.State;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class ScriptOptimizationViewModel : ViewModelBase, IRecipient<M2ConfigChangedMessage>
    {
        private readonly IScriptOptimizationService _scriptOptimizationService;
        private readonly ConfigStore _configStore;
        private readonly ILogger _logger;


        private string? _lastSortProperty;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

        [ObservableProperty]
        int _minMonBurstRate = 10;

        public string Head { get; } = "服务器脚本优化";

        public ObservableCollection<DuplicatedTriggerEntry> DuplicatedTriggers { get; set; }
        public ICollectionView DuplicatedTriggersView { get; }

        private bool CanExecuteOptimization => _configStore.ServerDirectory != string.Empty;

        public ScriptOptimizationViewModel(ILogger logger, IScriptOptimizationService scriptOptimizationService, ConfigStore configStore)
        {
            WeakReferenceMessenger.Default.Register<M2ConfigChangedMessage>(this);
            _logger = logger;
            _scriptOptimizationService = scriptOptimizationService;
            _configStore = configStore;
            DuplicatedTriggers = new ObservableCollection<DuplicatedTriggerEntry>();
            DuplicatedTriggersView = CollectionViewSource.GetDefaultView(DuplicatedTriggers);
        }


        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        async Task DetectDuplicatedTriggerAsync()
        {
            try
            {
                DuplicatedTriggers.Clear();
                var results = await Task.Run(() => _scriptOptimizationService.DetectDuplicatedTriggerAsync());
                if (results.Any())
                {
                    foreach (var result in results)
                    {
                        DuplicatedTriggers.Add(result);
                    }
                    Growl.SuccessGlobal($"检测到 {results.Count} 个重复调用脚本。");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "查询重复调用脚本失败！");
                Growl.ErrorGlobal("查询重复调用脚本失败！");
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        private void SortDuplicatedTriggers(string propertyName)
        {
            if (_lastSortProperty == propertyName)
            {
                _lastSortDirection = _lastSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }
            else
                _lastSortDirection = ListSortDirection.Ascending;

            _lastSortProperty = propertyName;

            DuplicatedTriggersView.SortDescriptions.Clear();
            DuplicatedTriggersView.SortDescriptions.Add(new SortDescription(propertyName, _lastSortDirection));
            DuplicatedTriggersView.Refresh();
        }

        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        private void OpenFile(DuplicatedTriggerEntry entry)
        {
            _scriptOptimizationService.OpenFile(entry);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        private void CopyTriggerField(DuplicatedTriggerEntry entry)
        {
            if (entry != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(entry.TriggerField);
                        Growl.SuccessGlobal("触发器名称已复制到剪贴板");
                    }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        Growl.ErrorGlobal($"无法复制到剪贴板: {ex.Message}");
                    }
                });
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        private async Task OptimizingCallsAsync()
        {
            try
            {
                await Task.Run(() => _scriptOptimizationService.OptimizingCallsAsync());
                Growl.SuccessGlobal($"优化完成！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"优化CALL调用脚本失败！{ex.Message}");
                Growl.ErrorGlobal("优化CALL调用脚本失败！");
            }
        }
        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        private async Task DropRateCalculatorAsync()
        {
            try
            {
                await Task.Run(() => _scriptOptimizationService.DropRateCalculatorAsync());
                Growl.SuccessGlobal($"爆率查询生成完成！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"爆率查询生成失败！{ex.Message}");
                Growl.ErrorGlobal("爆率查询生成失败！");
            }
        }
        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        private async Task OptimizingMinMonBurstRateAsync()
        {
            try
            {
                await Task.Run(() => _scriptOptimizationService.OptimizingMinMonBurstRateAsync(MinMonBurstRate));
                Growl.SuccessGlobal($"优化完成！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"优化爆率文件失败！{ex.Message}");
                Growl.ErrorGlobal("优化爆率文件失败！");
            }
        }


        public void Receive(M2ConfigChangedMessage message)
        {
            OnPropertyChanged(string.Empty);
            DetectDuplicatedTriggerCommand.NotifyCanExecuteChanged();
            SortDuplicatedTriggersCommand.NotifyCanExecuteChanged();
            OpenFileCommand.NotifyCanExecuteChanged();
            CopyTriggerFieldCommand.NotifyCanExecuteChanged();
            OptimizingCallsCommand.NotifyCanExecuteChanged();
            DropRateCalculatorCommand.NotifyCanExecuteChanged();
        }
    }
}
