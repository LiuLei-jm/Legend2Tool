using Legend2Tool.WPF.Models.Authentication;
using Serilog;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Legend2Tool.WPF.Services.Authentication
{
    public sealed class AuthenticationService : IAuthenticationService, IDisposable
    {
        private const string LoginPath = "api/auth/login";
        private const string RefreshPath = "api/auth/refresh";
        private static readonly TimeSpan MaximumRefreshDelay = TimeSpan.FromDays(30);
        private static readonly Uri ApiBaseAddress = new("https://mir.lovemumu.top:5800/");
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private readonly object _sessionLock = new();
        private readonly TimeSpan _refreshAdvance;
        private readonly TimeSpan _minimumRefreshDelay;
        private CancellationTokenSource? _refreshCancellation;
        private AuthenticationSession? _currentSession;
        private bool _disposed;

        public AuthenticationService(ILogger logger)
            : this(
                new HttpClient
                {
                    BaseAddress = ApiBaseAddress,
                    Timeout = TimeSpan.FromSeconds(30)
                },
                logger,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromSeconds(1)
            )
        {
        }

        internal AuthenticationService(
            HttpClient httpClient,
            ILogger logger,
            TimeSpan refreshAdvance,
            TimeSpan minimumRefreshDelay
        )
        {
            _httpClient = httpClient;
            _logger = logger;
            _refreshAdvance = refreshAdvance;
            _minimumRefreshDelay = minimumRefreshDelay;
        }

        public event EventHandler<AuthenticationStateChangedEventArgs>?
            AuthenticationStateChanged;

        public AuthenticationSession? CurrentSession
        {
            get
            {
                lock (_sessionLock)
                {
                    return _currentSession;
                }
            }
        }

        public async Task<AuthenticationSession> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default
        )
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("用户名不能为空。", nameof(username));
            }
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("密码不能为空。", nameof(password));
            }

            await _requestLock.WaitAsync(cancellationToken);
            try
            {
                AuthResponse response = await PostAsync(
                    LoginPath,
                    new LoginRequest(username, password),
                    cancellationToken
                );
                AuthenticationSession session = CreateSession(response, []);
                SetSession(session);
                RestartRefreshLoop();
                return session;
            }
            finally
            {
                _requestLock.Release();
            }
        }

        public async Task<AuthenticationSession> RefreshTokenAsync(
            CancellationToken cancellationToken = default
        )
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _requestLock.WaitAsync(cancellationToken);
            try
            {
                AuthenticationSession currentSession = CurrentSession
                    ?? throw new AuthenticationException("登录状态已失效，请重新登录。");
                AuthResponse response = await PostAsync(
                    RefreshPath,
                    new RefreshTokenRequest(currentSession.RefreshToken),
                    cancellationToken
                );
                AuthenticationSession refreshedSession = CreateSession(
                    response,
                    currentSession.RoleList
                );
                SetSession(refreshedSession);
                return refreshedSession;
            }
            finally
            {
                _requestLock.Release();
            }
        }

        internal static TimeSpan CalculateRefreshDelay(
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            TimeSpan refreshAdvance,
            TimeSpan minimumRefreshDelay
        )
        {
            TimeSpan delay = expiresAt - now - refreshAdvance;
            if (delay <= minimumRefreshDelay)
            {
                return minimumRefreshDelay;
            }

            return delay < MaximumRefreshDelay ? delay : MaximumRefreshDelay;
        }

        private async Task<AuthResponse> PostAsync<TRequest>(
            string path,
            TRequest request,
            CancellationToken cancellationToken
        )
        {
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                path,
                request,
                JsonOptions,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                string message = await ReadErrorMessageAsync(response, cancellationToken);
                throw new AuthenticationException(message, response.StatusCode);
            }

            AuthResponse? authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(
                JsonOptions,
                cancellationToken
            );
            if (
                authResponse is null
                || string.IsNullOrWhiteSpace(authResponse.AccessToken)
                || string.IsNullOrWhiteSpace(authResponse.RefreshToken)
                || authResponse.ExpiresAt == default
            )
            {
                throw new AuthenticationException("认证服务返回了无效的令牌数据。");
            }

            return authResponse;
        }

        private static async Task<string> ReadErrorMessageAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken
        )
        {
            try
            {
                ApiProblemDetails? problem =
                    await response.Content.ReadFromJsonAsync<ApiProblemDetails>(
                        JsonOptions,
                        cancellationToken
                    );
                if (!string.IsNullOrWhiteSpace(problem?.Detail))
                {
                    return problem.Detail;
                }

                string? domainError = problem?.Errors?
                    .SelectMany(entry => entry.Value)
                    .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
                if (!string.IsNullOrWhiteSpace(domainError))
                {
                    return domainError;
                }
            }
            catch (JsonException)
            {
                // Fall through to the HTTP status message when the body is not problem JSON.
            }

            return $"认证请求失败（HTTP {(int)response.StatusCode}）。";
        }

        private static AuthenticationSession CreateSession(
            AuthResponse response,
            IReadOnlyList<string> fallbackRoles
        )
        {
            IReadOnlyList<string> roles = response.RoleList
                ?? response.Roles
                ?? fallbackRoles;
            return new AuthenticationSession(
                response.AccessToken,
                response.RefreshToken,
                response.ExpiresAt,
                roles.ToArray()
            );
        }

        private void RestartRefreshLoop()
        {
            CancellationTokenSource? previousCancellation = _refreshCancellation;
            _refreshCancellation = new CancellationTokenSource();
            previousCancellation?.Cancel();
            previousCancellation?.Dispose();
            _ = RunRefreshLoopAsync(_refreshCancellation.Token);
        }

        private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    AuthenticationSession? session = CurrentSession;
                    if (session is null)
                    {
                        return;
                    }

                    TimeSpan delay = CalculateRefreshDelay(
                        session.ExpiresAt,
                        DateTimeOffset.UtcNow,
                        _refreshAdvance,
                        _minimumRefreshDelay
                    );
                    await Task.Delay(delay, cancellationToken);

                    try
                    {
                        await RefreshTokenAsync(cancellationToken);
                    }
                    catch (AuthenticationException ex) when (IsTransient(ex.StatusCode))
                    {
                        _logger.Warning(ex, "刷新登录令牌时服务暂时不可用，将稍后重试");
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (AuthenticationException ex)
                    {
                        _logger.Warning(ex, "刷新登录令牌失败，登录状态已失效");
                        ClearSession(ex.Message);
                        return;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.Warning(ex, "刷新登录令牌时网络请求失败，将稍后重试");
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.Warning("刷新登录令牌请求超时，将稍后重试");
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "自动刷新登录令牌时发生未处理错误");
                ClearSession("登录状态已失效，请重新登录。");
            }
        }

        private static bool IsTransient(HttpStatusCode? statusCode) =>
            statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || statusCode is not null && (int)statusCode.Value >= 500;

        private void SetSession(AuthenticationSession session)
        {
            lock (_sessionLock)
            {
                _currentSession = session;
            }
            AuthenticationStateChanged?.Invoke(
                this,
                new AuthenticationStateChangedEventArgs(session)
            );
        }

        private void ClearSession(string message)
        {
            lock (_sessionLock)
            {
                _currentSession = null;
            }
            AuthenticationStateChanged?.Invoke(
                this,
                new AuthenticationStateChangedEventArgs(null, message)
            );
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _httpClient.Dispose();
        }

        private sealed record LoginRequest(string Username, string Password);
        private sealed record RefreshTokenRequest(string RefreshToken);

        private sealed class AuthResponse
        {
            public string AccessToken { get; init; } = string.Empty;
            public string RefreshToken { get; init; } = string.Empty;
            public DateTimeOffset ExpiresAt { get; init; }
            public List<string>? RoleList { get; init; }
            public List<string>? Roles { get; init; }
        }

        private sealed class ApiProblemDetails
        {
            public string? Detail { get; init; }
            public Dictionary<string, string[]>? Errors { get; init; }
        }
    }
}
