using Legend2Tool.WPF.Attributes;

namespace Legend2Tool.WPF.Models.BackList
{
    public class GEEBackList : BackListBase
    {
        [IniConfig("", "IsCompress")]
        public int IsCompress { get; set; } = 0;
    }
}
