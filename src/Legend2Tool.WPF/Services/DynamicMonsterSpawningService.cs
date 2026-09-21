using Legend2Tool.WPF.Commons;
using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.ScriptOptimizations;
using Legend2Tool.WPF.State;
using System.IO;
using System.Text;
using System.Windows;

namespace Legend2Tool.WPF.Services
{
    public class DynamicMonsterSpawningService : IDynamicMonsterSpawningService
    {
        private const string BackupRootDirectoryName = "Legend2ToolBackups";
        private const string MongenBackupDirectoryName = "Mongen";
        private const string BackupIncompleteMarkerName = "backup.incomplete";
        private const string BackupRestoredMarkerName = "restore.completed";
        private readonly ConfigStore _configStore;
        private readonly IEncodingService _encodingService;
        public DynamicMonsterSpawningService(ConfigStore configStore, IEncodingService encodingService)
        {
            _configStore = configStore;
            _encodingService = encodingService;
        }

        public async Task<IReadOnlyList<DynamicMonsterSpawningResult>> GenerateRefreshMonScriptAsync(
            RefreshOptimizationOptions options
        )
        {
            var mongenPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "MonGen.txt");
            if (!File.Exists(mongenPath))
            {
                MessageBox.Show("MonGen.txt 文件不存在，请检查服务器目录设置。");
                return [];
            }
            Encoding legacyEncoding = _encodingService.GetEncodingByName("GB18030");
            Encoding mongenEncoding = ResolveEncodingForWrite(
                _encodingService.DetectFileEncodingResult(mongenPath), legacyEncoding
            );
            var robotManagePath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_def", "RobotManage.txt");
            if (!File.Exists(robotManagePath))
            {
                MessageBox.Show("RobotManage.txt 文件不存在，请检查服务器目录设置。");
                return [];
            }
            Encoding robotManageEncoding = ResolveEncodingForWrite(
                _encodingService.DetectFileEncodingResult(robotManagePath), mongenEncoding
            );
            var generateScriptTrigger = $@"@{options.RefreshMonTrigger}";
            var clearScriptTrigger = $@"@{options.ClearMonTrigger}";

            var generateScriptFiled = $@"@{options.RefreshMonTrigger}触发";
            var clearScriptFiled = $@"@{options.ClearMonTrigger}触发";

            if (File.ReadAllText(robotManagePath, robotManageEncoding).Contains(generateScriptTrigger))
            {
                MessageBox.Show($@"刷怪触发器已存在'{generateScriptTrigger}'，如果要重新生成脚本请先清除现有脚本。");
                return [];
            }

            var autoRunRobotPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_def", "AutoRunRobot.txt");
            if (!File.Exists(autoRunRobotPath))
            {
                MessageBox.Show("AutoRunRobot.txt 文件不存在，请检查服务器目录设置。");
                return [];
            }
            Encoding autoRunRobotEncoding = ResolveEncodingForWrite(
                _encodingService.DetectFileEncodingResult(autoRunRobotPath), mongenEncoding
            );

            var noClearMonListPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "NoClearMonList.txt");
            Encoding noClearMonListEncoding = File.Exists(noClearMonListPath)
                ? ResolveEncodingForWrite(
                    _encodingService.DetectFileEncodingResult(noClearMonListPath), mongenEncoding
                )
                : mongenEncoding;

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
            var referencedFileUpdates = new Dictionary<
                string,
                (Encoding Encoding, List<string> Lines)
            >(StringComparer.OrdinalIgnoreCase);


