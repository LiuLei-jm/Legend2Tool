using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.BackList
{
    public class BackListBase
    {
        public string sectionName { get; set; } = string.Empty;
        [IniConfig("", "Source")]
        public string Source { get; set; } = string.Empty;
        [IniConfig("", "Save")]
        public string Save { get; set; } = string.Empty;
        [IniConfig("", "Hour")]
        public int Hour { get; set; } = 0;
        [IniConfig("", "Min")]
        public int Min { get; set; } = 0;
        [IniConfig("", "BackMode")]
        public int BackMode { get; set; } = 0;
        [IniConfig("", "GetBack")]
        public int GetBack { get; set; } = 0;
    }
}
