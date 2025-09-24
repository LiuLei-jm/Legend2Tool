using Legend2Tool.WPF.Commons;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.State;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Legend2Tool.WPF.Services
{
    public class DynamicMonsterSpawningService : IDynamicMonsterSpawningService
    {
        private readonly ConfigStore _configStore;
        private readonly IEncodingService _encodingService;
        public DynamicMonsterSpawningService(ConfigStore configStore, IEncodingService encodingService)
        {
            _configStore = configStore;
            _encodingService = encodingService;
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
            var filterMapCodes = new HashSet<string>(options.FilterMapCode.Split(AppConstants.LineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonNames = new HashSet<string>(options.FilterMonName.Split(AppConstants.LineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonCounts = new HashSet<string>(options.FilterMonCount.Split(AppConstants.LineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterIntervals = new HashSet<string>(options.FilterInterval.Split(AppConstants.LineSeparator, StringSplitOptions.RemoveEmptyEntries));
            var filterMonNameColors = new HashSet<string>(options.FilterMonNameColor.Split(AppConstants.LineSeparator, StringSplitOptions.RemoveEmptyEntries));
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

                var parts = trimmedLine.Split(AppConstants.EmptySeparator, StringSplitOptions.RemoveEmptyEntries);

                // 怪物名称
                string monName = parts.Length > 3 ? parts[3] : string.Empty;
                if (string.IsNullOrEmpty(monName) || filterMonNames.Contains(monName))
                {
                    noClearMonLists.Add(monName);
                    newMongen.Add(trimmedLine);
                    continue;
                }

                // 刷新间隔
                string interval = parts.Length > 6 ? parts[6] : options.MaxRefreshInterval.ToString();
                if (string.IsNullOrEmpty(interval) || filterIntervals.Contains(interval) || !int.TryParse(interval, out _))
                {
                    noClearMonLists.Add(monName);
                    newMongen.Add(trimmedLine);
                    continue;
                }

                if (options.IsLimitRefreshInterval && int.TryParse(interval, out int refreshInterval))
                {
                    if (refreshInterval > options.MaxRefreshInterval && parts.Length > 6)
                    {
                        parts[6] = options.MaxRefreshInterval.ToString();
                        trimmedLine = string.Join(' ', parts);
                    }
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
                string pointX = parts.Length > 1 ? parts[1] : AppConstants.DefaultPointRange;
                if (int.TryParse(pointX, out int x))
                {
                    if (x < 0) pointX = AppConstants.DefaultPointRange;
                }
                else
                {
                    newMongen.Add(trimmedLine);
                    continue;
                }


                // 地图Y坐标
                string pointY = parts.Length > 2 ? parts[2] : AppConstants.DefaultPointRange;
                if (int.TryParse(pointY, out int y))
                {
                    if (y < 0) pointY = AppConstants.DefaultPointRange;
                }
                else
                {
                    newMongen.Add(trimmedLine);
                    continue;
                }


                // 刷新范围
                string range = parts.Length > 4 ? parts[4] : AppConstants.DefaultPointRange;
                if (int.TryParse(range, out int r))
                {
                    if (r < 0) range = AppConstants.DefaultPointRange;
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
                    else if (count > options.MaxRefreshCount)
                    {
                        count = options.MaxRefreshCount;
                    }
                    count *= options.RefreshMonMultiplier;
                    monCount = count.ToString();
                }
                else continue;



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
                    EngineType.GOM => $"MonGenEX {mapCode} {pointX} {pointY} {monName} {range} {monCount} 0 {monNameColor}",
                    _ => $"MonGenEX {mapCode} {pointX} {pointY} {monName} {range} {monCount} {monNameColor}"
                };

                if (!mapMonsters.TryGetValue(mapCode, out _))
                {
                    mapMonsters[mapCode] = [];
                    mapMonsterCounts[mapCode] = 0;
                }

                mapMonsters[mapCode].Add(mongenexScript);
                mapMonsterCounts[mapCode] += count;
            }

            if (options.IsCommentMongen || options.IsLimitRefreshInterval)
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", $"Mongen_{timestamp}.txt");

                File.Move(mongenPath, backupPath);
                await File.WriteAllLinesAsync(mongenPath, newMongen, mongenEncoding);
            }

            using (var refreshMonWriter = new StreamWriter(refreshMonScriptPath, false, mongenEncoding))
            {
                await refreshMonWriter.WriteLineAsync(AppConstants.StartWriteTitle);
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
                await refreshMonWriter.WriteLineAsync(AppConstants.EndWriteTitle);
            }

            using (var clearMonWriter = new StreamWriter(clearMonScriptPath, false, mongenEncoding))
            {
                await clearMonWriter.WriteLineAsync(AppConstants.StartWriteTitle);
                await clearMonWriter.WriteLineAsync('{');
                await clearMonWriter.WriteLineAsync($"[{clearScriptFiled}]");

                foreach (var map in mapMonsters)
                {
                    var mapCode = map.Key;

                    await clearMonWriter.WriteLineAsync("#If");
                    await clearMonWriter.WriteLineAsync($"CheckMapHumanCount {mapCode} < 1");
                    await clearMonWriter.WriteLineAsync("#Act");
                    await clearMonWriter.WriteLineAsync($"ClearMapMon {mapCode}");

                    await clearMonWriter.WriteLineAsync();
                }

                await clearMonWriter.WriteLineAsync('}');
                await clearMonWriter.WriteLineAsync(AppConstants.EndWriteTitle);

            }

            using (var noClearMonListWriter = new StreamWriter(noClearMonListPath, true, mongenEncoding))
            {
                foreach (var monName in noClearMonLists)
                {
                    if (string.IsNullOrEmpty(monName)) continue;
                    await noClearMonListWriter.WriteLineAsync(monName);
                }

            }

            using (var robotManageWriter = new StreamWriter(robotManagePath, true, robotManageEncoding))
            {
                await robotManageWriter.WriteLineAsync();
                await robotManageWriter.WriteLineAsync(AppConstants.StartWriteTitle);
                await robotManageWriter.WriteLineAsync($"[{generateScriptTrigger}]");
                await robotManageWriter.WriteLineAsync($@"#Call [\智能刷怪.txt] {generateScriptFiled}");
                if (options.IsClearMon)
                {
                    await robotManageWriter.WriteLineAsync($"[{clearScriptTrigger}]");
                    await robotManageWriter.WriteLineAsync($@"#Call [\智能清怪.txt] {clearScriptFiled}");
                }
                await robotManageWriter.WriteLineAsync(AppConstants.EndWriteTitle);
            }

            using (var autoRunRobotWriter = new StreamWriter(autoRunRobotPath, true, autoRunRobotEncoding))
            {
                var timeUnit = options.SelectedTimeUnit switch
                {
                    "分" => "MIN",
                    _ => "SEC"
                };
                await autoRunRobotWriter.WriteLineAsync();
                await autoRunRobotWriter.WriteLineAsync(AppConstants.StartWriteTitle);
                await autoRunRobotWriter.WriteLineAsync($@"#AutoRun NPC {timeUnit} {options.RefreshMonInterval} {generateScriptTrigger}");
                if (options.IsClearMon)
                {
                    await autoRunRobotWriter.WriteLineAsync($@"#AutoRun NPC {timeUnit} {options.ClearMonInterval} {clearScriptTrigger}");
                }
                await autoRunRobotWriter.WriteLineAsync(AppConstants.EndWriteTitle);
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

            var generateScriptField = $@"@{options.RefreshMonTrigger}触发";
            var clearScriptField = $@"@{options.ClearMonTrigger}触发";

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
            await ClearScriptContentAsync(robotManagePath, robotManageEncoding, options);
            await ClearScriptContentAsync(autoRunRobotPath, autoRunRobotEncoding, options);
            await File.WriteAllTextAsync(noClearMonListPath, string.Empty);
        }
        private static async Task ClearScriptContentAsync(string path, Encoding encoding, RefreshOptimizationOptions options)
        {
            var isDeleteContent = false;
            var content = new List<string>();
            await foreach (var line in File.ReadLinesAsync(path, encoding))
            {

                if (line.Contains(AppConstants.StartWriteTitle))
                {
                    isDeleteContent = true;
                    continue;
                }
                else if (line.Contains(AppConstants.EndWriteTitle))
                {
                    isDeleteContent = false;
                    continue;
                }
                else if (isDeleteContent || line.Contains(options.RefreshMonTrigger) || line.Contains(options.ClearMonTrigger))
                {
                    continue;
                }
                content.Add(line);
            }
            await File.WriteAllLinesAsync(path, content, encoding);
        }


    }
}
