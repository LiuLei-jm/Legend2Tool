namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class RefreshOptimizationOptions
    {
        public string RefreshMonTrigger { get; set; } = string.Empty;
        public string ClearMonTrigger { get; set; } = string.Empty;
        public string FilterMapCode { get; set; } = string.Empty;
        public string FilterMonName { get; set; } = string.Empty;
        public string FilterMonCount { get; set; } = string.Empty;
        public string FilterInterval { get; set; } = string.Empty;
        public string FilterMonNameColor { get; set; } = string.Empty;
        public string SelectedTimeUnit { get; set; } = string.Empty;
        public int RefreshMonInterval { get; set; }
        public int ClearMonInterval { get; set; }
        public int RefreshMonMultiplier { get; set; }
        public bool IsCommentMongen { get; set; }
        public bool IsClearMon { get; set; }
    }
}
