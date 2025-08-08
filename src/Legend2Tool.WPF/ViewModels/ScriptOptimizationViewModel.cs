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
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Eventing.Reader;
using System.Windows.Data;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class ScriptOptimizationViewModel : ViewModelBase, IRecipient<M2ConfigChangedMessage>
    {
        private readonly IScriptOptimizationService _scriptOptimizationService;
        private readonly ConfigStore _configStore;
        private readonly ILogger _logger;

        private const string DefaultFilterMapCode = "0\r1\r2\r3\r4\r5\r6\r11\r12\r";
        private const string DefaultFilterMonName = "弓箭手\r弓箭守卫\r虎卫\r鹰卫\r刀卫\r卫士\r带刀护卫\r";
        private const string DefaultFilterMonCount = "1\r2\r";
        private const string DefaultSelectedTimeUnit = "分";
        private const string DefaultRefreshMonTrigger = "XGD_动态刷怪";
        private const string DefaultClearMonTrigger = "XGD_动态清怪";

        private string? _lastSortProperty;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

        [ObservableProperty]
        string _filterMapCode = DefaultFilterMapCode;
        partial void OnFilterMapCodeChanged(string? oldValue, string newValue)
        {
            var mapCodes = newValue.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _scriptOptimizationService.UpdateMainCityLists(mapCodes);
        }
        [ObservableProperty]
        string _filterMonName = DefaultFilterMonName;
        [ObservableProperty]
        string _filterMonCount = DefaultFilterMonCount;
        [ObservableProperty]
        string _filterInterval = string.Empty;
        [ObservableProperty]
        string _filterMonNameColor = string.Empty;
        [ObservableProperty]
        string _selectedTimeUnit = DefaultSelectedTimeUnit;
        [ObservableProperty]
        int _refreshMonInterval = 2;
        [ObservableProperty]
        int _clearMonInterval = 15;
        [ObservableProperty]
        int _refreshMonMultiplier = 1;
        [ObservableProperty]
        [Required(ErrorMessage = "请填写刷怪触发器名称")]
        string _refreshMonTrigger = DefaultRefreshMonTrigger;
        [ObservableProperty]
        [Required(ErrorMessage = "请填写清怪触发器名称")]
        string _clearMonTrigger = DefaultClearMonTrigger;
        [ObservableProperty]
        bool _isClearMon;
        [ObservableProperty]
        bool _isCommentMongen;
        [ObservableProperty]
        bool _isBusy;
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
        async Task GenerateRefreshMonScript()
        {
            ValidateAllProperties();
            if (HasErrors)
            {
                Growl.Error("请检查输入参数是否正确。");
                return;
            }

            IsBusy = true;
            try
            {
                var options = CollectRefreshScriptOption();
                await _scriptOptimizationService.GenerateRefreshMonScriptAsync(options);
                Growl.Success("脚本生成成功!");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"生成脚本时发生错误: {ex.Message}");
                Growl.Error("生成脚本时发生错误，请检查输入参数是否正确。");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        async Task ClearRefreshMonScript()
        {
            IsBusy = true;
            try
            {
                var options = CollectRefreshScriptOption();
                await _scriptOptimizationService.ClearRefreshMonScriptAsync(options);
                Growl.Success("脚本清除成功！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"清除脚本时发生错误:{ex.Message}");
                Growl.Error("清除脚本时发生错误，请检查文件是否被占用。");
                return;
            }
            finally
            {
                IsBusy = false;
            }
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
                    Growl.Success($"检测到 {results.Count} 个重复调用脚本。");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "查询重复调用脚本失败！");
                Growl.Error("查询重复调用脚本失败！");
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
                        Growl.Success("触发器名称已复制到剪贴板");
                    }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        Growl.Error($"无法复制到剪贴板: {ex.Message}");
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
                Growl.Success($"优化完成！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"优化CALL调用脚本失败！{ex.Message}");
                Growl.Error("优化CALL调用脚本失败！");
            }
        }
        [RelayCommand(CanExecute = nameof(CanExecuteOptimization))]
        private async Task DropRateCalculatorAsync()
        {
            try
            {
                await Task.Run(() => _scriptOptimizationService.DropRateCalculatorAsync());
                Growl.Success($"爆率查询生成完成！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"爆率查询生成失败！{ex.Message}");
                Growl.Error("爆率查询生成失败！");
            }
        }
        private RefreshOptimizationOptions CollectRefreshScriptOption()
        {
            return new RefreshOptimizationOptions
            {
                FilterMapCode = FilterMapCode,
                FilterMonName = FilterMonName,
                FilterMonCount = FilterMonCount,
                FilterInterval = FilterInterval,
                FilterMonNameColor = FilterMonNameColor,
                SelectedTimeUnit = SelectedTimeUnit,
                RefreshMonInterval = RefreshMonInterval,
                ClearMonInterval = ClearMonInterval,
                RefreshMonMultiplier = RefreshMonMultiplier,
                RefreshMonTrigger = RefreshMonTrigger,
                ClearMonTrigger = ClearMonTrigger,
                IsCommentMongen = IsCommentMongen,
                IsClearMon = IsClearMon
            };
        }

        public void Receive(M2ConfigChangedMessage message)
        {
            OnPropertyChanged(string.Empty);
            GenerateRefreshMonScriptCommand.NotifyCanExecuteChanged();
            ClearRefreshMonScriptCommand.NotifyCanExecuteChanged();
            DetectDuplicatedTriggerCommand.NotifyCanExecuteChanged();
            SortDuplicatedTriggersCommand.NotifyCanExecuteChanged();
            OpenFileCommand.NotifyCanExecuteChanged();
            CopyTriggerFieldCommand.NotifyCanExecuteChanged();
            OptimizingCallsCommand.NotifyCanExecuteChanged();
            DropRateCalculatorCommand.NotifyCanExecuteChanged();
        }
    }
}
