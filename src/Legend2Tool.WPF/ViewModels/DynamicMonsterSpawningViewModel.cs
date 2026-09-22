using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HandyControl.Controls;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.Services.ApplicationSettings;
using Legend2Tool.WPF.Services.DynamicMonsterSpawning;
using Legend2Tool.WPF.Services.ScriptOptimization;
using Legend2Tool.WPF.State;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class DynamicMonsterSpawningViewModel : ViewModelBase, IRecipient<M2ConfigChangedMessage>
    {
        private readonly IDynamicMonsterSpawningService _dynamicMonsterSpawningService;
        private readonly IScriptOptimizationService _scriptOptimizationService;
        private readonly ConfigStore _configStore;
        private readonly ILogger _logger;
        private readonly AppConfig _appConfig;
        private readonly AppConfigService _appConfigService;

        [ObservableProperty]
        string _filterMapCode;
        partial void OnFilterMapCodeChanged(string? oldValue, string newValue)
        {
            _scriptOptimizationService.UpdateMainCityLists(newValue);
        }
        [ObservableProperty]
        string _filterMonName;
        [ObservableProperty]
        string _filterMonCount;
        [ObservableProperty]
        string _filterInterval;
        [ObservableProperty]
        string _filterMonNameColor;
        [ObservableProperty]
        string _selectedTimeUnit;
        [ObservableProperty]
        int _refreshMonInterval;
        [ObservableProperty]
        int _clearMonInterval;
        [ObservableProperty]
        int _refreshMonMultiplier;
        [ObservableProperty]
        [Required(ErrorMessage = "请填写刷怪触发器名称")]
        string _refreshMonTrigger;
        [ObservableProperty]
        [Required(ErrorMessage = "请填写清怪触发器名称")]
        string _clearMonTrigger;
        [ObservableProperty]
        bool _isClearMon;
        [ObservableProperty]
        bool _isCommentMongen;
        [ObservableProperty]
        bool _isLimitRefreshInterval;
        [ObservableProperty]
        int _maxRefreshInterval;
        [ObservableProperty]
        int _maxRefreshCount;
        [ObservableProperty]
        int _maxMonstersPerMap;
        [ObservableProperty]
        bool _isBusy;
        public ObservableCollection<DynamicMonsterSpawningResult> GenerationResults { get; } = [];
        public string Head { get; } = "动态刷怪配置";
        private bool CanExecuteAction => _configStore.ServerDirectory != string.Empty;
        public DynamicMonsterSpawningViewModel(
            IDynamicMonsterSpawningService dynamicMonsterSpawningService,
            ConfigStore configStore,
            ILogger logger,
            IScriptOptimizationService scriptOptimizationService,
            AppConfig appConfig,
            AppConfigService appConfigService
        )
        {
            DynamicMonsterSpawningConfig config = appConfig.DynamicMonsterSpawning;
            WeakReferenceMessenger.Default.Register<M2ConfigChangedMessage>(this);
            _dynamicMonsterSpawningService = dynamicMonsterSpawningService;
            _configStore = configStore;
            _logger = logger;
            _scriptOptimizationService = scriptOptimizationService;
            _appConfig = appConfig;
            _appConfigService = appConfigService;
            _filterMapCode = config.FilterMapCode;
            _filterMonName = config.FilterMonName;
            _filterMonCount = config.FilterMonCount;
            _filterInterval = config.FilterInterval;
            _filterMonNameColor = config.FilterMonNameColor;
            _selectedTimeUnit = config.SelectedTimeUnit;
            _refreshMonInterval = config.RefreshMonInterval;
            _clearMonInterval = config.ClearMonInterval;
            _refreshMonMultiplier = config.RefreshMonMultiplier;
            _refreshMonTrigger = config.RefreshMonTrigger;
            _clearMonTrigger = config.ClearMonTrigger;
            _isClearMon = config.IsClearMon;
            _isCommentMongen = config.IsCommentMongen;
            _isLimitRefreshInterval = config.IsLimitRefreshInterval;
            _maxRefreshInterval = config.MaxRefreshInterval;
            _maxRefreshCount = config.MaxRefreshCount;
            _maxMonstersPerMap = config.MaxMonstersPerMap;
        }

        [RelayCommand]
        private void SaveConfig()
        {
            ValidateAllProperties();
            if (HasErrors)
            {
                Growl.ErrorGlobal("请检查输入参数是否正确。");
                return;
            }

            try
            {
                UpdateConfigFromViewModel();
                _appConfigService.Save(_appConfig);
                Growl.SuccessGlobal("配置保存成功！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"保存配置时发生错误: {ex.Message}");
                Growl.ErrorGlobal("保存配置时发生错误，请检查程序目录的写入权限。");
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteAction))]
        async Task GenerateRefreshMonScript()
        {
            ValidateAllProperties();
            if (HasErrors)
            {
                Growl.ErrorGlobal("请检查输入参数是否正确。");
                return;
            }

            IsBusy = true;
            try
            {
                GenerationResults.Clear();
                var options = CollectRefreshScriptOption();
                IReadOnlyList<DynamicMonsterSpawningResult> results =
                    await _dynamicMonsterSpawningService.GenerateRefreshMonScriptAsync(options);
                foreach (DynamicMonsterSpawningResult result in results)
                {
                    GenerationResults.Add(result);
                }
                Growl.SuccessGlobal("脚本生成成功!");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"生成脚本时发生错误: {ex.Message}");
                Growl.ErrorGlobal("生成脚本时发生错误，请检查输入参数是否正确。");
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
                GenerationResults.Clear();
                Growl.SuccessGlobal("脚本清除成功！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"清除脚本时发生错误:{ex.Message}");
                Growl.ErrorGlobal("清除脚本时发生错误，请检查文件是否被占用。");
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
                MaxRefreshInterval = MaxRefreshInterval,
                MaxRefreshCount = MaxRefreshCount,
                MaxMonstersPerMap = MaxMonstersPerMap
            };
        }

        private void UpdateConfigFromViewModel()
        {
            DynamicMonsterSpawningConfig config = _appConfig.DynamicMonsterSpawning;
            config.FilterMapCode = FilterMapCode;
            config.FilterMonName = FilterMonName;
            config.FilterMonCount = FilterMonCount;
            config.FilterInterval = FilterInterval;
            config.FilterMonNameColor = FilterMonNameColor;
            config.SelectedTimeUnit = SelectedTimeUnit;
            config.RefreshMonInterval = RefreshMonInterval;
            config.ClearMonInterval = ClearMonInterval;
            config.RefreshMonMultiplier = RefreshMonMultiplier;
            config.RefreshMonTrigger = RefreshMonTrigger;
            config.ClearMonTrigger = ClearMonTrigger;
            config.IsClearMon = IsClearMon;
            config.IsCommentMongen = IsCommentMongen;
            config.IsLimitRefreshInterval = IsLimitRefreshInterval;
            config.MaxRefreshInterval = MaxRefreshInterval;
            config.MaxRefreshCount = MaxRefreshCount;
            config.MaxMonstersPerMap = MaxMonstersPerMap;
        }

        public void Receive(M2ConfigChangedMessage message)
        {
            GenerationResults.Clear();
            GenerateRefreshMonScriptCommand.NotifyCanExecuteChanged();
            ClearRefreshMonScriptCommand.NotifyCanExecuteChanged();
        }
    }
}
