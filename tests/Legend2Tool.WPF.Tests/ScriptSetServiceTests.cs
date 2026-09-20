using Legend2Tool.WPF.Models.Authentication;
using Legend2Tool.WPF.Models.ScriptSets;
using Legend2Tool.WPF.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class ScriptSetServiceTests
{
    [Fact]
    public async Task GetScriptSetsAsync_MultiplePages_ReturnsAllItemsWithBearerToken()
    {
        var requestedPages = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
            string query = request.RequestUri?.Query ?? string.Empty;
            requestedPages.Add(query);
            string json = query.Contains("pageNumber=1", StringComparison.Ordinal)
                ? """
                  {
                    "items": [
                      {
                        "id": "11111111-1111-1111-1111-111111111111",
                        "name": "基础脚本套",
                        "description": "基础功能"
                      }
                    ],
                    "totalPages": 2
                  }
                  """
                : """
                  {
                    "items": [
                      {
                        "id": "22222222-2222-2222-2222-222222222222",
                        "name": "扩展脚本套",
                        "description": "扩展功能"
                      }
                    ],
                    "totalPages": 2
                  }
                  """;
            return Task.FromResult(JsonResponse(json));
        });
        var authenticationService = new StubAuthenticationService("access-1");
        using var httpClient = CreateHttpClient(handler);
        using var service = new ScriptSetService(authenticationService, httpClient);

        IReadOnlyList<ScriptSetInfo> scriptSets = await service.GetScriptSetsAsync();

        Assert.Equal(2, scriptSets.Count);
        Assert.Equal("基础脚本套", scriptSets[0].Name);
        Assert.Equal("扩展脚本套", scriptSets[1].Name);
        Assert.Equal(2, requestedPages.Count);
        Assert.Contains("pageSize=100", requestedPages[0]);
    }

    [Fact]
    public async Task GetScriptSetsAsync_Unauthorized_RefreshesTokenAndRetries()
    {
        int requestCount = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            int currentRequest = Interlocked.Increment(ref requestCount);
            if (currentRequest == 1)
            {
                Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            Assert.Equal("access-2", request.Headers.Authorization?.Parameter);
            return Task.FromResult(
                JsonResponse(
                    """
                    {
                      "items": [],
                      "totalPages": 0
                    }
                    """
                )
            );
        });
        var authenticationService = new StubAuthenticationService("access-1");
        using var httpClient = CreateHttpClient(handler);
        using var service = new ScriptSetService(authenticationService, httpClient);

        IReadOnlyList<ScriptSetInfo> scriptSets = await service.GetScriptSetsAsync();

        Assert.Empty(scriptSets);
        Assert.Equal(1, authenticationService.RefreshCount);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetDeploymentDataAsync_ReturnsScriptDetailsAndDatabaseRows()
    {
        Guid scriptSetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid scriptFileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var requestedPaths = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            requestedPaths.Add(path);
            string json = path switch
            {
                "/api/scripts/files/by-set/11111111-1111-1111-1111-111111111111" =>
                    """
                    [
                      {
                        "id": "22222222-2222-2222-2222-222222222222",
                        "fileName": "QFunction-0.txt",
                        "filePath": "Mir200/Envir",
                        "type": 2
                      }
                    ]
                    """,
                "/api/scripts/files/22222222-2222-2222-2222-222222222222" =>
                    """
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "fileName": "QFunction-0.txt",
                      "filePath": "Mir200/Envir",
                      "type": 2,
                      "wholeContent": null,
                      "segments": [
                        {
                          "id": null,
                          "triggerField": "@Login",
                          "content": "SENDMSG 6 测试"
                        }
                      ]
                    }
                    """,
                "/api/scripts/db-datas/by-set/11111111-1111-1111-1111-111111111111" =>
                    """
                    [
                      {
                        "id": "33333333-3333-3333-3333-333333333333",
                        "scriptSetId": "11111111-1111-1111-1111-111111111111",
                        "tableType": 1,
                        "name": "测试物品",
                        "dataJson": "{\"Idx\":100,\"Name\":\"测试物品\"}"
                      }
                    ]
                    """,
                _ => throw new Xunit.Sdk.XunitException($"Unexpected API path: {path}")
            };
            return Task.FromResult(JsonResponse(json));
        });
        var authenticationService = new StubAuthenticationService("access-1");
        using var httpClient = CreateHttpClient(handler);
        using var service = new ScriptSetService(authenticationService, httpClient);

        ScriptSetDeploymentData deploymentData =
            await service.GetDeploymentDataAsync(scriptSetId);

        ScriptFileInfo scriptFile = Assert.Single(deploymentData.ScriptFiles);
        Assert.Equal(scriptFileId, scriptFile.Id);
        Assert.Equal(ScriptFileType.Partial, scriptFile.Type);
        Assert.Equal("@Login", Assert.Single(scriptFile.Segments!).TriggerField);
        ScriptSetDatabaseDataInfo databaseRow =
            Assert.Single(deploymentData.DatabaseRows);
        Assert.Equal(GameDatabaseTableType.StdItems, databaseRow.TableType);
        Assert.Equal(3, requestedPaths.Count);
        Assert.DoesNotContain(
            requestedPaths,
            path => path.Contains("material", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://mir.lovemumu.top:5800/")
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public StubAuthenticationService(string accessToken)
        {
            CurrentSession = CreateSession(accessToken);
        }

        public event EventHandler<AuthenticationStateChangedEventArgs>?
            AuthenticationStateChanged;

        public AuthenticationSession? CurrentSession { get; private set; }
        public int RefreshCount { get; private set; }

        public Task<AuthenticationSession> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<AuthenticationSession> RefreshTokenAsync(
            CancellationToken cancellationToken = default
        )
        {
            RefreshCount++;
            CurrentSession = CreateSession("access-2");
            AuthenticationStateChanged?.Invoke(
                this,
                new AuthenticationStateChangedEventArgs(CurrentSession)
            );
            return Task.FromResult(CurrentSession);
        }

        private static AuthenticationSession CreateSession(string accessToken) => new(
            accessToken,
            "refresh-token",
            DateTimeOffset.UtcNow.AddHours(1),
            []
        );
    }

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
