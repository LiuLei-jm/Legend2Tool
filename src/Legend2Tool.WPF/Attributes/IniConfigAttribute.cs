namespace Legend2Tool.WPF.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class IniConfigAttribute : Attribute
    {
        public string SectionName { get; }
        public string KeyName { get; }
        public IniConfigAttribute(string sectionName, string keyName)
        {
            SectionName = sectionName;
            KeyName = keyName;
        }
    }
}