            foreach (var line in File.ReadLines(mongenPath, mongenEncoding))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(';'))
                {
                    newMongen.Add(line);
                    continue;
                }

                if (trimmedLine.StartsWith("loadgen", StringComparison.OrdinalIgnoreCase))
                {
                    newMongen.Add(line);
                    string[] loadGenParts = trimmedLine.Split(
                        AppConstants.EmptySeparator,
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    if (loadGenParts.Length < 2)
                        continue;

                    var file = loadGenParts[1];
                    if (string.IsNullOrEmpty(file) || !file.Contains("txt")) continue;
                    var filePath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Mongen", file);
                    if (!File.Exists(filePath))
                        throw new FileNotFoundException($"没有找到文件：{filePath}");
                    var fileEncoding = _encodingService.DetectFileEncoding(
                        filePath,
                        mongenEncoding
                    );
                    var newReferencedFile = new List<string>();
                    await foreach (var subLine in File.ReadLinesAsync(filePath, fileEncoding))
                    {
                        var trimmedSubLine = subLine.Trim();
                        if (string.IsNullOrEmpty(trimmedSubLine) || trimmedSubLine.StartsWith(';'))
                        {
                            newReferencedFile.Add(subLine);
                            continue;
                        }
                        ProcessEachRowOfMonSpawning(options, mapMonsters, mapMonsterCounts, filterMapCodes, filterMonNames, filterMonCounts, filterIntervals, filterMonNameColors, noClearMonLists, newReferencedFile, trimmedSubLine);
                    }
                    referencedFileUpdates[filePath] = (fileEncoding, newReferencedFile);
                }
                else
                {
                    ProcessEachRowOfMonSpawning(options, mapMonsters, mapMonsterCounts, filterMapCodes, filterMonNames, filterMonCounts, filterIntervals, filterMonNameColors, noClearMonLists, newMongen, trimmedLine);
                }

            }

            IReadOnlyList<DynamicMonsterSpawningResult> generationResults =
                CreateGenerationResults(
                    mapMonsterCounts,
                    LoadMapNames(legacyEncoding),
                    options.MaxMonstersPerMap
                );

            LimitMapMonsterCounts(
                mapMonsters,
                mapMonsterCounts,
                options.MaxMonstersPerMap
            );

            if (options.IsCommentMongen || options.IsLimitRefreshInterval)
            {
                referencedFileUpdates[mongenPath] = (mongenEncoding, newMongen);
                string backupDirectory = CreateMongenBackup(referencedFileUpdates.Keys);
                try
                {
                    foreach (
                        KeyValuePair<string, (Encoding Encoding, List<string> Lines)> update
                        in referencedFileUpdates
                    )
                    {
                        await WriteAllLinesAtomicallyAsync(
                            update.Key,
                            update.Value.Lines,
                            update.Value.Encoding
                        );
                    }
                }
                catch
                {
                    RestoreMongenBackup(backupDirectory);
                    MarkBackupRestored(backupDirectory, "生成失败，已自动恢复");
                    throw;
                }
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
                        case EngineType.HGE:
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

            using (var noClearMonListWriter = new StreamWriter(noClearMonListPath, true, noClearMonListEncoding))
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

            return generationResults;
        }

        private void ProcessEachRowOfMonSpawning(RefreshOptimizationOptions options, Dictionary<string, List<string>> mapMonsters, Dictionary<string, int> mapMonsterCounts, HashSet<string> filterMapCodes, HashSet<string> filterMonNames, HashSet<string> filterMonCounts, HashSet<string> filterIntervals, HashSet<string> filterMonNameColors, HashSet<string> noClearMonLists, List<string> outputLines, string trimmedLine)
        {
            var parts = trimmedLine.Split(AppConstants.EmptySeparator, StringSplitOptions.RemoveEmptyEntries);

            // 怪物名称
            string monName = parts.Length > 3 ? parts[3] : string.Empty;
            if (string.IsNullOrEmpty(monName) || filterMonNames.Contains(monName))
            {
                noClearMonLists.Add(monName);
                outputLines.Add(trimmedLine);
                return;
            }

            // 刷新间隔
            string interval = parts.Length > 6 ? parts[6] : options.MaxRefreshInterval.ToString();
            if (string.IsNullOrEmpty(interval) || filterIntervals.Contains(interval) || !int.TryParse(interval, out _))
            {
                noClearMonLists.Add(monName);
                outputLines.Add(trimmedLine);
                return;
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
                outputLines.Add(trimmedLine);
                return;
            }

            // 地图X坐标
            string pointX = parts.Length > 1 ? parts[1] : AppConstants.DefaultPointRange;
            if (int.TryParse(pointX, out int x))
            {
                if (x < 0) pointX = AppConstants.DefaultPointRange;
            }
            else
            {
                outputLines.Add(trimmedLine);
                return;
            }


            // 地图Y坐标
            string pointY = parts.Length > 2 ? parts[2] : AppConstants.DefaultPointRange;
            if (int.TryParse(pointY, out int y))
            {
                if (y < 0) pointY = AppConstants.DefaultPointRange;
            }
            else
            {
                outputLines.Add(trimmedLine);
                return;
            }


            // 刷新范围
            string range = parts.Length > 4 ? parts[4] : AppConstants.DefaultPointRange;
            if (int.TryParse(range, out int r))
            {
                if (r < 0) range = AppConstants.DefaultPointRange;
            }
            else
            {
                outputLines.Add(trimmedLine);
                return;
            }

            // 刷怪数量
            string monCount = parts.Length > 5 ? parts[5] : "1";
            if (filterMonCounts.Contains(monCount))
            {
                noClearMonLists.Add(monName);
                outputLines.Add(trimmedLine);
                return;
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
            else return;

            // 怪物类型
            string monType = parts.Length > 7 ? parts[7] : "0";

            // 怪物名称颜色
            string monNameColor = parts.Length > 8 ? parts[8] : "255";
            if (string.IsNullOrEmpty(monNameColor) || filterMonNameColors.Contains(monNameColor))
            {
                noClearMonLists.Add(monName);
                outputLines.Add(trimmedLine);
                return;
            }
            if (options.IsCommentMongen)
            {
                outputLines.Add($";{trimmedLine}");
            }
            else
            {
                outputLines.Add(trimmedLine);
            }

            string mongenexScript = _configStore.EngineType switch
            {
                EngineType.GOM => $"MonGenEX {mapCode} {pointX} {pointY} {monName} {range} {monCount} 0 {monNameColor}",
                EngineType.HGE => $"MonGenEX {mapCode} {pointX} {pointY} {monName}|{monType}|{monNameColor}|1,2 {range} {monCount} 0 1",
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

        private string CreateMongenBackup(IEnumerable<string> filePaths)
        {
            string serverDirectory = Path.GetFullPath(_configStore.ServerDirectory);
            string backupRoot = Path.Combine(
                serverDirectory,
                BackupRootDirectoryName,
                MongenBackupDirectoryName
            );
            Directory.CreateDirectory(backupRoot);

            string backupDirectory = Path.Combine(
                backupRoot,
                $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(backupDirectory);
            string incompleteMarker = Path.Combine(
                backupDirectory,
                BackupIncompleteMarkerName
            );
            File.WriteAllText(incompleteMarker, DateTime.Now.ToString("O"), Encoding.UTF8);

            foreach (string filePath in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(filePath);
                string relativePath = GetRelativePathWithinRoot(serverDirectory, fullPath);
                string backupPath = Path.Combine(backupDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(fullPath, backupPath, overwrite: false);
            }

            File.Delete(incompleteMarker);
            return backupDirectory;
        }

        private bool TryRestoreLatestMongenBackup()
        {
            string backupRoot = Path.Combine(
                Path.GetFullPath(_configStore.ServerDirectory),
                BackupRootDirectoryName,
                MongenBackupDirectoryName
            );
            if (!Directory.Exists(backupRoot))
                return false;

            string? backupDirectory = Directory
                .GetDirectories(backupRoot)
                .Where(path =>
                    !File.Exists(Path.Combine(path, BackupIncompleteMarkerName))
                    && !File.Exists(Path.Combine(path, BackupRestoredMarkerName))
                )
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (backupDirectory is null)
                return false;

            RestoreMongenBackup(backupDirectory);
            MarkBackupRestored(backupDirectory, "清除刷新脚本时已恢复");
            return true;
        }

        private void RestoreMongenBackup(string backupDirectory)
        {
            string serverDirectory = Path.GetFullPath(_configStore.ServerDirectory);
            foreach (
                string backupPath in Directory.EnumerateFiles(
                    backupDirectory,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string fileName = Path.GetFileName(backupPath);
                if (
                    fileName.Equals(BackupIncompleteMarkerName, StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals(BackupRestoredMarkerName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                string relativePath = GetRelativePathWithinRoot(
                    Path.GetFullPath(backupDirectory),
                    Path.GetFullPath(backupPath)
                );
                string destinationPath = Path.Combine(serverDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(backupPath, destinationPath, overwrite: true);
            }
        }

        private static void MarkBackupRestored(string backupDirectory, string reason)
        {
            File.WriteAllText(
                Path.Combine(backupDirectory, BackupRestoredMarkerName),
                $"{DateTime.Now:O}{Environment.NewLine}{reason}",
                Encoding.UTF8
            );
        }

        private static string GetRelativePathWithinRoot(string rootPath, string filePath)
        {
            string relativePath = Path.GetRelativePath(rootPath, filePath);
            if (
                Path.IsPathRooted(relativePath)
                || relativePath.Equals("..", StringComparison.Ordinal)
                || relativePath.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                || relativePath.StartsWith(
                    $"..{Path.AltDirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidOperationException($"文件路径超出服务器目录：{filePath}");
            }

            return relativePath;
        }

        private static async Task WriteAllLinesAtomicallyAsync(
            string path,
            IEnumerable<string> lines,
            Encoding encoding
        )
        {
            string directory = Path.GetDirectoryName(path)!;
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp"
            );
            try
            {
                await File.WriteAllLinesAsync(temporaryPath, lines, encoding);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private IReadOnlyDictionary<string, string> LoadMapNames(Encoding fallback)
        {
            string mapInfoPath = Path.Combine(
                _configStore.ServerDirectory,
                "Mir200",
                "Envir",
                "MapInfo.txt"
            );
            if (!File.Exists(mapInfoPath))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                Encoding encoding = ResolveEncodingForWrite(
                    _encodingService.DetectFileEncodingResult(mapInfoPath),
                    fallback
                );
                return ParseMapNames(File.ReadLines(mapInfoPath, encoding));
            }
            catch (IOException)
            {
                return new Dictionary<string, string>();
            }
            catch (UnauthorizedAccessException)
            {
                return new Dictionary<string, string>();
            }
        }

        internal static IReadOnlyDictionary<string, string> ParseMapNames(
            IEnumerable<string> lines
        )
        {
            var mapNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                int closingBracketIndex = trimmedLine.IndexOf(']');
                if (
                    string.IsNullOrWhiteSpace(trimmedLine)
                    || trimmedLine.StartsWith(';')
                    || !trimmedLine.StartsWith('[')
                    || closingBracketIndex <= 1
                )
                {
                    continue;
                }

                string[] mapParts = trimmedLine[1..closingBracketIndex]
                    .Split(
                        AppConstants.EmptySeparator,
                        StringSplitOptions.RemoveEmptyEntries
                    );
                if (mapParts.Length < 2)
                {
                    continue;
                }

                string mapName = mapParts[1].Trim();
                if (trimmedLine.Contains("FB", StringComparison.OrdinalIgnoreCase))
                {
                    mapName = $"{mapName}-副本";
                }

                foreach (
                    string mapCode in mapParts[0].Split(
                        '|',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                )
                {
                    mapNames.TryAdd(mapCode, mapName);
                }
            }

            return mapNames;
        }

        internal static IReadOnlyList<DynamicMonsterSpawningResult> CreateGenerationResults(
            IReadOnlyDictionary<string, int> mapMonsterCounts,
            IReadOnlyDictionary<string, string> mapNames,
            int maxMonstersPerMap
        ) => mapMonsterCounts
            .Where(entry => entry.Value > maxMonstersPerMap)
            .Select(entry => new DynamicMonsterSpawningResult
            {
                MapCode = entry.Key,
                MapName = mapNames.GetValueOrDefault(entry.Key, entry.Key),
                MonsterCount = entry.Value
            })
            .OrderByDescending(result => result.MonsterCount)
            .ThenBy(result => result.MapName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        internal static void LimitMapMonsterCounts(
            Dictionary<string, List<string>> mapMonsters,
            Dictionary<string, int> mapMonsterCounts,
            int maxMonstersPerMap
        )
        {
            foreach (string mapCode in mapMonsters.Keys.ToList())
            {
                int mapMonsterTotal = mapMonsterCounts[mapCode];
                if (mapMonsterTotal <= maxMonstersPerMap)
                {
                    continue;
                }

                decimal scale =
                    (maxMonstersPerMap / (decimal)mapMonsterTotal * 100m) / 100m;
                List<string> adjustedScripts = [];

                foreach (string script in mapMonsters[mapCode])
                {
                    string[] parts = script.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    if (
                        parts.Length <= 6
                        || !int.TryParse(parts[6], out int originalCount)
                    )
                    {
                        throw new InvalidDataException(
                            $"无法读取地图 {mapCode} 的 MongenEX 怪物数量：{script}"
                        );
                    }

                    int adjustedCount = decimal.ToInt32(
                        decimal.Floor(originalCount * scale));
                    if (adjustedCount < 1)
                    {
                        continue;
                    }

                    parts[6] = adjustedCount.ToString();
                    adjustedScripts.Add(string.Join(' ', parts));
                }

                mapMonsters[mapCode] = adjustedScripts;
            }
        }

        internal static Encoding ResolveEncodingForWrite(
            EncodingDetectionResult detection,
            Encoding fallback
        ) => detection.Encoding ?? fallback;

        public async Task ClearRefreshMonScriptAsync(RefreshOptimizationOptions options)
        {
            var mongenPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "MonGen.txt");
            if (!File.Exists(mongenPath))
            {
                MessageBox.Show("MonGen.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            Encoding legacyEncoding = _encodingService.GetEncodingByName("GB18030");
            Encoding mongenEncoding = ResolveEncodingForWrite(
                _encodingService.DetectFileEncodingResult(mongenPath), legacyEncoding
            );
            var robotManagePath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "Robot_def", "RobotManage.txt");
            if (!File.Exists(robotManagePath))
            {
                MessageBox.Show("RobotManage.txt 文件不存在，请检查服务器目录设置。");
                return;
            }
            Encoding robotManageEncoding = ResolveEncodingForWrite(
                _encodingService.DetectFileEncodingResult(robotManagePath), mongenEncoding
            );
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
            Encoding autoRunRobotEncoding = ResolveEncodingForWrite(
                _encodingService.DetectFileEncodingResult(autoRunRobotPath), mongenEncoding
            );

            var noClearMonListPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "NoClearMonList.txt");

            Encoding noClearMonListEncoding = File.Exists(noClearMonListPath)
                ? ResolveEncodingForWrite(
                    _encodingService.DetectFileEncodingResult(noClearMonListPath), mongenEncoding
                )
                : mongenEncoding;

            var refreshMonScriptPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary", "智能刷怪.txt");
            var clearMonScriptPath = Path.Combine(_configStore.ServerDirectory, "Mir200", "Envir", "QuestDiary", "智能清怪.txt");

            bool restoredMongenFiles = TryRestoreLatestMongenBackup();
            if (!restoredMongenFiles && options.IsCommentMongen)
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
            await File.WriteAllTextAsync(
                noClearMonListPath,
                string.Empty,
                noClearMonListEncoding
            );
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
