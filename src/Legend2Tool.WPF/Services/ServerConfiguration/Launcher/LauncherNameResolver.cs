using System.Text.RegularExpressions;
using Legend2Tool.WPF.State;
using Serilog;
using TinyPinyin;

namespace Legend2Tool.WPF.Services.ServerConfiguration.Launcher;

internal sealed class LauncherNameResolver
{
    private readonly ILogger _logger;

    public LauncherNameResolver(ILogger logger) => _logger = logger;

    public string GetResourcesDirectory(string launcherName)
    {
        if (string.IsNullOrEmpty(launcherName))
        {
            _logger.Warning("GetResourcesDirByGamePinyin 被调用，但 launcherName 为空。");
            return string.Empty;
        }
        string pinyin = PinyinHelper.GetPinyin(launcherName).ToLowerInvariant();
        return string.Concat(pinyin.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    public string GetLauncherName(ConfigStore configStore)
    {
        const string pattern = @"^(.*?)[\d一二三四五六七八九十]+区";
        if (string.IsNullOrEmpty(configStore.M2Config.GameName))
            configStore.M2Config.GameName = "热血传奇";
        string launcherName = Regex.Match(configStore.M2Config.GameName, pattern).Groups[1].Value;
        if (string.IsNullOrEmpty(launcherName))
            configStore.M2Config.GameName = "热血传奇";
        return launcherName;
    }
}
