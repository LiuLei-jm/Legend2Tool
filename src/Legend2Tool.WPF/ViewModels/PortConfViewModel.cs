using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HandyControl.Controls;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.State;
using Serilog;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class PortConfViewModel : ViewModelBase, IRecipient<M2ConfigChangedMessage>, IRecipient<PatchDirectoryChangedMessage>
    {
        #region Fields
        private readonly IFileService _fileService;
        private readonly IEncodingService _encodingService;
        private readonly ILogger _logger;
        private ConfigStore _configStore;
        private ProgressStore _progressStore;

        [ObservableProperty]
        private int _modifyNumberOfPort = 0;

        [ObservableProperty]
        private string _selectedEncoding = "GB18030";

        #endregion

        #region Constructor
        public PortConfViewModel(ConfigStore configStore, IFileService fileService, IEncodingService encodingService, ILogger logger, ProgressStore progressStore)
        {
            WeakReferenceMessenger.Default.Register<M2ConfigChangedMessage>(this);
            WeakReferenceMessenger.Default.Register<PatchDirectoryChangedMessage>(this);
            _configStore = configStore;
            _fileService = fileService;
            _encodingService = encodingService;
            _logger = logger;
            _progressStore = progressStore;

        }
        #endregion

        #region Properties
        public string Head { get; } = "服务器端口设置";
        private bool CanExecuteConfigCommands => _configStore.ServerDirectory != string.Empty;

        public string? GameName
        {
            get => _configStore.M2Config.GameName;
            set => SetProperty(_configStore.M2Config.GameName, value, _configStore, (m, v) => _configStore.M2Config.GameName = v);
        }
        public string? ExtIPAddr
        {
            get => _configStore.M2Config.ExtIPaddr;
            set => SetProperty(_configStore.M2Config.ExtIPaddr, value, _configStore, (m, v) => _configStore.M2Config.ExtIPaddr = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int DBServerGatePort
        {
            get => _configStore.M2Config.DBServerGatePort;
            set => SetProperty(_configStore.M2Config.DBServerGatePort, value, _configStore, (m, v) => _configStore.M2Config.DBServerGatePort = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int DBServerServerPort
        {
            get => _configStore.M2Config.DBServerServerPort;
            set => SetProperty(_configStore.M2Config.DBServerServerPort, value, _configStore, (m, v) => _configStore.M2Config.DBServerServerPort = v);
        }
        public int DBServerGetStart
        {
            get => _configStore.M2Config.DBServerGetStart;
            set => SetProperty(_configStore.M2Config.DBServerGetStart, value, _configStore, (m, v) => _configStore.M2Config.DBServerGetStart = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int M2ServerGatePort
        {
            get => _configStore.M2Config.M2ServerGatePort;
            set => SetProperty(_configStore.M2Config.M2ServerGatePort, value, _configStore, (m, v) => _configStore.M2Config.M2ServerGatePort = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int M2ServerMsgSrvPort
        {
            get => _configStore.M2Config.M2ServerMsgSrvPort;
            set => SetProperty(_configStore.M2Config.M2ServerMsgSrvPort, value, _configStore, (m, v) => _configStore.M2Config.M2ServerMsgSrvPort = v);
        }
        public int M2ServerGetStart
        {
            get => _configStore.M2Config.M2ServerGetStart;
            set => SetProperty(_configStore.M2Config.M2ServerGetStart, value, _configStore, (m, v) => _configStore.M2Config.M2ServerGetStart = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort1
        {
            get => _configStore.M2Config.RunGateGatePort1;
            set => SetProperty(_configStore.M2Config.RunGateGatePort1, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort1 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort2
        {
            get => _configStore.M2Config.RunGateGatePort2;
            set => SetProperty(_configStore.M2Config.RunGateGatePort2, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort2 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort3
        {
            get => _configStore.M2Config.RunGateGatePort3;
            set => SetProperty(_configStore.M2Config.RunGateGatePort3, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort3 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort4
        {
            get => _configStore.M2Config.RunGateGatePort4;
            set => SetProperty(_configStore.M2Config.RunGateGatePort4, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort4 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort5
        {
            get => _configStore.M2Config.RunGateGatePort5;
            set => SetProperty(_configStore.M2Config.RunGateGatePort5, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort5 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort6
        {
            get => _configStore.M2Config.RunGateGatePort6;
            set => SetProperty(_configStore.M2Config.RunGateGatePort6, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort6 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort7
        {
            get => _configStore.M2Config.RunGateGatePort7;
            set => SetProperty(_configStore.M2Config.RunGateGatePort7, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort7 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int RunGateGatePort8
        {
            get => _configStore.M2Config.RunGateGatePort8;
            set => SetProperty(_configStore.M2Config.RunGateGatePort8, value, _configStore, (m, v) => _configStore.M2Config.RunGateGatePort8 = v);
        }
        public int RunGateCount
        {
            get => _configStore.M2Config.RunGateCount;
            set => SetProperty(_configStore.M2Config.RunGateCount, value, _configStore, (m, v) => _configStore.M2Config.RunGateCount = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int LoginGateGatePort
        {
            get => _configStore.M2Config.LoginGateGatePort;
            set => SetProperty(_configStore.M2Config.LoginGateGatePort, value, _configStore, (m, v) => _configStore.M2Config.LoginGateGatePort = v);
        }
        public int LoginGateGetStart
        {
            get => _configStore.M2Config.LoginGateGetStart;
            set => SetProperty(_configStore.M2Config.LoginGateGetStart, value, _configStore, (m, v) => _configStore.M2Config.LoginGateGetStart = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int SelGateGatePort
        {
            get => _configStore.M2Config.SelGateGatePort;
            set => SetProperty(_configStore.M2Config.SelGateGatePort, value, _configStore, (m, v) => _configStore.M2Config.SelGateGatePort = v);
        }
        public int SelGateGetStart
        {
            get => _configStore.M2Config.SelGateGetStart;
            set => SetProperty(_configStore.M2Config.SelGateGetStart, value, _configStore, (m, v) => _configStore.M2Config.SelGateGetStart = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int SelGateGatePort1
        {
            get => _configStore.M2Config.SelGateGatePort1;
            set => SetProperty(_configStore.M2Config.SelGateGatePort1, value, _configStore, (m, v) => _configStore.M2Config.SelGateGatePort1 = v);
        }
        public int SelGateGetStart1
        {
            get => _configStore.M2Config.SelGateGetStart1;
            set => SetProperty(_configStore.M2Config.SelGateGetStart1, value, _configStore, (m, v) => _configStore.M2Config.SelGateGetStart1 = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int LoginServerGatePort
        {
            get => _configStore.M2Config.LoginServerGatePort;
            set => SetProperty(_configStore.M2Config.LoginServerGatePort, value, _configStore, (m, v) => _configStore.M2Config.LoginServerGatePort = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int LoginServerServerPort
        {
            get => _configStore.M2Config.LoginServerServerPort;
            set => SetProperty(_configStore.M2Config.LoginServerServerPort, value, _configStore, (m, v) => _configStore.M2Config.LoginServerServerPort = v);
        }
        public int LoginServerGetStart
        {
            get => _configStore.M2Config.LoginServerGetStart;
            set => SetProperty(_configStore.M2Config.LoginServerGetStart, value, _configStore, (m, v) => _configStore.M2Config.LoginServerGetStart = v);
        }
        [Range(1, 65535, ErrorMessage = "必须在1至65535之间")]
        public int LogServerPort
        {
            get => _configStore.M2Config.LogServerPort;
            set => SetProperty(_configStore.M2Config.LogServerPort, value, _configStore, (m, v) => _configStore.M2Config.LogServerPort = v);
        }
        public int LogServerGetStart
        {
            get => _configStore.M2Config.LogServerGetStart;
            set => SetProperty(_configStore.M2Config.LogServerGetStart, value, _configStore, (m, v) => _configStore.M2Config.LogServerGetStart = v);
        }
        public string MainAddress
        {
            get => _configStore.LauncherConfig.MainAddress ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.MainAddress, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.MainAddress = v);
        }
        public string BackupAddress
        {
            get => _configStore.LauncherConfig.BackupAddress ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.BackupAddress, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.BackupAddress = v);
        }
        public string MainTcpListServer
        {
            get => _configStore.LauncherConfig.MainTcpListServer ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.MainTcpListServer, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.MainTcpListServer = v);
        }
        public string BackupTcpListServer
        {
            get => _configStore.LauncherConfig.BackupTcpListServer ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.BackupTcpListServer, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.BackupTcpListServer = v);
        }
        public int MainTcpPort
        {
            get => _configStore.LauncherConfig.MainTcpPort;
            set => SetProperty(_configStore.LauncherConfig.MainTcpPort, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.MainTcpPort = v);
        }
        public int BackupTcpPort
        {
            get => _configStore.LauncherConfig.BackupTcpPort;
            set => SetProperty(_configStore.LauncherConfig.BackupTcpPort, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.BackupTcpPort = v);
        }
        public string MainTcpConfigFile
        {
            get => _configStore.LauncherConfig.MainTcpConfigFile ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.MainTcpConfigFile, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.MainTcpConfigFile = v);
        }
        public string BackupTcpConfigFile
        {
            get => _configStore.LauncherConfig.BackupTcpConfigFile ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.BackupTcpConfigFile, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.BackupTcpConfigFile = v);
        }
        public string LauncherName
        {
            get => _configStore.LauncherConfig.LauncherName ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.LauncherName, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.LauncherName = v);
        }
        public string ResourcesDir
        {
            get => _configStore.LauncherConfig.ResourcesDir ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.ResourcesDir, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.ResourcesDir = v);
        }
        public string UpdatePassword
        {
            get => _configStore.LauncherConfig.UpdatePassword ?? string.Empty;
            set => SetProperty(_configStore.LauncherConfig.UpdatePassword, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.UpdatePassword = v);
        }
        public int MultiInstanceCount
        {
            get => _configStore.LauncherConfig.MultiInstanceCount;
            set => SetProperty(_configStore.LauncherConfig.MultiInstanceCount, value, _configStore.LauncherConfig, (m, v) => _configStore.LauncherConfig.MultiInstanceCount = v);
        }
        #endregion

        #region Functions

        public void Receive(M2ConfigChangedMessage message)
        {
            OnPropertyChanged(string.Empty);
            SetDefaultPortConfCommand.NotifyCanExecuteChanged();
            BatchEditCommand.NotifyCanExecuteChanged();
            ConvertEncodingCommand.NotifyCanExecuteChanged();
            SaveConfigToFileCommand.NotifyCanExecuteChanged();
            GetExtIpAddrCommand.NotifyCanExecuteChanged();
            SetLocalIpCommand.NotifyCanExecuteChanged();
            SetByServerNameCommand.NotifyCanExecuteChanged();
        }
        public void Receive(PatchDirectoryChangedMessage message)
        {
            OnPropertyChanged(string.Empty);
            GenerateByGamePinyinCommand.NotifyCanExecuteChanged();
        }
        #endregion

        #region Commands
        [RelayCommand(CanExecute = nameof(CanExecuteConfigCommands))]
        private void SetDefaultPortConf()
        {
            M2ServerGatePort = 5000;
            M2ServerMsgSrvPort = 4900;
            _configStore.M2Config.DynamicIPMode = 0;
            DBServerGatePort = 5100;
            DBServerServerPort = 6000;
            RunGateCount = 1;
            RunGateGatePort1 = 7200;
            RunGateGatePort2 = 7300;
            RunGateGatePort3 = 7400;
            RunGateGatePort4 = 7500;
            RunGateGatePort5 = 7600;
            RunGateGatePort6 = 7700;
            RunGateGatePort7 = 7800;
            RunGateGatePort8 = 7900;
            _configStore.M2Config.LoginGateGetStart = 1;
            LoginGateGatePort = 7000;
            SelGateGetStart = 1;
            SelGateGatePort = 7100;
            SelGateGetStart1 = 0;
            SelGateGatePort1 = 6200;
            LoginServerGatePort = 5500;
            LoginServerServerPort = 5600;
            LogServerPort = 10000;
            if (_configStore.M2Config is GEEConfig geeConfig
                )
            {
                geeConfig.LoginGateGetStart1 = 0;
                geeConfig.LoginGateGatePort1 = 6100;
                geeConfig.RunGateGetMultiThread = 0;
                geeConfig.RunGateDBPort1 = 27200;
                geeConfig.RunGateDBPort2 = 27300;
                geeConfig.RunGateDBPort3 = 27400;
                geeConfig.RunGateDBPort4 = 27500;
                geeConfig.RunGateDBPort5 = 27600;
                geeConfig.RunGateDBPort6 = 27700;
                geeConfig.RunGateDBPort7 = 27800;
                geeConfig.RunGateDBPort8 = 27900;
            }
            if (_configStore.M2Config is BLUEConfig blueConfig)
            {
                blueConfig.LoginServerMonPort = 3000;
            }
            Growl.Success("默认配置载入成功");
        }

        [RelayCommand(CanExecute = nameof(CanExecuteConfigCommands))]
        private void BatchEdit()
        {
            M2ServerGatePort += ModifyNumberOfPort;
            M2ServerMsgSrvPort += ModifyNumberOfPort;
            DBServerGatePort += ModifyNumberOfPort;
            DBServerServerPort += ModifyNumberOfPort;
            RunGateGatePort1 += ModifyNumberOfPort;
            RunGateGatePort2 += ModifyNumberOfPort;
            RunGateGatePort3 += ModifyNumberOfPort;
            RunGateGatePort4 += ModifyNumberOfPort;
            RunGateGatePort5 += ModifyNumberOfPort;
            RunGateGatePort6 += ModifyNumberOfPort;
            RunGateGatePort7 += ModifyNumberOfPort;
            RunGateGatePort8 += ModifyNumberOfPort;
            LoginGateGatePort += ModifyNumberOfPort;
            SelGateGatePort += ModifyNumberOfPort;
            SelGateGatePort1 += ModifyNumberOfPort;
            LoginServerGatePort += ModifyNumberOfPort;
            LoginServerServerPort += ModifyNumberOfPort;
            LogServerPort += ModifyNumberOfPort;
            if (_configStore.M2Config is GEEConfig geeConfig
                )
            {
                geeConfig.LoginGateGatePort1 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort1 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort2 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort3 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort4 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort5 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort6 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort7 += ModifyNumberOfPort;
                geeConfig.RunGateDBPort8 += ModifyNumberOfPort;
            }
            if (_configStore.M2Config is BLUEConfig blueConfig)
            {
                blueConfig.LoginServerMonPort += ModifyNumberOfPort;
            }
            Growl.Success("批量编辑端口成功");
        }

        [RelayCommand(CanExecute = nameof(CanExecuteConfigCommands))]
        private async Task ConvertEncodingAsync()
        {
            string convertDirectory = Path.Combine(_configStore.ServerDirectory, @"Mir200\Envir");
            if (!Path.Exists(convertDirectory))
            {
                Growl.Error($"目录不存在：{convertDirectory}");
                return;
            }
            IProgress<ProgressStore> progress = new Progress<ProgressStore>(report =>
            {
                _progressStore.ProgressPercentage = report.ProgressPercentage;
                _progressStore.ProgressText = report.ProgressText;
            });
            _progressStore.ProgressPercentage = 0;
            _progressStore.ProgressText = string.Empty;

            int currentProgress = 0;
            int progressCount = 0;

            var files = _fileService.GetFiles(convertDirectory, new List<string> { "*.txt", "*.ini" }, SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileName(file);
                    _progressStore.ProgressPercentage = (int)((++currentProgress / (double)files.Count) * 100);
                    _progressStore.ProgressText = $"{currentProgress}/{files.Count}：{fileName}";
                    progress.Report(_progressStore);
                    if (fileInfo.Length > 50)
                    {
                        Encoding fileEncoding = _encodingService.DetectFileEncoding(file);
                        if (fileEncoding != _encodingService.GetEncodingByName(SelectedEncoding))
                        {
                            ++progressCount;
                            await Task.Run(() => _encodingService.ConvertFileEncoding(file, file, fileEncoding, SelectedEncoding));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"转换文件编码失败：{file}，错误信息：{ex.Message}");
                }
            }
            _progressStore.ProgressText = $"转换完成，处理了{progressCount}个文件.";
            progress.Report(_progressStore);
            Growl.Success($"转换完成，处理了{progressCount}个文件.");
        }

        [RelayCommand(CanExecute = nameof(CanExecuteConfigCommands))]
        private void SaveConfigToFile()
        {
            ValidateAllProperties();

            _configStore.RenamePatchDirectory(ResourcesDir);
            _configStore.ModifyPAKPath();
            _configStore.SaveConfigFile();
            Growl.Success("保存成功");
        }

        [RelayCommand(CanExecute = nameof(CanExecuteConfigCommands))]
        private async Task GetExtIpAddrAsync()
        {
            try
            {
                ExtIPAddr = await _configStore.GetExternalIpAddressAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"获取外网IP地址失败:{ex.Message}", ex);
            }
            Growl.Success("获取外网IP地址成功");
        }

        [RelayCommand(CanExecute = nameof(CanExecuteConfigCommands))]
        private void SetLocalIp()
        {
            ExtIPAddr = "127.0.0.1";
            Growl.Success("设置本地IP地址成功");
        }
        [RelayCommand(CanExecute = nameof(CanExecuteConfigCommands))]
        private void SetByServerName()
        {
            LauncherName = _configStore.GetLauncherName();
            Growl.Success("客户端名称设置成功!");
        }
        [RelayCommand(CanExecute =nameof(CanExecuteConfigCommands))]
        private void GenerateByGamePinyin()
        {
            ResourcesDir = _configStore.GetResourcesDirByGamePinyin(LauncherName);
            Growl.Success("资源目录设置成功!");
        }


        #endregion
    }
}
