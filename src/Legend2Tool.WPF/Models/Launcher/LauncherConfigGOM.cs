using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.Launcher
{
    public class LauncherConfigGOM : LauncherConfigBase
    {
        [IniConfig("Setup", "ResourcesDir")]
        public override string? ResourcesDir { get; set; }
    }
}
