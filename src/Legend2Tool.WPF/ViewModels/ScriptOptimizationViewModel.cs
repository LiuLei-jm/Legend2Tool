using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Services;
using Legend2Tool.WPF.State;
using Serilog;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using System.Windows;
using MessageBox = HandyControl.Controls.MessageBox;

namespace Legend2Tool.WPF.ViewModels
{
    public partial class ScriptOptimizationViewModel : ViewModelBase
    {
        private readonly ConfigStore _configStore;
        private readonly IEncodingService _encodingService;
        private readonly ILogger _logger;
        private static readonly char[] _emptySeparator = [' ', '\t'];
        private static readonly string[] _lineSeparator = ["\r", "\n", "\r\n"];
        private const string _defaultPointRange = "50";
        private const string _startWriteTitle = ";XGD_动态刷怪生成开始";
        private const string _endWriteTitle = ";XGD_动态刷怪生成结束";

        [ObservableProperty]
        string _filterMapCode = "0\r1\r2\r3\r4\r5\r6\r11\r12\r";
        [ObservableProperty]
        string _filterMonName = "弓箭手\r弓箭守卫\r虎卫\r鹰卫\r刀卫\r卫士\r带刀护卫\r";
        [ObservableProperty]
        string _filterMonCount = "1\r";
        [ObservableProperty]
        string _filterInterval = string.Empty;
        [ObservableProperty]
        string _filterMonNameColor = string.Empty;
        [ObservableProperty]
        string _selectedTimeUnit = "分";
        [ObservableProperty]
        int _refreshMonInterval = 1;
        [ObservableProperty]
        int _clearMonInterval = 15;
        [ObservableProperty]
        int _refreshMonMultiplier = 1;
        [ObservableProperty]
        [Required(ErrorMessage = "请填写刷怪触发器名称")]
        string _refreshMonTrigger = "XGD_动态刷怪";
        [ObservableProperty]
        [Required(ErrorMessage = "请填写清怪触发器名称")]
        string _clearMonTrigger = "XGD_动态清怪";
        [ObservableProperty]
        bool _isClearMon;
        [ObservableProperty]
        bool _isCommentMongen;
        public string Head { get; } = "服务器脚本优化";

        public ScriptOptimizationViewModel(ConfigStore configStore, IEncodingService encodingService, ILogger logger)
        {
            _configStore = configStore;
            _encodingService = encodingService;
            _logger = logger;
        }

