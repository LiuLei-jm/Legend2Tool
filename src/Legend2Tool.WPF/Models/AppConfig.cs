namespace Legend2Tool.WPF.Models
{
    public sealed class AppConfig
    {
        public DynamicMonsterSpawningConfig DynamicMonsterSpawning { get; set; } = new();
    }

    public sealed class DynamicMonsterSpawningConfig
    {
        public string FilterMapCode { get; set; } = "0\r1\r2\r3\r4\r5\r6\r11\r12\r";
        public string FilterMonName { get; set; } = "弓箭手\r弓箭守卫\r虎卫\r鹰卫\r刀卫\r卫士\r带刀护卫\r";
        public string FilterMonCount { get; set; } = "1\r";
        public string FilterInterval { get; set; } = string.Empty;
        public string FilterMonNameColor { get; set; } = string.Empty;
        public string SelectedTimeUnit { get; set; } = "分";
        public int RefreshMonInterval { get; set; } = 2;
        public int ClearMonInterval { get; set; } = 15;
        public int RefreshMonMultiplier { get; set; } = 1;
        public string RefreshMonTrigger { get; set; } = "XGD_动态刷怪";
        public string ClearMonTrigger { get; set; } = "XGD_动态清怪";
        public bool IsClearMon { get; set; }
        public bool IsCommentMongen { get; set; }
        public bool IsLimitRefreshInterval { get; set; }
        public int MaxRefreshInterval { get; set; } = 30;
        public int MaxRefreshCount { get; set; } = 50;
        public int MaxMonstersPerMap { get; set; } = 200;
    }
}
