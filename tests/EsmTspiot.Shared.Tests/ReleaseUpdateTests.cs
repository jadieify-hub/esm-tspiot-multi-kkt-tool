using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Services;

internal static class ReleaseUpdateTests
{
    internal static void Run()
    {
        Check("11.3.35.0", "v11.3.36.0", true);
        Check("11.3.36.0", "v11.3.36.0", false);
        Check("11.3.36.0-test", "v11.3.36.0", true);
        Check("11.3.36.0-test", "v11.3.26.0", false);
        Check("11.3.9.0", "v11.3.10.0", true);
        Check("11.3.36.0", "v11.3.37.0-rc", false);
        Check("11.3.36.0", "bad-tag", false);
        foreach (string flag in new[] { "draft", "prerelease" })
            using (var client = new HttpClient(new ResponseHandler {
                Body = "{\"tag_name\":\"v99.0.0\",\"" + flag + "\":true}" }))
                if (new ReleaseUpdateChecker(client).GetAvailableVersionAsync("11.3.36.0", CancellationToken.None).GetAwaiter().GetResult() != null)
                    throw new Exception("Draft/prerelease must not be offered.");
        using (var client = new HttpClient(new ResponseHandler { Status = HttpStatusCode.NotFound }))
            if (new ReleaseUpdateChecker(client).GetAvailableVersionAsync("11.3.36.0", CancellationToken.None).GetAwaiter().GetResult() != null)
                throw new Exception("An absent release must be ignored.");
        using (var client = new HttpClient(new ResponseHandler { Status = HttpStatusCode.ServiceUnavailable }))
        {
            bool failed = false;
            try { new ReleaseUpdateChecker(client).GetAvailableVersionAsync("11.3.36.0", CancellationToken.None).GetAwaiter().GetResult(); }
            catch (HttpRequestException) { failed = true; }
            if (!failed) throw new Exception("Network failure must remain distinguishable from an up-to-date version.");
        }
    }

    private static void Check(string current, string tag, bool available)
    {
        using (var client = new HttpClient(new ResponseHandler { Body = "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false}" }))
        {
            string actual = new ReleaseUpdateChecker(client).GetAvailableVersionAsync(current, CancellationToken.None).GetAwaiter().GetResult();
            if (actual != (available ? tag : null)) throw new Exception("Wrong update result for " + current + " -> " + tag);
        }
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        internal string Body = "{}";
        internal HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri.AbsoluteUri != ReleaseUpdateChecker.LatestReleaseApi ||
                request.Content != null || request.Headers.Authorization != null || request.Headers.UserAgent.Count == 0 ||
                request.RequestUri.Query.Length != 0)
                throw new Exception("Update requests must use the fixed public API without client data.");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
}
