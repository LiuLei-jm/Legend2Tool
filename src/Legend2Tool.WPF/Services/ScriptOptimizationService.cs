using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.M2Config.M2Config;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.State;
using Microsoft.Data.Sqlite;
using Serilog;
using SQLitePCL;
using System.Data.OleDb;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Legend2Tool.WPF.Services
{
    public class ScriptOptimizationService : IScriptOptimizationService
    {
        private readonly ConfigStore _configStore;
        private readonly IEncodingService _encodingService;
        private readonly IFileService _fileService;
        private readonly ProgressStore _progressStore;
        private readonly ILogger _logger;

        private static readonly char[] _emptySeparator = [' ', '\t'];
        private static readonly string[] _lineSeparator = ["\r", "\n", "\r\n"];
        private const string _defaultPointRange = "50";
        private const string _startWriteTitle = ";XGD_动态刷怪生成开始";
        private const string _endWriteTitle = ";XGD_动态刷怪生成结束";

        private static readonly HashSet<string> _excludedTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "[@main]",
            "[@buy]",
            "[@makedrug]",
            "[@storage]",
            "[@s_repair]",
            "[@repair]",
            "[@sell]",
            "[@getback]",
            "[@upgradenow]",
            "[@getbackupgnow]"
        };

        private Dictionary<string, StdMode> _stdModes = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Monster> _monsters = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, MapData> _mapDatas = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, MapData> _mapInfos = new(StringComparer.OrdinalIgnoreCase);
        private List<NpcData> _npcDatas = [];
        private HashSet<string> _mapDescList = [];
        private HashSet<string> _visited = [];


        public ScriptOptimizationService(ConfigStore configStore, IEncodingService encodingService, IFileService fileService, ProgressStore progressStore, ILogger logger)
        {
            _configStore = configStore;
            _encodingService = encodingService;
            _fileService = fileService;
            _progressStore = progressStore;
            _logger = logger;
        }

        public async Task GenerateRefreshMonScriptAsync(RefreshOptimizationOptions options)
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
            var generateScriptTrigger = $@"@{options.RefreshMonTrigger}";
            var clearScriptTrigger = $@"@{options.ClearMonTrigger}";

            var generateScriptFiled = $@"@{options.RefreshMonTrigger}触发";
            var clearScriptFiled = $@"@{options.ClearMonTrigger}触发";

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
            var filterMapCodes = new HashSet<string>(options.FilterMapCode.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonNames = new HashSet<string>(options.FilterMonName.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonCounts = new HashSet<string>(options.FilterMonCount.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterIntervals = new HashSet<string>(options.FilterInterval.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonNameColors = new HashSet<string>(options.FilterMonNameColor.Split(_lineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var noClearMonLists = new HashSet<string>();

            var newMongen = new List<string>();


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
                    count *= options.RefreshMonMultiplier;
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

                if (options.IsCommentMongen)
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

            if (options.IsCommentMongen)
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", $"Mongen_{timestamp}.txt");

                File.Move(mongenPath, backupPath);
                await File.WriteAllLinesAsync(mongenPath, newMongen, mongenEncoding);
            }

            using (var refreshMonWriter = new StreamWriter(refreshMonScriptPath, false, mongenEncoding))
            {
                await refreshMonWriter.WriteLineAsync(_startWriteTitle);
                await refreshMonWriter.WriteLineAsync('{');
                await refreshMonWriter.WriteLineAsync($"[{generateScriptFiled}]");

                foreach (var map in mapMonsters)
                {
                    var mapCode = map.Key;
                    int count = mapMonsterCounts[mapCode];

                    await refreshMonWriter.WriteLineAsync("#If");
                    await refreshMonWriter.WriteLineAsync($"CheckMapHumanCount {mapCode} > 0");
                    switch (_configStore.EngineType)
                    {
                        case EngineType.BLUE:
                            await refreshMonWriter.WriteLineAsync($"!CheckMonMap {mapCode} {count}");
                            break;
                        case EngineType.GOM:
                            await refreshMonWriter.WriteLineAsync($"Not CheckMonMap {mapCode} {count}");
                            break;
                        default:
                            await refreshMonWriter.WriteLineAsync($"Not CheckMonMap {mapCode} {count} 1");
                            break;
                    }
                    await refreshMonWriter.WriteLineAsync("#Act");

                    foreach (var mongenex in map.Value)
                    {
                        await refreshMonWriter.WriteLineAsync(mongenex);
                    }

                    await refreshMonWriter.WriteLineAsync();
                }

                await refreshMonWriter.WriteLineAsync('}');
                await refreshMonWriter.WriteLineAsync(_endWriteTitle);
            }

            using (var clearMonWriter = new StreamWriter(clearMonScriptPath, false, mongenEncoding))
            {
                await clearMonWriter.WriteLineAsync(_startWriteTitle);
                await clearMonWriter.WriteLineAsync('{');
                await clearMonWriter.WriteLineAsync($"[{clearScriptFiled}]");

                foreach (var map in mapMonsters)
                {
                    var mapCode = map.Key;

                    await clearMonWriter.WriteLineAsync("#If");
                    await clearMonWriter.WriteLineAsync($"CheckMapHumanCount {mapCode} = 0");
                    await clearMonWriter.WriteLineAsync("#Act");
                    await clearMonWriter.WriteLineAsync($"ClearMapMon {mapCode}");

                    await clearMonWriter.WriteLineAsync();
                }

                await clearMonWriter.WriteLineAsync('}');
                await clearMonWriter.WriteLineAsync(_endWriteTitle);

            }

            using (var noClearMonListWriter = new StreamWriter(noClearMonListPath, true, mongenEncoding))
            {
                await noClearMonListWriter.WriteLineAsync();
                await noClearMonListWriter.WriteLineAsync(_startWriteTitle);

                foreach (var monName in noClearMonLists)
                {
                    await noClearMonListWriter.WriteLineAsync(monName);
                }

                await noClearMonListWriter.WriteLineAsync(_endWriteTitle);
            }

            using (var robotManageWriter = new StreamWriter(robotManagePath, true, robotManageEncoding))
            {
                await robotManageWriter.WriteLineAsync();
                await robotManageWriter.WriteLineAsync(_startWriteTitle);
                await robotManageWriter.WriteLineAsync($"[{generateScriptTrigger}]");
                await robotManageWriter.WriteLineAsync($@"#Call [\智能刷怪.txt] {generateScriptFiled}");
                if (options.IsClearMon)
                {
                    await robotManageWriter.WriteLineAsync($"[{clearScriptTrigger}]");
                    await robotManageWriter.WriteLineAsync($@"#Call [\智能清怪.txt] {clearScriptFiled}");
                }
                await robotManageWriter.WriteLineAsync(_endWriteTitle);
            }

            using (var autoRunRobotWriter = new StreamWriter(autoRunRobotPath, true, autoRunRobotEncoding))
            {
                var timeUnit = options.SelectedTimeUnit switch
                {
                    "分" => "MIN",
                    _ => "SEC"
                };
                await autoRunRobotWriter.WriteLineAsync();
                await autoRunRobotWriter.WriteLineAsync(_startWriteTitle);
                await autoRunRobotWriter.WriteLineAsync($@"#AutoRun NPC {timeUnit} {options.RefreshMonInterval} {generateScriptTrigger}");
                if (options.IsClearMon)
                {
                    await autoRunRobotWriter.WriteLineAsync($@"#AutoRun NPC {timeUnit} {options.ClearMonInterval} {clearScriptTrigger}");
                }
                await autoRunRobotWriter.WriteLineAsync(_endWriteTitle);
            }

        }

        public async Task ClearRefreshMonScriptAsync(RefreshOptimizationOptions options)
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
            var generateScriptTrigger = $@"@{options.RefreshMonTrigger}";
            var clearScriptTrigger = $@"@{options.ClearMonTrigger}";

            var generateScriptFiled = $@"@{options.RefreshMonTrigger}触发";
            var clearScriptFiled = $@"@{options.ClearMonTrigger}触发";

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

            if (options.IsCommentMongen)
            {
                var backupDir = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir");
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
                    throw new Exception($"还原Mongen.txt失败: {ex.Message}", ex);
                }
            }
            File.Delete(refreshMonScriptPath);
            File.Delete(clearMonScriptPath);
            await ClearScriptContentAsync(robotManagePath, robotManageEncoding);
            await ClearScriptContentAsync(autoRunRobotPath, autoRunRobotEncoding);
            await ClearScriptContentAsync(noClearMonListPath, noClearMonListEncoding);
        }

        private static async Task ClearScriptContentAsync(string path, Encoding encoding)
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
            await File.WriteAllLinesAsync(path, content, encoding);
        }

        public async Task<List<DuplicatedTriggerEntry>> DetectDuplicatedTriggerAsync()
        {
            List<string> files = GetScriptFiles();

            IProgress<ProgressStore> progress = new Progress<ProgressStore>(report =>
            {
                _progressStore.ProgressPercentage = report.ProgressPercentage;
                _progressStore.ProgressText = report.ProgressText;
            });
            _progressStore.ProgressPercentage = 0;
            _progressStore.ProgressText = string.Empty;

            int totalFiles = files.Count;
            int currentProgress = 0;

            var triggers = new HashSet<string>();
            var duplicatedEntries = new List<DuplicatedTriggerEntry>();

            var pattern = @"\[@(.*)\]";

            foreach (var filePath in files)
            {
                await Task.Run(async () =>
                {
                    var fileName = Path.GetFileName(filePath);
                    var fileEncoding = _encodingService.DetectFileEncoding(filePath);
                    try
                    {
                        var lines = await File.ReadAllLinesAsync(filePath, fileEncoding);
                        var currentTrigger = string.Empty;
                        var currentField = string.Empty;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var currentLine = lines[i].Trim();
                            if (!currentLine.Contains('@')) continue;
                            var match = Regex.Match(currentLine, pattern);
                            if (match.Success)
                            {
                                currentTrigger = match.Value;
                                currentField = currentTrigger.Substring(1, currentTrigger.Length - 2).ToLower();
                                if (_excludedTriggers.Contains(currentTrigger)) continue;
                                if (!triggers.Add(currentField))
                                {
                                    var entry = new DuplicatedTriggerEntry(currentField, fileName, filePath, i + 1, false);
                                    duplicatedEntries.Add(entry);
                                }
                                continue;
                            }
                            else
                            {
                                if (_excludedTriggers.Contains(currentTrigger)) continue;
                                if (!string.IsNullOrEmpty(currentField) && currentLine.EndsWith(currentField))
                                {
                                    var entry = new DuplicatedTriggerEntry(currentField, fileName, filePath, i + 1, true);
                                    duplicatedEntries.Add(entry);
                                }

                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        throw new IOException($"无法读取文件 '{filePath}': {ex.Message}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        throw new UnauthorizedAccessException($"无权限访问文件 '{filePath}': {ex.Message}");
                    }
                    finally
                    {
                        int updatedProgress = Interlocked.Increment(ref currentProgress);
                        _progressStore.ProgressPercentage = (int)((updatedProgress / (double)totalFiles) * 100);
                        _progressStore.ProgressText = $"{updatedProgress}/{totalFiles}：{Path.GetFileName(filePath)}";
                        progress.Report(_progressStore);
                    }
                });
            }
            _progressStore.ProgressText = $"检测完成，重复字段共计{duplicatedEntries.Count}个.";
            progress.Report(_progressStore);

            return duplicatedEntries;
        }

        public async Task OptimizingCallsAsync()
        {
            IProgress<ProgressStore> progress = new Progress<ProgressStore>(report =>
            {
                _progressStore.ProgressPercentage = report.ProgressPercentage;
                _progressStore.ProgressText = report.ProgressText;
            });
            _progressStore.ProgressPercentage = 0;
            _progressStore.ProgressText = string.Empty;

            List<string> files = GetScriptFiles();

            int totalFiles = files.Count;
            int currentProgress = 0;

            foreach (var filePath in files)
            {
                try
                {
                    var fileName = Path.GetFileName(filePath);
                    Encoding fileEncoding = _encodingService.DetectFileEncoding(filePath);
                    var newFileContent = new List<string>();
                    var callLines = new HashSet<string>();
                    var linesToPrepend = new List<string>();
                    bool isStart = !filePath.Contains("QFunction", StringComparison.OrdinalIgnoreCase)
                        && !filePath.Contains("QManage", StringComparison.OrdinalIgnoreCase)
                        && !filePath.Contains("RobotManage", StringComparison.OrdinalIgnoreCase);

                    if (isStart)
                    {
                        await foreach (var line in File.ReadLinesAsync(filePath, fileEncoding))
                        {
                            var trimedLine = line.TrimStart();

                            if (trimedLine.StartsWith("#Call", StringComparison.OrdinalIgnoreCase))
                            {
                                ExtractCallPathAndField(trimedLine, out string callPath, out string callField);
                                var callLine = $"{callPath}{callField}";
                                if (callLines.Add(callLine))
                                {
                                    newFileContent.Add(trimedLine);
                                }
                                else
                                {
                                    string oldLine = $";{trimedLine}";
                                    newFileContent.Add(oldLine);
                                    string newLine = $"Goto {callField}";
                                    newFileContent.Add(newLine);
                                }
                            }
                            else
                            {
                                newFileContent.Add(trimedLine);
                            }
                        }
                    }
                    else
                    {
                        await foreach (var line in File.ReadLinesAsync(filePath, fileEncoding))
                        {
                            var trimedLine = line.TrimStart();
                            if (!isStart)
                            {
                                if (trimedLine.StartsWith("[@", StringComparison.OrdinalIgnoreCase))
                                {
                                    isStart = true;
                                }
                                else
                                {
                                    newFileContent.Add(trimedLine);
                                    continue;
                                }
                            }

                            if (trimedLine.StartsWith("#call", StringComparison.OrdinalIgnoreCase))
                            {
                                ExtractCallPathAndField(trimedLine, out string callPath, out string callField);
                                var callLine = $"{callPath}{callField}";
                                if (callLines.Add(callLine))
                                {
                                    linesToPrepend.Add(trimedLine);
                                }
                                string oldLine = $";{trimedLine}";
                                newFileContent.Add(oldLine);
                                string newLine = $"Goto {callField}";
                                newFileContent.Add(newLine);
                            }
                            else
                            {
                                newFileContent.Add(trimedLine);
                            }
                        }
                    }

                    newFileContent.InsertRange(0, linesToPrepend);
                    await File.WriteAllLinesAsync(filePath, newFileContent, fileEncoding);
                }
                catch (FileNotFoundException ex)
                {
                    throw new FileNotFoundException($"文件 '{filePath}' 未找到: {ex.Message}", ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw new DirectoryNotFoundException($"目录未找到: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new UnauthorizedAccessException($"无权限访问文件 '{filePath}': {ex.Message}");
                }
                catch (IOException ex)
                {
                    throw new IOException($"无法读取文件 '{filePath}': {ex.Message}", ex);
                }
                catch (OutOfMemoryException ex)
                {
                    throw new OutOfMemoryException($"处理文件 '{filePath}' 时内存不足: {ex.Message}", ex);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException($"处理文件 '{filePath}' 时发生错误: {ex.Message}", ex);
                }
                catch (Exception)
                {
                    throw new Exception($"处理文件 '{filePath}' 时发生错误。请检查文件内容和格式是否正确。");
                }
                finally
                {
                    int updatedProgress = Interlocked.Increment(ref currentProgress);
                    _progressStore.ProgressPercentage = (int)((updatedProgress / (double)totalFiles) * 100);
                    _progressStore.ProgressText = $"{updatedProgress}/{totalFiles}：{Path.GetFileName(filePath)}";
                    progress.Report(_progressStore);
                }
            }

            _progressStore.ProgressText = $"脚本优化完成.";
            progress.Report(_progressStore);
        }

        private void ExtractCallPathAndField(string line, out string callPath, out string callField)
        {
            line = line.Trim();
            var startIndex = line.IndexOf('[');
            var endIndex = line.IndexOf(']');
            callPath = line[(startIndex + 1)..endIndex];
            while (callPath.StartsWith('\\'))
            {
                callPath = callPath[1..];
            }
            startIndex = line.IndexOf('@');
            if (startIndex != -1)
            {
                callField = line[startIndex..];
            }
            else
            {
                callField = string.Empty;
            }
        }

        public void OpenFile(DuplicatedTriggerEntry entry)
        {
            if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath))
            {
                MessageBox.Show($"文件 '{entry.FilePath}' 不存在或路径无效。");
                return;
            }
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = entry.FilePath,
                        Arguments = $"-n {entry.LineNumber}",
                        UseShellExecute = true
                    }
                };
                process.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开文件失败: {ex.Message}");
            }
        }
        private List<string> GetScriptFiles()
        {
            var mapQuestDefDirectory = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "MapQuest_Def");
            if (!Directory.Exists(mapQuestDefDirectory))
            {
                throw new DirectoryNotFoundException($"目录{mapQuestDefDirectory}没有找到");
            }
            var marketDefDirectory = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Market_Def");
            if (!Directory.Exists(marketDefDirectory))
            {
                throw new DirectoryNotFoundException($"目录{marketDefDirectory}没有找到");
            }
            var questDiaryDirectory = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary");
            if (!Directory.Exists(questDiaryDirectory))
            {
                throw new DirectoryNotFoundException($"目录{questDiaryDirectory}没有找到");
            }
            var robotDefDirectory = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_Def");
            if (!Directory.Exists(robotDefDirectory))
            {
                throw new DirectoryNotFoundException($"目录{robotDefDirectory}没有找到");
            }
            var files = _fileService.GetFiles(mapQuestDefDirectory, ["*.txt"], SearchOption.AllDirectories);
            files.AddRange(_fileService.GetFiles(marketDefDirectory, ["*.txt"], SearchOption.AllDirectories));
            files.AddRange(_fileService.GetFiles(questDiaryDirectory, ["*.txt"], SearchOption.AllDirectories));
            files.AddRange(_fileService.GetFiles(robotDefDirectory, ["*.txt"], SearchOption.AllDirectories));
            return files;
        }

        public async Task DropRateCalculatorAsync()
        {
            await ProcessDBDataAsync();
            await ProcessMapInfoAsync();
        }

        private async Task ProcessMapInfoAsync()
        {
            string filePath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "MapInfo.txt");
            Encoding fileEncoding = _encodingService.DetectFileEncoding(filePath);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"文件 '{filePath}' 未找到。请检查服务器目录设置。");
            }

            HashSet<string> UsedMaps = [];
            int index = 0;
            var mapPaths = new List<string>();

            // 获得基础路径
            await foreach (var line in File.ReadLinesAsync(filePath, fileEncoding))
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(';'))
                {
                    continue;
                }

                if (trimmedLine.StartsWith('['))
                {
                    if (!trimmedLine.Contains("]"))
                    {
                        throw new ArgumentException($"无效的地图信息格式: {trimmedLine}");
                    }

                    string mapString = trimmedLine.Substring(1, trimmedLine.IndexOf(']') - 1).Trim();

                    var mapParts = mapString.Split(_emptySeparator, StringSplitOptions.RemoveEmptyEntries);

                    if (mapParts.Length < 2)
                    {
                        throw new ArgumentException($"地图信息格式错误: {trimmedLine}");
                    }

                    string mapCode = mapParts[0].Trim();
                    string mapName = mapParts[1].Trim();
                    if (trimmedLine.Contains("FB")) mapCode = $"FB-{mapCode}";

                    if (mapCode.Contains('|'))
                    {
                        var codes = mapCode.Split('|');
                        mapCode = codes[0].Trim();
                        UsedMaps.Add(codes[1].Trim());
                    }
                    else
                    {
                        UsedMaps.Add(mapCode);
                    }

                    if (string.IsNullOrEmpty(mapCode) || string.IsNullOrEmpty(mapName))
                    {
                        throw new ArgumentException($"地图代码或名称不能为空: {trimmedLine}");
                    }
                    var mapData = new MapData
                    {
                        Id = index,
                        Name = mapName,
                        Code = mapCode
                    };

                    if (!_mapDatas.ContainsKey(mapData.Code))
                    {
                        _mapDatas[mapData.Code] = mapData;
                        index++;
                    }
                }
                else
                {
                    mapPaths.Add(trimmedLine.Trim());
                }
            }

            // 将基础路径添加到地图对象
            foreach (var mapPath in mapPaths)
            {
                var parts = mapPath.Split(_emptySeparator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    _logger.Warning($"地图路径格式错误: {mapPath}");
                    continue;
                }

                string fromMapCode;
                string fromMapName;
                string fromMapCoordinate;
                string symbol;
                string toMapCode;
                string toMapName;
                string toMapCoordinate;
                MapData fromMap;
                MapData toMap;

                if (parts.Length > 6)
                {
                    fromMapCode = parts[0];
                    if (!_mapDatas.TryGetValue(fromMapCode, out fromMap!))
                    {
                        _logger.Warning($"地图代码 '{fromMapCode}' 未找到，跳过路径: {mapPath}");
                        continue;
                    }
                    fromMapName = fromMap.Name!;
                    fromMapCoordinate = $"{parts[1]}:{parts[2]}";
                    symbol = parts[3];
                    toMapCode = parts[4];
                    if (!_mapDatas.TryGetValue(toMapCode, out toMap!))
                    {
                        _logger.Warning($"地图代码 '{toMapCode}' 未找到，跳过路径: {mapPath}");
                        continue;
                    }
                    toMapName = toMap.Name!;
                    toMapCoordinate = $"{parts[5]}:{parts[6]}";
                }
                else
                {
                    fromMapCode = parts[0];
                    if (!_mapDatas.TryGetValue(fromMapCode, out fromMap!))
                    {
                        _logger.Warning($"地图代码 '{fromMapCode}' 未找到，跳过路径: {mapPath}");
                        continue;
                    }
                    fromMapName = fromMap.Name!;
                    fromMapCoordinate = parts[1].Replace(',', ':');
                    symbol = parts[2];
                    toMapCode = parts[3];
                    if (!_mapDatas.TryGetValue(toMapCode, out toMap!))
                    {
                        _logger.Warning($"地图代码 '{toMapCode}' 未找到，跳过路径: {mapPath}");
                        continue;
                    }
                    toMapName = toMap.Name!;
                    toMapCoordinate = parts[4].Replace(',', ':');
                }

                string path = $"{fromMapName}({fromMapCode}:{fromMapCoordinate}){symbol}{toMapName}({toMapCode}:{toMapCoordinate})";

                string pathFlag = $"{fromMapCode}->{toMapCode}";
                string coordinate = fromMapCoordinate.Replace(':', ',');
                string mapDesc = $"{fromMapName},{coordinate},{toMapName},";

                AddPathToMapData(fromMapCode, toMap, path, pathFlag, mapDesc);
            }

            // 获取动态地图链接
            var files = GetScriptFiles();
            foreach (var file in files)
            {
                fileEncoding = _encodingService.DetectFileEncoding(file);
                await foreach (var line in File.ReadLinesAsync(file, fileEncoding))
                {
                    var trimedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimedLine) || trimedLine.StartsWith(';'))
                    {
                        continue;
                    }
                    ProcessAddMapGate(trimedLine);
                }
            }

            // 遍历所有地图，获取最佳路径
            foreach (var mapData in _mapDatas.Values)
            {
                GetBestPath(mapData);
            }
            _visited.Clear();

            foreach (var mapData in _mapDatas.Values)
            {
                GetBestPath(mapData);
            }
            _visited.Clear();

        }

        public void GetBestPath(MapData currentMap)
        {
            // 起点地图（没有来源）
            if (currentMap.FromMapList.Count == 0 || currentMap.IsMainCity || !_visited.Add(currentMap.Code!)) return;

            // 如果有多个来源地图，递归每一个，取最优路径
            List<string> bestPath = [];
            bool isMainCityFrom = false;

            foreach (var fromMapCode in currentMap.FromMapList)
            {
                if (!_mapDatas.TryGetValue(fromMapCode, out var fromMapData)) { continue; }
                GetBestPath(fromMapData);
                if (fromMapData.IsMainCity)
                {
                    foreach (var currentMapPath in currentMap.Path)
                    {
                        var (currentMapPathFromCode, currentMapPathToCode) = GetMapCodeForPath(currentMapPath);
                        if (fromMapData.Code!.Equals(currentMapPathFromCode))
                        {
                            bestPath = [currentMapPath];
                            currentMap.BestPath = bestPath;
                        }
                    }
                    break;
                }
                var path = fromMapData.BestPath.Count > 0 ? fromMapData.BestPath.ToList() : fromMapData.Path.ToList();
                var firstPath = path.FirstOrDefault();
                if (string.IsNullOrEmpty(firstPath)) continue;
                var endPath = path.LastOrDefault();
                if (string.IsNullOrEmpty(endPath)) continue;
                var (firstPathFromCode, firstPathToCode) = GetMapCodeForPath(firstPath);
                var (endPathFromCode, endPathToCode) = GetMapCodeForPath(endPath);
                if (!_mapDatas.TryGetValue(firstPathFromCode, out var firstFromMapData))
                {
                    _logger.Warning($"地图代码 '{firstPathFromCode}' 未找到，跳过路径: {firstPath}");
                    continue;
                }
                foreach (var currentMapPath in currentMap.Path)
                {
                    var (currentMapPathFromCode, currentMapPathToCode) = GetMapCodeForPath(currentMapPath);
                    if (endPathToCode.Equals(currentMapPathFromCode) && !path.Contains(currentMapPath))
                        path.Add(currentMapPath); // 加上当前地图的路径（从前一个地图来的路径）
                }
                if (bestPath.Count == 0 && firstFromMapData.Id < currentMap.Id) // 根据你对“最佳”的定义调整逻辑
                {
                    bestPath = path;
                    currentMap.BestPath = bestPath;
                    if (firstFromMapData.IsMainCity) isMainCityFrom = true;
                }
                else if (firstFromMapData.IsMainCity && path.Count < bestPath.Count)
                {
                    bestPath = path;
                    currentMap.BestPath = bestPath;
                }
                else if (!isMainCityFrom && firstFromMapData.IsMainCity)
                {
                    bestPath = path;
                    currentMap.BestPath = bestPath;
                    isMainCityFrom = true;
                }
                else if(!isMainCityFrom && firstFromMapData.Id < currentMap.Id)
                {
                    bestPath = path;
                    currentMap.BestPath = bestPath;
                    if (firstFromMapData.IsMainCity) isMainCityFrom = true;
                }
            }
        }

        private (string firstPathFromCode, string firstPathToCode) GetMapCodeForPath(string path)
        {
            string[] pathSegments = path.Split(new string[] { "->" }, StringSplitOptions.None);
            if (pathSegments.Length < 2)
            {
                return (string.Empty, string.Empty);
            }
            var pathFrom = pathSegments[0];
            var pathTo = pathSegments[1];

            var pathFromMapCode = pathFrom.Substring(pathFrom.IndexOf('(') + 1, pathFrom.IndexOf(':') - pathFrom.IndexOf('(') - 1);
            var pathToMapCode = pathTo.Substring(pathTo.IndexOf('(') + 1, pathTo.IndexOf(':') - pathTo.IndexOf('(') - 1);
            return (pathFromMapCode, pathToMapCode);
        }

        private void AddPathFromPreviousMap(MapData mapData, HashSet<string> visited)
        {
            if (mapData.FromMapList.Count == 0 || mapData.IsMainCity || !visited.Add(mapData.Code!)) return;

            foreach (var fromMapCode in mapData.FromMapList)
            {
                if (!_mapDatas.TryGetValue(fromMapCode, out var fromMapData)) continue;
                AddPathFromPreviousMap(fromMapData, visited);
                if (fromMapData.IsMainCity) continue;
                var pathList = fromMapData.Path.ToList();
                if (pathList[0] == "没有找到")
                {
                    continue;
                }
            }
        }

        private void ProcessAddMapGate(string trimedLine)
        {
            if (trimedLine.StartsWith("addmapgate", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimedLine.Split(_emptySeparator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9)
                {
                    _logger.Warning($"addmapgate格式错误: {trimedLine}");
                    return;
                }
                var fromMapCode = parts[2];
                if (!_mapDatas.TryGetValue(fromMapCode, out var fromMap))
                {
                    _logger.Warning($"地图代码 '{fromMapCode}' 未找到，跳过addmapgate: {trimedLine}");
                    return;
                }
                if (!int.TryParse(parts[3], out _)) parts[3] = "-1";
                if (!int.TryParse(parts[4], out _)) parts[4] = "-1";
                var fromCoordinate = $"{parts[3]}:{parts[4]}";
                var symbol = "->";
                var toMapCode = parts[6];
                if (!_mapDatas.TryGetValue(toMapCode, out var toMap))
                {
                    _logger.Warning($"地图代码 '{toMapCode}' 未找到，跳过addmapgate: {trimedLine}");
                    return;
                }
                if (!int.TryParse(parts[7], out _)) parts[7] = "-1";
                if (!int.TryParse(parts[8], out _)) parts[8] = "-1";
                var toCoordinate = $"{parts[7]}:{parts[8]}";
                string path = $"{fromMap.Name}({fromMapCode}:{fromCoordinate}){symbol}{toMap.Name}({toMapCode}:{toCoordinate})";
                string pathFlag = $"{fromMapCode}->{toMapCode}";
                string coordinate = fromCoordinate.Replace(':', ',');
                string mapDesc = $"{fromMap.Name},{coordinate},{toMap.Name},";
                AddPathToMapData(fromMapCode, toMap, path, pathFlag, mapDesc);
            }
        }

        private void AddPathToMapData(string fromMapCode, MapData toMap, string path, string pathFlag, string mapDesc)
        {
            toMap.FromMapList.Add(fromMapCode);
            if (!toMap.AddedPath.Add(pathFlag)) return;
            if (toMap.Path[0] == "没有找到")
            {
                toMap.Path[0] = path;
            }
            else if (!toMap.Path.Contains(path))
            {
                toMap.Path.Add(path);
                if (toMap.Path.Count > 9) toMap.IsMainCity = true;
            }
            _mapDescList.Add(mapDesc);
        }

        #region 读取DB数据
        private async Task ProcessDBDataAsync()
        {
            string? dbPath;
            string? connectionString;
            string? stdmodeQuery;
            string? monsterQuery;

            switch (_configStore.EngineType)
            {
                case EngineType.BLUE:
                    var blueConfig = _configStore.M2Config as BLUEConfig;
                    dbPath = blueConfig!.DataTableFile!;
                    connectionString = $"Data Source={dbPath};";
                    stdmodeQuery = "SELECT ClassID, Name, StdMode FROM item";
                    monsterQuery = "SELECT Name FROM monster";
                    break;
                case EngineType.GOM:
                    var gomConfig = _configStore.M2Config as GOMConfig;
                    dbPath = gomConfig!.AccessFileName!;
                    connectionString =
                        $@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={dbPath};Persist Security Info=False;";
                    stdmodeQuery = "SELECT Idx, Name, StdMode FROM StdItems";
                    monsterQuery = "SELECT Name FROM Monster";
                    break;
                default:
                    var geeConfig = _configStore.M2Config as GEEConfig;
                    dbPath = geeConfig!.SqliteDBName!;
                    connectionString = $"Data Source={dbPath};";
                    stdmodeQuery = "SELECT Idx, Name, StdMode FROM StdItems";
                    monsterQuery = "SELECT Name FROM Monster";
                    break;
            }

            if (string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(connectionString))
            {
                var stackTrace = new System.Diagnostics.StackTrace(true);
                var ex = new Exception($"{dbPath} 不存在.\n{stackTrace}");
                return;
            }

            await Task.Run(() =>
            {
                if (_configStore.EngineType.Equals(EngineType.GOM))
                {
                    _stdModes = GetAccessStdList(connectionString, stdmodeQuery);
                    _monsters = GetAccessMonList(connectionString, monsterQuery);
                }
                else
                {
                    _stdModes = GetSqliteStdList(connectionString, stdmodeQuery);
                    _monsters = GetSqliteMonList(connectionString, monsterQuery);
                }
            });
        }

        private Dictionary<string, Monster> GetSqliteMonList(string connectionString, string monsterQuery)
        {
            var monsters = new Dictionary<string, Monster>(StringComparer.OrdinalIgnoreCase);
            using SqliteConnection connection = new SqliteConnection(connectionString);
            Batteries_V2.Init(); // Correct initialization for SQLitePCL
            connection.Open();
            using SqliteCommand command = new SqliteCommand(monsterQuery, connection);
            using SqliteDataReader reader = command.ExecuteReader();
            int index = 0;
            while (reader.Read())
            {
                var monster = new Monster
                {
                    Id = index,
                    Name = Convert.ToString(reader["Name"])?.Trim() ?? string.Empty,
                };
                if (string.IsNullOrEmpty(monster.Name) || monsters.ContainsKey(monster.Name))
                {
                    continue;
                }
                monsters[monster.Name] = monster;
                index++;
            }
            return monsters;
        }

        private Dictionary<string, StdMode> GetSqliteStdList(string connectionString, string stdmodeQuery)
        {
            var stdmodes = new Dictionary<string, StdMode>(StringComparer.OrdinalIgnoreCase);
            using SqliteConnection connection = new SqliteConnection(connectionString);

            Batteries_V2.Init(); // Correct initialization for SQLitePCL
            connection.Open();
            using SqliteCommand command = new SqliteCommand(stdmodeQuery, connection);
            using SqliteDataReader reader = command.ExecuteReader();
            int index = 0;
            while (reader.Read())
            {
                var std = new StdMode
                {
                    Id = index,
                    Name = Convert.ToString(reader["Name"])?.Trim() ?? string.Empty,
                    Type = string.IsNullOrEmpty(reader["StdMode"].ToString()) ? "0" : reader["StdMode"].ToString()!.Trim()
                };
                if (string.IsNullOrEmpty(std.Name) || stdmodes.ContainsKey(std.Name))
                {
                    continue;
                }
                stdmodes[std.Name] = std;
                index++;
            }
            return stdmodes;
        }

        private Dictionary<string, Monster> GetAccessMonList(string connectionString, string monsterQuery)
        {
            var monsters = new Dictionary<string, Monster>(StringComparer.OrdinalIgnoreCase);
            using var connection = new OleDbConnection(connectionString);
            connection.Open();
            using var command = new OleDbCommand(monsterQuery, connection);
            using var reader = command.ExecuteReader();
            int index = 0;
            while (reader.Read())
            {
                var monster = new Monster
                {
                    Id = index,
                    Name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim()
                };
                if (string.IsNullOrEmpty(monster.Name) || monsters.ContainsKey(monster.Name))
                {
                    continue;
                }
                monsters[monster.Name] = monster;
                index++;
            }
            return monsters;
        }

        private Dictionary<string, StdMode> GetAccessStdList(string connectionString, string stdmodeQuery)
        {
            var stdmodes = new Dictionary<string, StdMode>(StringComparer.OrdinalIgnoreCase);
            using var connection = new OleDbConnection(connectionString);
            connection.Open();
            using var command = new OleDbCommand(stdmodeQuery, connection);
            using var reader = command.ExecuteReader();
            int index = 0;
            while (reader.Read())
            {
                var std = new StdMode
                {
                    Id = index,
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                    Type = reader.IsDBNull(2) ? "0" : reader.GetString(2).Trim()
                };
                if (string.IsNullOrEmpty(std.Name) || stdmodes.ContainsKey(std.Name))
                {
                    continue;
                }
                stdmodes[std.Name] = std;
                index++;
            }
            return stdmodes;
        }
        #endregion
    }
}
