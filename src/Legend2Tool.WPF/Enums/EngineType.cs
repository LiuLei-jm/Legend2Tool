using System.ComponentModel;

namespace Legend2Tool.WPF.Enums
{
    public enum EngineType
    {
        [Description("未知引擎")]
        Unknown,
        [Description("GOM引擎")]
        GOM,
        [Description("GEE引擎")]
        GEE,
        [Description("GXX引擎")]
        GXX,
        [Description("领风引擎")]
        LF,
        [Description("V8引擎")]
        V8,
        [Description("BLUE引擎")]
        BLUE
    }
}
