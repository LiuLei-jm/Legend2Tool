using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HandyControl.Controls;
using Legend2Tool.WPF.Commons;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class DynamicMonsterSpawningViewModel : ViewModelBase, IRecipient<M2ConfigChangedMessage>
    {
        private readonly IDynamicMonsterSpawningService _dynamicMonsterSpawningService;
        private readonly IScriptOptimizationService _scriptOptimizationService;
        private readonly ConfigStore _configStore;
        private readonly ILogger _logger;

        [ObservableProperty]
        string _filterMapCode = AppConstants.DefaultFilterMapCode;
        partial void OnFilterMapCodeChanged(string? oldValue, string newValue)
        {
            _scriptOptimizationService.UpdateMainCityLists(newValue);
        }
        [ObservableProperty]
        string _filterMonName = AppConstants.DefaultFilterMonName;
        [ObservableProperty]
        string _filterMonCount = AppConstants.DefaultFilterMonCount;
        [ObservableProperty]
        string _filterInterval = string.Empty;
        [ObservableProperty]
        string _filterMonNameColor = string.Empty;
        [ObservableProperty]
        string _selectedTimeUnit = AppConstants.DefaultSelectedTimeUnit;
        [ObservableProperty]
        int _refreshMonInterval = 2;
        [ObservableProperty]
        int _clearMonInterval = 15;
        [ObservableProperty]
        int _refreshMonMultiplier = 1;
        [ObservableProperty]
        [Required(ErrorMessage = "请填写刷怪触发器名称")]
        string _refreshMonTrigger = AppConstants.DefaultRefreshMonTrigger;
        [ObservableProperty]
        [Required(ErrorMessage = "请填写清怪触发器名称")]
        string _clearMonTrigger = AppConstants.DefaultClearMonTrigger;
        [ObservableProperty]
        bool _isClearMon;
        [ObservableProperty]
        bool _isCommentMongen;
        [ObservableProperty]
        bool _isLimitRefreshInterval;
        [ObservableProperty]
        int _maxRefreshInterval = 30;
        [ObservableProperty]
        bool _isBusy;
        public string Head { get; } = "动态刷怪配置";
        private bool CanExecuteAction => _configStore.ServerDirectory != string.Empty;
        public DynamicMonsterSpawningViewModel(IDynamicMonsterSpawningService dynamicMonsterSpawningService, ConfigStore configStore, ILogger logger, IScriptOptimizationService scriptOptimizationService)
        {
            WeakReferenceMessenger.Default.Register<M2ConfigChangedMessage>(this);
            _dynamicMonsterSpawningService = dynamicMonsterSpawningService;
            _configStore = configStore;
            _logger = logger;
            _scriptOptimizationService = scriptOptimizationService;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteAction))]
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
                await _dynamicMonsterSpawningService.GenerateRefreshMonScriptAsync(options);
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

        [RelayCommand(CanExecute = nameof(CanExecuteAction))]
        async Task ClearRefreshMonScript()
        {
            IsBusy = true;
            try
            {
                var options = CollectRefreshScriptOption();
                await _dynamicMonsterSpawningService.ClearRefreshMonScriptAsync(options);
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
                IsClearMon = IsClearMon,
                IsLimitRefreshInterval = IsLimitRefreshInterval,
                MaxRefreshInterval = MaxRefreshInterval
            };
        }

        public void Receive(M2ConfigChangedMessage message)
        {
            GenerateRefreshMonScriptCommand.NotifyCanExecuteChanged();
            ClearRefreshMonScriptCommand.NotifyCanExecuteChanged();
        }
    }
}
