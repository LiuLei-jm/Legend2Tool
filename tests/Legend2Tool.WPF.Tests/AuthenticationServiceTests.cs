using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Legend2Tool.WPF.Models.Authentication;
using Legend2Tool.WPF.Services.Authentication;
using Serilog;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class AuthenticationServiceTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task LoginAsync_ValidResponse_StoresTokensRolesAndRequestBody()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            Assert.Equal("/api/auth/login", request.RequestUri?.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {
                  "accessToken": "access-1",
                  "refreshToken": "refresh-1",
                  "expiresAt": "2030-01-01T00:00:00+00:00",
                  "roleList": ["Admin", "User"]
                }
                """
            );
        });
        using var httpClient = CreateHttpClient(handler);
        using var service = CreateService(httpClient);

        AuthenticationSession session = await service.LoginAsync("tester", "secret");

        using JsonDocument requestJson = JsonDocument.Parse(requestBody!);
        Assert.Equal("tester", requestJson.RootElement.GetProperty("username").GetString());
        Assert.Equal("secret", requestJson.RootElement.GetProperty("password").GetString());
        Assert.Equal("access-1", session.AccessToken);
        Assert.Equal("refresh-1", session.RefreshToken);
        Assert.Equal(["Admin", "User"], session.RoleList);
        Assert.Same(session, service.CurrentSession);
    }

    [Fact]
    public async Task RefreshTokenAsync_UsesCurrentRefreshTokenAndRotatesTokens()
    {
        int requestCount = 0;
        string? refreshRequestBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            int currentRequest = Interlocked.Increment(ref requestCount);
            if (currentRequest == 1)
            {
                return JsonResponse(
                    """
                    {
                      "accessToken": "access-1",
                      "refreshToken": "refresh-1",
                      "expiresAt": "2030-01-01T00:00:00+00:00",
                      "roleList": ["Admin"]
                    }
                    """
                );
            }

            Assert.Equal("/api/auth/refresh", request.RequestUri?.AbsolutePath);
            refreshRequestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {
                  "accessToken": "access-2",
                  "refreshToken": "refresh-2",
                  "expiresAt": "2030-01-01T01:00:00+00:00"
                }
                """
            );
        });
        using var httpClient = CreateHttpClient(handler);
        using var service = CreateService(httpClient);
        await service.LoginAsync("tester", "secret");

        AuthenticationSession refreshed = await service.RefreshTokenAsync();

        using JsonDocument requestJson = JsonDocument.Parse(refreshRequestBody!);
        Assert.Equal(
            "refresh-1",
            requestJson.RootElement.GetProperty("refreshToken").GetString()
        );
        Assert.Equal("access-2", refreshed.AccessToken);
        Assert.Equal("refresh-2", refreshed.RefreshToken);
        Assert.Equal(["Admin"], refreshed.RoleList);
    }

    [Fact]
    public async Task LoginAsync_NearExpiry_AutomaticallyRefreshesToken()
    {
        int requestCount = 0;
        var refreshed = new TaskCompletionSource<AuthenticationSession>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            int currentRequest = Interlocked.Increment(ref requestCount);
            string json = currentRequest == 1
                ? $$"""
                  {
                    "accessToken": "access-1",
                    "refreshToken": "refresh-1",
                    "expiresAt": "{{DateTimeOffset.UtcNow.AddMilliseconds(150):O}}"
                  }
                  """
                : $$"""
                  {
                    "accessToken": "access-2",
                    "refreshToken": "refresh-2",
                    "expiresAt": "{{DateTimeOffset.UtcNow.AddHours(1):O}}"
                  }
                  """;
            return Task.FromResult(JsonResponse(json));
        });
        using var httpClient = CreateHttpClient(handler);
        using var service = new AuthenticationService(
            httpClient,
            Logger,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10)
        );
        service.AuthenticationStateChanged += (_, eventArgs) =>
        {
            if (eventArgs.Session?.AccessToken == "access-2")
            {
                refreshed.TrySetResult(eventArgs.Session);
            }
        };

        await service.LoginAsync("tester", "secret");
        AuthenticationSession refreshedSession = await refreshed.Task.WaitAsync(
            TimeSpan.FromSeconds(2)
        );

        Assert.Equal("refresh-2", refreshedSession.RefreshToken);
        Assert.True(requestCount >= 2);
    }

    [Fact]
    public void CalculateRefreshDelay_RefreshesAheadOfExpiration()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        TimeSpan delay = AuthenticationService.CalculateRefreshDelay(
            now.AddMinutes(10),
            now,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(1)
        );

        Assert.Equal(TimeSpan.FromMinutes(9), delay);
    }

    private static AuthenticationService CreateService(HttpClient httpClient) => new(
        httpClient,
        Logger,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromSeconds(1)
    );

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://mir.lovemumu.top:5800/")
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>
        > _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler
        )
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => _handler(request, cancellationToken);
    }
}
