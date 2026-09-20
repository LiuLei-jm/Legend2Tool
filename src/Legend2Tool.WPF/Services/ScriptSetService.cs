using Legend2Tool.WPF.Models.Authentication;
using Legend2Tool.WPF.Models.ScriptSets;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Legend2Tool.WPF.Services
{
    public sealed class ScriptSetService : IScriptSetService, IDisposable
    {
        private const int PageSize = 100;
        private static readonly TimeSpan ApiRequestTimeout = TimeSpan.FromMinutes(2);
        private static readonly Uri ApiBaseAddress = new("https://mir.lovemumu.top:5800/");
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IAuthenticationService _authenticationService;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public ScriptSetService(IAuthenticationService authenticationService)
            : this(
                authenticationService,
                new HttpClient
                {
                    BaseAddress = ApiBaseAddress,
                    Timeout = ApiRequestTimeout
                }
            )
        {
        }

        internal ScriptSetService(
            IAuthenticationService authenticationService,
            HttpClient httpClient
        )
        {
            _authenticationService = authenticationService;
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<ScriptSetInfo>> GetScriptSetsAsync(
            CancellationToken cancellationToken = default
        )
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _ = _authenticationService.CurrentSession
                ?? throw new AuthenticationException("登录状态已失效，请重新登录。");

            var scriptSets = new List<ScriptSetInfo>();
            int pageNumber = 1;
            int totalPages;
            do
            {
                PagedScriptSetResponse page = await GetPageAsync(
                    pageNumber,
                    cancellationToken
                );
                scriptSets.AddRange(page.Items);
                totalPages = page.TotalPages;
                pageNumber++;
            } while (pageNumber <= totalPages);

            return scriptSets;
        }

        public async Task<ScriptSetDeploymentData> GetDeploymentDataAsync(
            Guid scriptSetId,
            CancellationToken cancellationToken = default
        )
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (scriptSetId == Guid.Empty)
            {
                throw new ArgumentException("脚本套 ID 不能为空。", nameof(scriptSetId));
            }

            List<ScriptFileSummary> fileSummaries = await GetAsync<List<ScriptFileSummary>>(
                $"api/scripts/files/by-set/{scriptSetId:D}",
                cancellationToken
            );
            var scriptFiles = new List<ScriptFileInfo>(fileSummaries.Count);
            foreach (ScriptFileSummary summary in fileSummaries)
            {
                ScriptFileInfo detail = await GetAsync<ScriptFileInfo>(
                    $"api/scripts/files/{summary.Id:D}",
                    cancellationToken
                );
                scriptFiles.Add(detail);
            }

            List<ScriptSetDatabaseDataInfo> databaseRows =
                await GetAsync<List<ScriptSetDatabaseDataInfo>>(
                    $"api/scripts/db-datas/by-set/{scriptSetId:D}",
                    cancellationToken
                );
            return new ScriptSetDeploymentData(scriptFiles, databaseRows);
        }

        private Task<PagedScriptSetResponse> GetPageAsync(
            int pageNumber,
            CancellationToken cancellationToken
        ) => GetAsync<PagedScriptSetResponse>(
            $"api/scripts/sets?pageNumber={pageNumber}&pageSize={PageSize}",
            cancellationToken
        );

        private async Task<T> GetAsync<T>(
            string path,
            CancellationToken cancellationToken
        ) where T : class
        {
            AuthenticationSession session = _authenticationService.CurrentSession
                ?? throw new AuthenticationException("登录状态已失效，请重新登录。");
            using HttpResponseMessage response = await SendGetRequestAsync(
                path,
                session.AccessToken,
                cancellationToken
            );
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthenticationSession refreshedSession =
                    await _authenticationService.RefreshTokenAsync(cancellationToken);
                using HttpResponseMessage retryResponse = await SendGetRequestAsync(
                    path,
                    refreshedSession.AccessToken,
                    cancellationToken
                );
                return await ReadResponseAsync<T>(retryResponse, cancellationToken);
            }

            return await ReadResponseAsync<T>(response, cancellationToken);
        }

        private async Task<HttpResponseMessage> SendGetRequestAsync(
            string path,
            string accessToken,
            CancellationToken cancellationToken
        )
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await _httpClient.SendAsync(request, cancellationToken);
        }

        private static async Task<T> ReadResponseAsync<T>(
            HttpResponseMessage response,
            CancellationToken cancellationToken
        ) where T : class
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"获取脚本套数据失败（HTTP {(int)response.StatusCode}）。",
                    null,
                    response.StatusCode
                );
            }

            T? result = await response.Content.ReadFromJsonAsync<T>(
                    JsonOptions,
                    cancellationToken
                );
            if (result is null)
            {
                throw new JsonException("脚本套接口返回了无效的数据。");
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _httpClient.Dispose();
        }

        private sealed class PagedScriptSetResponse
        {
            public List<ScriptSetInfo> Items { get; init; } = [];
            public int TotalPages { get; init; }
        }

        private sealed class ScriptFileSummary
        {
            public Guid Id { get; init; }
        }
    }
}