        [RelayCommand]
        void GenerateRefreshMonScript()
        {
            ValidateAllProperties();

            var mongenPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "MonGen.txt");
            if (!File.Exists(mongenPath))
            {
                MessageBox.Show("MonGen.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            var mongenEncoding = _encodingService.DetectFileEncoding(mongenPath);
            var robotManagePath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_def", "RobotManage.txt");
            if (!File.Exists(robotManagePath))
            {
                MessageBox.Show("RobotManage.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            var robotManageEncoding = _encodingService.DetectFileEncoding(robotManagePath);
            var generateScriptTrigger = $@"@{RefreshMonTrigger}";
            var clearScriptTrigger = $@"@{ClearMonTrigger}";

            var generateScriptFiled = $@"@{RefreshMonTrigger}触发";
            var clearScriptFiled = $@"@{ClearMonTrigger}触发";

            if (File.ReadAllText(robotManagePath, robotManageEncoding).Contains(generateScriptTrigger))
            {
                MessageBox.Show($@"刷怪触发器已存在'{generateScriptTrigger}'，如果要重新生成脚本请先清除现有脚本。");
                return;
            }

            var autoRunRobotPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_def", "AutoRunRobot.txt");
            if (!File.Exists(autoRunRobotPath))
            {
                MessageBox.Show("AutoRunRobot.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            var autoRunRobotEncoding = _encodingService.DetectFileEncoding(autoRunRobotPath);

            var noClearMonListPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "NoClearMonList.txt");

            var refreshMonScriptPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary", "智能刷怪.txt");
            var clearMonScriptPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary", "智能清怪.txt");


            var mapMonsters = new Dictionary<string, List<string>>();
            var mapMonsterCounts = new Dictionary<string, int>();
            var filterMapCodes = new HashSet<string>(FilterMapCode.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonNames = new HashSet<string>(FilterMonName.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonCounts = new HashSet<string>(FilterMonCount.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterIntervals = new HashSet<string>(FilterInterval.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonNameColors = new HashSet<string>(FilterMonNameColor.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var noClearMonLists = new HashSet<string>();

            var newMongen = new List<string>();

            try
            {
                foreach (var line in File.ReadLines(mongenPath, mongenEncoding))
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(';'))
                    {
                        newMongen.Add(trimmedLine);
                        continue;
                    }

                    var parts = trimmedLine.Split(_emptySeparator, StringSplitOptions.RemoveEmptyEntries);

                    // 怪物名称
                    string monName = parts.Length > 3 ? parts[3] : string.Empty;
                    if (string.IsNullOrEmpty(monName) || filterMonNames.Contains(monName))
                    {
                        noClearMonLists.Add(monName);
                        newMongen.Add(trimmedLine);
                        continue;
                    }

                    // 地图代码
                    string mapCode = parts.Length > 0 ? parts[0] : string.Empty;
                    if (string.IsNullOrEmpty(mapCode) || filterMapCodes.Contains(mapCode))
                    {
                        noClearMonLists.Add(monName);
                        newMongen.Add(trimmedLine);
                        continue;
                    }

                    // 地图X坐标
                    string pointX = parts.Length > 1 ? parts[1] : _defaultPointRange;
                    if (int.TryParse(pointX, out int x))
                    {
                        if (x < 0) pointX = _defaultPointRange;
                    }
                    else
                    {
                        newMongen.Add(trimmedLine);
                        continue;
                    }


                    // 地图Y坐标
                    string pointY = parts.Length > 2 ? parts[2] : _defaultPointRange;
                    if (int.TryParse(pointY, out int y))
                    {
                        if (y < 0) pointY = _defaultPointRange;
                    }
                    else
                    {
                        newMongen.Add(trimmedLine);
                        continue;
                    }


                    // 刷新范围
                    string range = parts.Length > 4 ? parts[4] : _defaultPointRange;
                    if (int.TryParse(range, out int r))
                    {
                        if (r < 0) range = _defaultPointRange;
                    }
                    else
                    {
                        newMongen.Add(trimmedLine);
                        continue;
                    }

                    // 刷怪数量
                    string monCount = parts.Length > 5 ? parts[5] : "1";
                    if (filterMonCounts.Contains(monCount))
                    {
                        noClearMonLists.Add(monName);
                        newMongen.Add(trimmedLine);
                        continue;
                    }
                    if (int.TryParse(monCount, out int count))
                    {
                        if (count < 1)
                        {
                            count = 1;
                        }
                        count *= RefreshMonMultiplier;
                        monCount = count.ToString();
                    }
                    else continue;


                    // 刷新间隔
                    string interval = parts.Length > 6 ? parts[6] : "1";
                    if (string.IsNullOrEmpty(interval) || filterIntervals.Contains(interval) || !int.TryParse(interval, out _))
                    {
                        noClearMonLists.Add(monName);
                        newMongen.Add(trimmedLine);
                        continue;
                    }

                    // 怪物名称颜色
                    string monNameColor = parts.Length > 8 ? parts[8] : "255";
                    if (string.IsNullOrEmpty(monNameColor) || filterMonNameColors.Contains(monNameColor))
                    {
                        noClearMonLists.Add(monName);
                        newMongen.Add(trimmedLine);
                        continue;
                    }

                    if (IsCommentMongen)
                    {
                        newMongen.Add($";{trimmedLine}");
                    }
                    else
                    {
                        newMongen.Add(trimmedLine);
                    }

                    string mongenexScript = _configStore.EngineType switch
                    {
                        EngineType.GOM => $"Mongenex {mapCode} {pointX} {pointY} {monName} {range} {monCount} 0 {monNameColor}",
                        _ => $"Mongenex {mapCode} {pointX} {pointY} {monName} {range} {monCount} {monNameColor}"
                    };

                    if (!mapMonsters.TryGetValue(mapCode, out _))
                    {
                        mapMonsters[mapCode] = [];
                        mapMonsterCounts[mapCode] = 0;
                    }

                    mapMonsters[mapCode].Add(mongenexScript);
                    mapMonsterCounts[mapCode] += count;
                }

                if (IsCommentMongen)
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var backupPath = Path.Combine(_configStore.ServerDirectory, "Mir200","Envir", $"Mongen_{timestamp}.txt");

                    File.Move(mongenPath, backupPath);
                    File.WriteAllLines(mongenPath, newMongen, mongenEncoding);
                }

                using (var refreshMonWriter = new StreamWriter(refreshMonScriptPath, false, mongenEncoding))
                {
                    refreshMonWriter.WriteLine(_startWriteTitle);
                    refreshMonWriter.WriteLine('{');
                    refreshMonWriter.WriteLine($"[{generateScriptFiled}]");

                    foreach (var map in mapMonsters)
                    {
                        var mapCode = map.Key;
                        int count = mapMonsterCounts[mapCode];

                        refreshMonWriter.WriteLine("#If");
                        refreshMonWriter.WriteLine($"CheckMapHumanCount {mapCode} > 0");
                        switch (_configStore.EngineType)
                        {
                            case EngineType.BLUE:
                                refreshMonWriter.WriteLine($"!CheckMonMap {mapCode} {count}");
                                break;
                            case EngineType.GOM:
                                refreshMonWriter.WriteLine($"Not CheckMonMap {mapCode} {count}");
                                break;
                            default:
                                refreshMonWriter.WriteLine($"Not CheckMonMap {mapCode} {count} 1");
                                break;
                        }
                        refreshMonWriter.WriteLine("#Act");

                        foreach (var mongenex in map.Value)
                        {
                            refreshMonWriter.WriteLine(mongenex);
                        }

                        refreshMonWriter.WriteLine();
                    }

                    refreshMonWriter.WriteLine('}');
                    refreshMonWriter.WriteLine(_endWriteTitle);
                }

                using (var clearMonWriter = new StreamWriter(clearMonScriptPath, false, mongenEncoding))
                {
                    clearMonWriter.WriteLine(_startWriteTitle);
                    clearMonWriter.WriteLine('{');
                    clearMonWriter.WriteLine($"[{clearScriptFiled}]");

                    foreach (var map in mapMonsters)
                    {
                        var mapCode = map.Key;

                        clearMonWriter.WriteLine("#If");
                        clearMonWriter.WriteLine($"CheckMapHumanCount {mapCode} = 0");
                        clearMonWriter.WriteLine("#Act");
                        clearMonWriter.WriteLine($"ClearMapMon {mapCode}");

                        clearMonWriter.WriteLine();
                    }

                    clearMonWriter.WriteLine('}');
                    clearMonWriter.WriteLine(_endWriteTitle);

                }

                using (var noClearMonListWriter = new StreamWriter(noClearMonListPath, true, mongenEncoding))
                {
                    noClearMonListWriter.WriteLine();
                    noClearMonListWriter.WriteLine(_startWriteTitle);

                    foreach (var monName in noClearMonLists)
                    {
                        noClearMonListWriter.WriteLine(monName);
                    }

                    noClearMonListWriter.WriteLine(_endWriteTitle);
                }

                using (var robotManageWriter = new StreamWriter(robotManagePath, true, robotManageEncoding))
                {
                    robotManageWriter.WriteLine();
                    robotManageWriter.WriteLine(_startWriteTitle);
                    robotManageWriter.WriteLine($"[{generateScriptTrigger}]");
                    robotManageWriter.WriteLine($@"#Call [\QuestDiary\智能刷怪.txt] {generateScriptFiled}");
                    if (IsClearMon)
                    {
                        robotManageWriter.WriteLine($"[{clearScriptTrigger}]");
                        robotManageWriter.WriteLine($@"#Call [\QuestDiary\智能清怪.txt] {clearScriptFiled}");
                    }
                    robotManageWriter.WriteLine(_endWriteTitle);
                }

                using (var autoRunRobotWriter = new StreamWriter(autoRunRobotPath, true, autoRunRobotEncoding))
                {
                    var timeUnit = SelectedTimeUnit switch
                    {
                        "分" => "MIN",
                        _ => "SEC"
                    };
                    autoRunRobotWriter.WriteLine();
                    autoRunRobotWriter.WriteLine(_startWriteTitle);
                    autoRunRobotWriter.WriteLine($@"#AutoRun NPC {timeUnit} {RefreshMonInterval} {generateScriptTrigger}");
                    if (IsClearMon)
                    {
                        autoRunRobotWriter.WriteLine($@"#AutoRun NPC {timeUnit} {ClearMonInterval} {clearScriptTrigger}");
                    }
                    autoRunRobotWriter.WriteLine(_endWriteTitle);
                }
                Growl.Success("脚本生成成功！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"生成脚本时发生错误:{ex.Message}");
                Growl.Error("生成脚本时发生错误，请检查输入参数是否正确。");
                return;
            }
        }

        [RelayCommand]
        void ClearRefreshMonScript()
        {
            var mongenPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "MonGen.txt");
            if (!File.Exists(mongenPath))
            {
                MessageBox.Show("MonGen.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            var mongenEncoding = _encodingService.DetectFileEncoding(mongenPath);
            var robotManagePath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_def", "RobotManage.txt");
            if (!File.Exists(robotManagePath))
            {
                MessageBox.Show("RobotManage.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            var robotManageEncoding = _encodingService.DetectFileEncoding(robotManagePath);
            var generateScriptTrigger = $@"@{RefreshMonTrigger}";
            var clearScriptTrigger = $@"@{ClearMonTrigger}";

            var generateScriptFiled = $@"@{RefreshMonTrigger}触发";
            var clearScriptFiled = $@"@{ClearMonTrigger}触发";

            var autoRunRobotPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_def", "AutoRunRobot.txt");
            if (!File.Exists(autoRunRobotPath))
            {
                MessageBox.Show("AutoRunRobot.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            var autoRunRobotEncoding = _encodingService.DetectFileEncoding(autoRunRobotPath);

            var noClearMonListPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "NoClearMonList.txt");

            var noClearMonListEncoding = _encodingService.DetectFileEncoding(noClearMonListPath);

            var refreshMonScriptPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary", "智能刷怪.txt");
            var clearMonScriptPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary", "智能清怪.txt");

            if (IsCommentMongen)
            {
                var backupDir = Path.Combine(_configStore.ServerDirectory, "Mir200","Envir");
                var backupFiles = Directory.GetFiles(backupDir, "Mongen_*.txt");
                if (backupFiles.Length == 0)
                {
                    MessageBox.Show("未找到任何Mongen.txt备份文件。");
                    return;
                }

                // 找到最新的备份文件
                var latestBackup = backupFiles
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .First();

                try
                {
                    File.Copy(latestBackup, mongenPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"还原Mongen.txt失败:{ex.Message}");
                    Growl.Error("还原Mongen.txt失败，请检查文件权限或是否被占用。");
                }
            }


            try
            {
                File.Delete(refreshMonScriptPath);
                File.Delete(clearMonScriptPath);
                ClearScriptContent(robotManagePath, robotManageEncoding);
                ClearScriptContent(autoRunRobotPath, autoRunRobotEncoding);
                ClearScriptContent(noClearMonListPath, noClearMonListEncoding);
                Growl.Success("脚本清除成功！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"清除脚本时发生错误:{ex.Message}");
                Growl.Error("清除脚本时发生错误，请检查文件是否被占用。");
                return;
            }
        }
        private static void ClearScriptContent(string path, Encoding encoding)
        {
            var isDeleteContent = false;
            var content = new List<string>();
            foreach (var line in File.ReadLines(path, encoding))
            {
                if (line.Contains(_startWriteTitle))
                {
                    isDeleteContent = true;
                    break;
                }
                if (line.Contains(_endWriteTitle))
                {
                    isDeleteContent = false;
                }
                if (isDeleteContent)
                {
                    continue;
                }
                content.Add(line);
            }
            File.WriteAllLines(path, content, encoding);
        }
    }
}
