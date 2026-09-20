namespace Legend2Tool.WPF.Models.Authentication
{
    public sealed record AuthenticationSession(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset ExpiresAt,
        IReadOnlyList<string> RoleList
    );

    public sealed class AuthenticationStateChangedEventArgs : EventArgs
    {
        public AuthenticationStateChangedEventArgs(
            AuthenticationSession? session,
            string? message = null
        )
        {
            Session = session;
            Message = message;
        }

        public AuthenticationSession? Session { get; }
        public string? Message { get; }
    }
}
