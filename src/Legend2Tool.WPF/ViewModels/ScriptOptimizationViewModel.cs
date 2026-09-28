using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HandyControl.Controls;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.Services.ApplicationSettings;
using Legend2Tool.WPF.Services.Presentation;
using Legend2Tool.WPF.Services.ScriptOptimization;
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
        private readonly IDialogService _dialogService;
        private readonly IDropRateSiteService _dropRateSiteService;
        private readonly AppConfig _appConfig;
        private readonly AppConfigService _appConfigService;

        [ObservableProperty]
        private string _dropRateDirectory = string.Empty;

        [ObservableProperty]
        private string _dropRateStatus = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SelectDropRateDirectoryCommand))]
        [NotifyCanExecuteChangedFor(nameof(DropRateCalculatorCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteDropRateVersionCommand))]
        private bool _isDropRateBusy;

        public ObservableCollection<DropRateVersion> DropRateVersions { get; } = [];
        private bool CanManageDropRate => !IsDropRateBusy;
        private bool CanGenerateDropRate => CanExecuteOptimization && !IsDropRateBusy;


        private string? _lastSortProperty;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

        public string Head { get; } = "服务器脚本优化";

        public ObservableCollection<DuplicatedTriggerEntry> DuplicatedTriggers { get; set; }
        public ICollectionView DuplicatedTriggersView { get; }

        private bool CanExecuteOptimization => _configStore.ServerDirectory != string.Empty;

        public ScriptOptimizationViewModel(ILogger logger, IScriptOptimizationService scriptOptimizationService,
            ConfigStore configStore, IDialogService dialogService, IDropRateSiteService dropRateSiteService,
            AppConfig appConfig, AppConfigService appConfigService)
        {
            WeakReferenceMessenger.Default.Register<M2ConfigChangedMessage>(this);
            _logger = logger;
            _scriptOptimizationService = scriptOptimizationService;
            _configStore = configStore;
            _dialogService = dialogService;
            _dropRateSiteService = dropRateSiteService;
            _appConfig = appConfig;
            _appConfigService = appConfigService;
            DuplicatedTriggers = new ObservableCollection<DuplicatedTriggerEntry>();
            DuplicatedTriggersView = CollectionViewSource.GetDefaultView(DuplicatedTriggers);
            DropRateDirectory = appConfig.ScriptOptimization.DropRateDirectory;
            LoadDropRateVersions();
        }

        [RelayCommand(CanExecute = nameof(CanManageDropRate))]
        private void SelectDropRateDirectory()
        {
            string? selected = _dialogService.ShowFolderBrowserDialog(DropRateDirectory);
            if (string.IsNullOrWhiteSpace(selected)) return;
            string previous = _appConfig.ScriptOptimization.DropRateDirectory;
            try
            {
                _appConfig.ScriptOptimization.DropRateDirectory = selected;
                _appConfigService.Save(_appConfig);
                DropRateDirectory = selected;
                LoadDropRateVersions();
            }
            catch (Exception ex)
            {
                _appConfig.ScriptOptimization.DropRateDirectory = previous;
                _logger.Error(ex, "保存爆率查询目录失败");
                Growl.ErrorGlobal($"保存爆率查询目录失败：{ex.Message}");
            }
        }

        private void LoadDropRateVersions()
        {
            DropRateVersions.Clear();
            try
            {
                if (!_dropRateSiteService.HasCustomFile(DropRateDirectory))
                {
                    DropRateStatus = string.IsNullOrWhiteSpace(DropRateDirectory)
                        ? "未选择目录，生成文件将保存到服务器目录。"
                        : "未找到 js/custom.js，生成文件将保存到服务器目录。";
                    return;
                }
                foreach (var item in _dropRateSiteService.Load(DropRateDirectory)) DropRateVersions.Add(item);
                DropRateStatus = $"已加载 {DropRateVersions.Count} 个版本；生成使用当前服务器配置。";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "读取爆率版本列表失败");
                DropRateStatus = $"读取失败：{ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanManageDropRate))]
        private async Task DeleteDropRateVersionAsync(DropRateVersion? version)
        {
            if (version is null) return;
            IsDropRateBusy = true;
            string directory = DropRateDirectory;
            try
            {
                await Task.Run(() => _dropRateSiteService.Delete(directory, version));
                Growl.SuccessGlobal($"已删除版本：{version.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "删除爆率版本失败");
                Growl.ErrorGlobal($"删除爆率版本失败：{ex.Message}");
            }
            finally
            {
                LoadDropRateVersions();
                IsDropRateBusy = false;
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
        [RelayCommand(CanExecute = nameof(CanGenerateDropRate))]
        private async Task DropRateCalculatorAsync()
        {
            IsDropRateBusy = true;
            string directory = DropRateDirectory;
            try
            {
                await Task.Run(() => _scriptOptimizationService.DropRateCalculatorAsync(directory));
                Growl.SuccessGlobal($"爆率查询生成完成！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"爆率查询生成失败！{ex.Message}");
                Growl.ErrorGlobal($"爆率查询生成失败：{ex.Message}");
            }
            finally
            {
                LoadDropRateVersions();
                IsDropRateBusy = false;
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
