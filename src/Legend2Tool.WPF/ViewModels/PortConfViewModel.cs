using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using IniFileParser.Model;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Messages;
using Legend2Tool.WPF.Models.M2Config;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.State;
using System.IO;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class PortConfViewModel : ViewModelBase
    {
        #region Fields
        private readonly ConfigStore _configStore;


        [ObservableProperty]
        private int _dBServerGatePort;

        [ObservableProperty]
        private int _dBServerServerPort;

        [ObservableProperty]
        private int _m2ServerGatePort;

        [ObservableProperty]
        private int _m2ServerMsgSrvPort;

        [ObservableProperty]
        private int _runGateCount;

        [ObservableProperty]
        private int _runGatePort1;

        [ObservableProperty]
        private int _runGatePort2;

        [ObservableProperty]
        private int _runGatePort3;

        [ObservableProperty]
        private int _runGatePort4;

        [ObservableProperty]
        private int _runGatePort5;

        [ObservableProperty]
        private int _runGatePort6;

        [ObservableProperty]
        private int _runGatePort7;

        [ObservableProperty]
        private int _runGatePort8;

        [ObservableProperty]
        private int _loginGatePort;

        [ObservableProperty]
        private int _loginGatePort1;

        [ObservableProperty]
        private int _selGatePort;

        [ObservableProperty]
        private int _selGatePort1;

        [ObservableProperty]
        private int _loginServerGatePort;

        [ObservableProperty]
        private int _loginServerServerPort;

        [ObservableProperty]
        private int _logServerPort;

        [ObservableProperty]
        private int _changePortNumber = 0;

        #endregion

        #region Constructor
        public PortConfViewModel(ConfigStore configStore)
        {
            _configStore = configStore;
        }
        #endregion

        #region Properties
        public string Head { get; } = "服务器端口设置";
        public string? GameName
        {
            get => _configStore.M2Config.GameName;
            set => SetProperty(_configStore.M2Config.GameName, value, _configStore, (m, v) => _configStore.M2Config.GameName = v);
        }


        #endregion

        #region Functions

        #endregion

        #region Commands
        #endregion
    }
}
