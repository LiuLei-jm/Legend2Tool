using Legend2Tool.WPF.Models.Authentication;

namespace Legend2Tool.WPF.Services.Authentication
{
    public interface IAuthenticationService
    {
        event EventHandler<AuthenticationStateChangedEventArgs>? AuthenticationStateChanged;

        AuthenticationSession? CurrentSession { get; }

        Task<AuthenticationSession> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default
        );

        Task<AuthenticationSession> RefreshTokenAsync(
            CancellationToken cancellationToken = default
        );
    }
}
