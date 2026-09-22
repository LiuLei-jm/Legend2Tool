using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Legend2Tool.WPF.Models.Authentication;
using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services.Authentication;
using Legend2Tool.WPF.Services.ScriptSets;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class InsertScriptViewModel : ViewModelBase
    {
        private readonly IAuthenticationService _authenticationService;
        private readonly IScriptSetService _scriptSetService;
        private readonly IScriptSetInstallationService _scriptSetInstallationService;
        private readonly ICredentialStore _credentialStore;
        private readonly ILogger _logger;

        [ObservableProperty]
        [Required(ErrorMessage = "请输入用户名")]
        private string _username = string.Empty;

        [ObservableProperty]
        [Required(ErrorMessage = "请输入密码")]
        private string _password = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private bool _isLoggingIn;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        [NotifyCanExecuteChangedFor(nameof(LoadScriptSetsCommand))]
        [NotifyCanExecuteChangedFor(nameof(InstallScriptSetCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveScriptSetCommand))]
        private bool _isLoggedIn;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _rememberCredentials;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadScriptSetsCommand))]
        [NotifyCanExecuteChangedFor(nameof(InstallScriptSetCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveScriptSetCommand))]
        private bool _isLoadingScriptSets;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadScriptSetsCommand))]
        [NotifyCanExecuteChangedFor(nameof(InstallScriptSetCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveScriptSetCommand))]
        private bool _isInstallingScriptSet;

        [ObservableProperty]
        private string _scriptSetStatusMessage = string.Empty;

        public ObservableCollection<ScriptSetInfo> ScriptSets { get; } = [];
        public ICollectionView ScriptSetsView { get; }

        public InsertScriptViewModel(
            IAuthenticationService authenticationService,
            IScriptSetService scriptSetService,
            IScriptSetInstallationService scriptSetInstallationService,
            ICredentialStore credentialStore,
            ILogger logger
        )
        {
            _authenticationService = authenticationService;
            _scriptSetService = scriptSetService;
            _scriptSetInstallationService = scriptSetInstallationService;
            _credentialStore = credentialStore;
            _logger = logger;
            LoadSavedCredentials();
            ScriptSetsView = CollectionViewSource.GetDefaultView(ScriptSets);
            ScriptSetsView.Filter = MatchesSearch;
            _authenticationService.AuthenticationStateChanged +=
                OnAuthenticationStateChanged;
            IsLoggedIn = _authenticationService.CurrentSession is not null;
            if (IsLoggedIn)
            {
                _ = LoadScriptSetsAsync();
            }
        }

        public string Head { get; } = "插入脚本";

        private bool CanLogin => !IsLoggingIn && !IsLoggedIn;
        private bool CanLoadScriptSets =>
            IsLoggedIn && !IsLoadingScriptSets && !IsInstallingScriptSet;
        private bool CanInstallScriptSet =>
            IsLoggedIn && !IsLoadingScriptSets && !IsInstallingScriptSet;
        private bool CanRemoveScriptSet =>
            IsLoggedIn && !IsLoadingScriptSets && !IsInstallingScriptSet;

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task LoginAsync()
        {
            ValidateAllProperties();
            if (HasErrors)
            {
                StatusMessage = "请输入用户名和密码。";
                return;
            }

            IsLoggingIn = true;
            StatusMessage = string.Empty;
            try
            {
                string username = Username.Trim();
                string password = Password;
                await _authenticationService.LoginAsync(username, password);
                SaveCredentialsIfRequested(username, password);
                if (!RememberCredentials)
                {
                    Password = string.Empty;
                }
                IsLoggedIn = true;
                Growl.SuccessGlobal("登录成功！");
                await LoadScriptSetsAsync();
            }
            catch (AuthenticationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning(ex, "登录 API 网络请求失败");
                StatusMessage = "无法连接登录服务，请检查网络后重试。";
            }
            catch (TaskCanceledException ex)
            {
                _logger.Warning(ex, "登录 API 请求超时");
                StatusMessage = "登录请求超时，请稍后重试。";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "登录时发生未处理错误");
                StatusMessage = "登录失败，请稍后重试。";
            }
            finally
            {
                IsLoggingIn = false;
            }
        }

        partial void OnRememberCredentialsChanged(bool value)
        {
            if (value)
            {
                return;
            }

            try
            {
                _credentialStore.Clear();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "清除已保存的登录凭据失败");
                Growl.WarningGlobal("无法清除已保存的账号密码，请检查当前用户目录权限。");
            }
        }

        private void LoadSavedCredentials()
        {
            try
            {
                SavedCredentials? credentials = _credentialStore.Load();
                if (credentials is null)
                {
                    return;
                }

                Username = credentials.Username;
                Password = credentials.Password;
                RememberCredentials = true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "加载已保存的登录凭据失败");
            }
        }

        private void SaveCredentialsIfRequested(string username, string password)
        {
            try
            {
                if (RememberCredentials)
                {
                    _credentialStore.Save(username, password);
                }
                else
                {
                    _credentialStore.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "保存登录凭据失败");
                Growl.WarningGlobal("登录成功，但账号密码保存失败。");
            }
        }

        [RelayCommand(CanExecute = nameof(CanLoadScriptSets))]
        private async Task LoadScriptSetsAsync()
        {
            IsLoadingScriptSets = true;
            ScriptSetStatusMessage = "正在加载脚本套...";
            try
            {
                IReadOnlyList<ScriptSetInfo> scriptSets =
                    await _scriptSetService.GetScriptSetsAsync();
                ScriptSets.Clear();
                foreach (ScriptSetInfo scriptSet in scriptSets)
                {
                    ScriptSets.Add(scriptSet);
                }
                ScriptSetsView.Refresh();

                UpdateScriptSetStatus();
            }
            catch (AuthenticationException ex)
            {
                ScriptSetStatusMessage = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning(ex, "获取脚本套 API 请求失败");
                ScriptSetStatusMessage = "无法获取脚本套，请检查网络后重试。";
            }
            catch (TaskCanceledException ex)
            {
                _logger.Warning(ex, "获取脚本套 API 请求超时");
                ScriptSetStatusMessage = "获取脚本套超时，请稍后重试。";
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "脚本套 API 返回的数据格式无效");
                ScriptSetStatusMessage = "脚本套数据格式无效，请联系管理员。";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取脚本套时发生未处理错误");
                ScriptSetStatusMessage = "获取脚本套失败，请稍后重试。";
            }
            finally
            {
                IsLoadingScriptSets = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanInstallScriptSet))]
        private async Task InstallScriptSetAsync(ScriptSetInfo? scriptSet)
        {
            if (scriptSet is null)
            {
                return;
            }

            MessageBoxResult confirmation = System.Windows.MessageBox.Show(
                $"确定要将脚本套“{scriptSet.Name}”插入当前服务端吗？\n\n全量脚本会替换目标文件内容，数据库数据会直接追加，素材文件会写入登录器补丁目录。",
                "确认插入脚本套",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            IsInstallingScriptSet = true;
            ScriptSetStatusMessage = $"正在插入脚本套“{scriptSet.Name}”...";
            try
            {
                ScriptSetInstallationResult result =
                    await _scriptSetInstallationService.InstallAsync(scriptSet);
                ScriptSetStatusMessage =
                    $"“{scriptSet.Name}”插入完成：脚本文件 {result.ScriptFileCount} 个，数据库数据 {result.DatabaseRowCount} 条，素材文件 {result.MaterialFileCount} 个。";
                Growl.SuccessGlobal($"脚本套“{scriptSet.Name}”插入成功！");
            }
            catch (ScriptSetInstallationException ex)
            {
                _logger.Warning(ex, "插入脚本套 {ScriptSetId} 失败", scriptSet.Id);
                ScriptSetStatusMessage = ex.Message;
                Growl.ErrorGlobal(ex.Message);
            }
            catch (AuthenticationException ex)
            {
                ScriptSetStatusMessage = ex.Message;
                Growl.ErrorGlobal(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning(ex, "获取脚本套 {ScriptSetId} 的部署数据失败", scriptSet.Id);
                ScriptSetStatusMessage = "无法获取脚本套部署数据，请检查网络后重试。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            catch (TaskCanceledException ex)
            {
                _logger.Warning(ex, "插入脚本套 {ScriptSetId} 超时", scriptSet.Id);
                ScriptSetStatusMessage = "插入脚本套超时，请稍后重试。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "脚本套 {ScriptSetId} 的部署数据格式无效", scriptSet.Id);
                ScriptSetStatusMessage = "脚本套部署数据格式无效，请联系管理员。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "插入脚本套 {ScriptSetId} 时发生未处理错误", scriptSet.Id);
                ScriptSetStatusMessage = "插入脚本套失败，请查看日志。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            finally
            {
                IsInstallingScriptSet = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanRemoveScriptSet))]
        private async Task RemoveScriptSetAsync(ScriptSetInfo? scriptSet)
        {
            if (scriptSet is null)
            {
                return;
            }

            MessageBoxResult confirmation = System.Windows.MessageBox.Show(
                $"确定要从当前服务端删除脚本套“{scriptSet.Name}”吗？\n\n片段脚本只会删除带有本脚本套标识的内容；已被修改的全量脚本或素材文件不会删除。",
                "确认删除已插入脚本套",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            IsInstallingScriptSet = true;
            ScriptSetStatusMessage = $"正在删除脚本套“{scriptSet.Name}”...";
            try
            {
                ScriptSetRemovalResult result =
                    await _scriptSetInstallationService.RemoveAsync(scriptSet);
                ScriptSetStatusMessage =
                    $"“{scriptSet.Name}”删除完成：脚本文件 {result.ScriptFileCount} 个，数据库数据 {result.DatabaseRowCount} 条，素材文件 {result.MaterialFileCount} 个。";
                Growl.SuccessGlobal($"脚本套“{scriptSet.Name}”删除完成！");
            }
            catch (ScriptSetInstallationException ex)
            {
                _logger.Warning(ex, "删除脚本套 {ScriptSetId} 失败", scriptSet.Id);
                ScriptSetStatusMessage = ex.Message;
                Growl.ErrorGlobal(ex.Message);
            }
            catch (AuthenticationException ex)
            {
                ScriptSetStatusMessage = ex.Message;
                Growl.ErrorGlobal(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning(ex, "获取脚本套 {ScriptSetId} 的部署数据失败", scriptSet.Id);
                ScriptSetStatusMessage = "无法获取脚本套部署数据，请检查网络后重试。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            catch (TaskCanceledException ex)
            {
                _logger.Warning(ex, "删除脚本套 {ScriptSetId} 超时", scriptSet.Id);
                ScriptSetStatusMessage = "删除脚本套超时，请稍后重试。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "脚本套 {ScriptSetId} 的部署数据格式无效", scriptSet.Id);
                ScriptSetStatusMessage = "脚本套部署数据格式无效，请联系管理员。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "删除脚本套 {ScriptSetId} 时发生未处理错误", scriptSet.Id);
                ScriptSetStatusMessage = "删除脚本套失败，请查看日志。";
                Growl.ErrorGlobal(ScriptSetStatusMessage);
            }
            finally
            {
                IsInstallingScriptSet = false;
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            ScriptSetsView.Refresh();
            UpdateScriptSetStatus();
        }

        private bool MatchesSearch(object item)
        {
            if (item is not ScriptSetInfo scriptSet)
            {
                return false;
            }

            string searchText = SearchText.Trim();
            return searchText.Length == 0
                || scriptSet.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || scriptSet.Description?.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase
                ) == true;
        }

        private void UpdateScriptSetStatus()
        {
            if (ScriptSets.Count == 0)
            {
                ScriptSetStatusMessage = "暂无可用的脚本套。";
                return;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                ScriptSetStatusMessage = $"共 {ScriptSets.Count} 个脚本套。";
                return;
            }

            int matchingCount = ScriptSetsView.Cast<object>().Count();
            ScriptSetStatusMessage = $"找到 {matchingCount} 个匹配的脚本套。";
        }

        private void OnAuthenticationStateChanged(
            object? sender,
            AuthenticationStateChangedEventArgs eventArgs
        )
        {
            void ApplyState()
            {
                bool becameLoggedIn = !IsLoggedIn && eventArgs.Session is not null;
                IsLoggedIn = eventArgs.Session is not null;
                if (eventArgs.Session is null && !string.IsNullOrWhiteSpace(eventArgs.Message))
                {
                    StatusMessage = eventArgs.Message;
                }
                if (eventArgs.Session is null)
                {
                    ScriptSets.Clear();
                    SearchText = string.Empty;
                    ScriptSetStatusMessage = string.Empty;
                }
                else if (becameLoggedIn && !IsLoggingIn)
                {
                    _ = LoadScriptSetsAsync();
                }
            }

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(ApplyState);
            }
            else
            {
                ApplyState();
            }
        }
    }
}
