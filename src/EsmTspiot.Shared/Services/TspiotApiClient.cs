using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
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

        public async Task<ApiResponse> GetLmInfoAsync(
            string baseUrl,
            string instancePort,
            string softPort,
            CancellationToken cancellationToken)
        {
            ApiResponse response = await SendAsync(
                "GET",
                BuildLmInfoUrl(baseUrl, instancePort, softPort),
                null,
                null,
                cancellationToken,
                true).ConfigureAwait(false);

            response.ReasonPhrase = SensitiveDataMasker.Mask(response.ReasonPhrase);
            response.ResponseBody = SensitiveDataMasker.Mask(response.ResponseBody);
            response.DecodedMessage = SensitiveDataMasker.Mask(response.DecodedMessage);
            return response;
        }

        public static string BuildUrl(string baseUrl, string path)
        {
            string normalizedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            return normalizedBase + path;
        }

        public static string BuildLmInfoUrl(
            string baseUrl,
            string instancePort,
            string softPort)
        {
            Uri baseAddress;
            if (!Uri.TryCreate((baseUrl ?? string.Empty).Trim(), UriKind.Absolute, out baseAddress) ||
                string.IsNullOrWhiteSpace(baseAddress.Host))
            {
                throw new ArgumentException("Адрес ЕСМ некорректен.", "baseUrl");
            }

            int infoPort;
            if (!int.TryParse((softPort ?? string.Empty).Trim(), out infoPort) || infoPort <= 0)
            {
                int servicePort;
                if (!int.TryParse((instancePort ?? string.Empty).Trim(), out servicePort) ||
                    servicePort < 1 || servicePort > 64535)
                {
                    throw new ArgumentException(
                        "Не удалось определить softPort экземпляра ККТ.",
                        "softPort");
                }

                infoPort = servicePort + 1000;
            }

            if (infoPort < 1 || infoPort > 65535)
            {
                throw new ArgumentException("softPort экземпляра ККТ некорректен.", "softPort");
            }

            UriBuilder builder = new UriBuilder(
                Uri.UriSchemeHttps,
                baseAddress.IsLoopback ? "localhost" : baseAddress.Host,
                infoPort,
                TspiotDefaults.LmInfoPath);
            return builder.Uri.AbsoluteUri;
        }

        private async Task<ApiResponse> SendAsync(
            string method,
            string url,
            string requestBody,
            string requestBodyForLog,
            CancellationToken cancellationToken,
            bool acceptJson = false)
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
                    if (acceptJson)
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    }
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
                result.IsTlsCertificateFailure = IsTlsCertificateFailure(ex);
                result.ResponseBody = ex.Message;
                result.DecodedMessage = TspiotErrorDecoder.DecodeConnectionFailure();
            }

            return result;
        }

        private static bool IsTlsCertificateFailure(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is AuthenticationException)
                {
                    return true;
                }

                WebException webException = current as WebException;
                if (webException != null &&
                    webException.Status == WebExceptionStatus.TrustFailure)
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }
    }
}
