using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Legend2Tool.WPF.Models.ScriptOptimizations
{
    public class DuplicatedTriggerEntry
    {
        public DuplicatedTriggerEntry(string currentField, string fileName, string filePath, int lineNumber, bool isRecursion)
        {
            TriggerField = currentField;
            FileName = fileName;
            FilePath = filePath;
            LineNumber = lineNumber;
            IsRecursion = isRecursion;
        }

        public string TriggerField { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public bool IsRecursion { get; set; } 
    }
}
