namespace Legend2Tool.WPF.Services
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
