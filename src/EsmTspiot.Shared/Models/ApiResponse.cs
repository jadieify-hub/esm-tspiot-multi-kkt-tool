namespace EsmTspiot.Shared.Models
{
    public sealed class ApiResponse
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public string RequestBody { get; set; }
        public int StatusCode { get; set; }
        public string ReasonPhrase { get; set; }
        public string ResponseBody { get; set; }
        public bool IsSuccess { get; set; }
        public string DecodedMessage { get; set; }
        public bool IsConnectionFailure { get; set; }
        public static bool HasHttpResponse(ApiResponse response)
        {
            return response != null && !response.IsConnectionFailure && response.StatusCode > 0;
        }

        public bool IsTlsCertificateFailure { get; set; }
    }
}
