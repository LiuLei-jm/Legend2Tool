using Legend2Tool.WPF.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.Launcher
{
    public class LauncherConfigGOM : LauncherConfigBase
    {
        [IniConfig("Setup", "ResourcesDir")]
        public override string? ResourcesDir { get; set;}
    }
}
