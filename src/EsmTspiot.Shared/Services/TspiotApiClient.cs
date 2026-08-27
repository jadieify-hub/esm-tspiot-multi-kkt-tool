using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class TspiotApiClient : ITspiotApiClient
    {
        private readonly HttpClient _httpClient;

        public TspiotApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public TspiotApiClient(HttpClient httpClient)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException("httpClient");
            }

            _httpClient = httpClient;
        }

        public Task<ApiResponse> GetInstancesAsync(string baseUrl)
        {
            return GetInstancesAsync(baseUrl, CancellationToken.None);
        }

        public Task<ApiResponse> GetInstancesAsync(string baseUrl, CancellationToken cancellationToken)
        {
            return SendAsync("GET", BuildUrl(baseUrl, TspiotDefaults.InstancesInfoPath), null, null, cancellationToken);
        }

        public Task<ApiResponse> GetDkktListAsync(string baseUrl)
        {
            return GetDkktListAsync(baseUrl, CancellationToken.None);
        }

        public Task<ApiResponse> GetDkktListAsync(string baseUrl, CancellationToken cancellationToken)
        {
            return SendAsync("GET", BuildUrl(baseUrl, TspiotDefaults.DkktListPath), null, null, cancellationToken);
        }

        public Task<ApiResponse> GetInstanceAsync(string baseUrl, string id, CancellationToken cancellationToken)
        {
            string path = TspiotDefaults.InstancesInfoPath + "/" + Uri.EscapeDataString((id ?? string.Empty).Trim());
            return SendAsync("GET", BuildUrl(baseUrl, path), null, null, cancellationToken);
        }

        public Task<ApiResponse> GetSettingsAsync(string baseUrl, string id, CancellationToken cancellationToken)
        {
            string path = TspiotDefaults.SettingsPath + "/" + Uri.EscapeDataString((id ?? string.Empty).Trim());
            return SendAsync("GET", BuildUrl(baseUrl, path), null, null, cancellationToken);
        }

        public Task<ApiResponse> AddInstanceAsync(string baseUrl, AddTspiotRequest request)
        {
            return AddInstanceAsync(baseUrl, request, CancellationToken.None);
        }

        public Task<ApiResponse> AddInstanceAsync(string baseUrl, AddTspiotRequest request, CancellationToken cancellationToken)
        {
            string requestBody = JsonHelper.Serialize(request);
            return SendAsync(
                "POST",
                BuildUrl(baseUrl, TspiotDefaults.TspiotPath),
                requestBody,
                requestBody,
                cancellationToken);
        }

        public Task<ApiResponse> RegisterInstanceAsync(string baseUrl, RegisterTspiotRequest request)
        {
            return RegisterInstanceAsync(baseUrl, request, CancellationToken.None);
        }

        public Task<ApiResponse> RegisterInstanceAsync(string baseUrl, RegisterTspiotRequest request, CancellationToken cancellationToken)
        {
            string requestBody = JsonHelper.Serialize(request);
            return SendAsync(
                "PUT",
                BuildUrl(baseUrl, TspiotDefaults.TspiotPath),
                requestBody,
                requestBody,
                cancellationToken);
        }

        public Task<ApiResponse> DeleteInstanceAsync(string baseUrl, string id, CancellationToken cancellationToken)
        {
            string path = TspiotDefaults.TspiotPath + "/" + Uri.EscapeDataString((id ?? string.Empty).Trim());
            return SendAsync("DELETE", BuildUrl(baseUrl, path), null, null, cancellationToken);
        }

        public Task<ApiResponse> ConfigureLmGatewayAsync(
            string baseUrl,
            string id,
            LmConnectionRequest request,
            CancellationToken cancellationToken)
        {
            string path = TspiotDefaults.LmSettingsPath + "/" +
                Uri.EscapeDataString((id ?? string.Empty).Trim());
            string requestBody = JsonHelper.Serialize(request);
            return SendAsync(
                "PUT",
                BuildUrl(baseUrl, path),
                requestBody,
                SensitiveDataMasker.Mask(requestBody),
                cancellationToken);
        }

        public static string BuildUrl(string baseUrl, string path)
        {
            string normalizedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            return normalizedBase + path;
        }

        private async Task<ApiResponse> SendAsync(
            string method,
            string url,
            string requestBody,
            string requestBodyForLog,
            CancellationToken cancellationToken)
        {
            ApiResponse result = new ApiResponse
            {
                Method = method,
                Url = url,
                RequestBody = requestBodyForLog ?? string.Empty
            };

            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), url))
                {
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    }

                    using (HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        result.StatusCode = (int)response.StatusCode;
                        result.ReasonPhrase = response.ReasonPhrase;
                        result.ResponseBody = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        result.IsSuccess = response.IsSuccessStatusCode;
                        result.DecodedMessage = TspiotErrorDecoder.Decode(result.StatusCode, result.ResponseBody);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                result.StatusCode = 0;
                result.IsSuccess = false;
                result.IsConnectionFailure = true;
                result.ResponseBody = "Истекло время ожидания ответа ЕСМ/ТС ПИоТ.";
                result.DecodedMessage = TspiotErrorDecoder.DecodeConnectionFailure();
            }
            catch (Exception ex)
            {
                result.StatusCode = 0;
                result.IsSuccess = false;
                result.IsConnectionFailure = true;
                result.ResponseBody = ex.Message;
                result.DecodedMessage = TspiotErrorDecoder.DecodeConnectionFailure();
            }

            return result;
        }
    }
}
