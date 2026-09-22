namespace Legend2Tool.WPF.Services.ScriptSets
{
    public sealed class ScriptSetInstallationException : Exception
    {
        public ScriptSetInstallationException(string message)
            : base(message)
        {
        }

        public ScriptSetInstallationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
