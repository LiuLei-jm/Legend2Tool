namespace Legend2Tool.WPF.Commons
{
    public class AppConstants
    {
        public const string DefaultFilterMapCode = "0\r1\r2\r3\r4\r5\r6\r11\r12\r";
        public const string DefaultFilterMonName = "弓箭手\r弓箭守卫\r虎卫\r鹰卫\r刀卫\r卫士\r带刀护卫\r";
        public const string DefaultFilterMonCount = "1\r";
        public const string DefaultSelectedTimeUnit = "分";
        public const string DefaultRefreshMonTrigger = "XGD_动态刷怪";
        public const string DefaultClearMonTrigger = "XGD_动态清怪";

        public const string DefaultPointRange = "50";
        public const string StartWriteTitle = ";---------------由小疙瘩制作QQ14699396,生成开始";
        public const string EndWriteTitle = ";---------------由小疙瘩制作QQ14699396,生成结束";
        public static readonly string[] LineSeparator = ["\r", "\n", "\r\n"];

        public static readonly char[] EmptySeparator = [' ', '\t'];
        public static readonly char[] MerchantSeparator = ['\\', '/'];
        public static readonly HashSet<string> ExcludedTriggers = new(StringComparer.OrdinalIgnoreCase)
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
        public static readonly HashSet<string> ExcludeItemName = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "金币","金创药(小量)","魔法药(小量)","金创药(中量)","魔法药(中量)","强效金创药","强效魔法药","太阳水","疗伤药","万年雪霜","强效太阳水"
        };
        public static readonly HashSet<string> GiveCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "give","giveex", "giveonitem", "givestateitem", "givegamepet", "givefenghao", "confertitle"
        };
        public static readonly HashSet<string> TakeCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "take","takew", "recycfenghao", "deprivetitle"
        };
        public static readonly HashSet<string> MapCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "map","mapmove","groupmapmove"
        };
    }
}
