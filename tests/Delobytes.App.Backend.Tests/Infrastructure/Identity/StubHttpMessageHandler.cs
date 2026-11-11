using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

internal sealed class RecordedHttpRequest
{
    public HttpMethod Method { get; init; } = HttpMethod.Get;

    public Uri? RequestUri { get; init; }

    public string? Content { get; init; }

    public string? Authorization { get; init; }
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new Queue<HttpResponseMessage>();

    public List<RecordedHttpRequest> Requests { get; } = new List<RecordedHttpRequest>();

    public StubHttpMessageHandler Enqueue(HttpResponseMessage response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public static Dictionary<string, string> ParseForm(string content)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string pair in content.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            values[key] = value;
        }

        return values;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = request.Content == null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedHttpRequest
        {
            Method = request.Method,
            RequestUri = request.RequestUri,
            Content = content,
            Authorization = request.Headers.Authorization?.ToString(),
        });

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No stubbed HTTP response is queued.");
        }

        return _responses.Dequeue();
    }
}
