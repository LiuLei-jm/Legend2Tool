using Legend2Tool.WPF.Enums;
using Legend2Tool.WPF.Models.BackList;
using Legend2Tool.WPF.Models.Launcher;
using Legend2Tool.WPF.Models.M2Config;

namespace Legend2Tool.WPF.Models;

public sealed record LoadedServerConfig(
    string ServerDirectory,
    EngineType EngineType,
    M2ConfigBase M2Config,
    LauncherConfigBase LauncherConfig,
    Setup Setup,
    List<BackListBase> BackLists
);
