using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Delobytes.App.Backend.Tests.TestSupport;

internal sealed class RecordedHttpRequest
{
    public HttpMethod Method { get; init; } = HttpMethod.Get;

    public Uri? RequestUri { get; init; }

    public string? Content { get; init; }

    public string? Authorization { get; init; }
}

/// <summary>
/// Transport double for tests that inspect outgoing requests or replay a canned response. Two shapes
/// shared one class name before: a queue of responses and a response factory. Both live here now, so a
/// test picks the shape it needs instead of declaring its own handler.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new Queue<HttpResponseMessage>();
    private readonly Func<HttpResponseMessage>? _responseFactory;

    public StubHttpMessageHandler()
    {
    }

    public StubHttpMessageHandler(Func<HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

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

        if (_responseFactory != null)
        {
            return _responseFactory();
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No stubbed HTTP response is queued.");
        }

        return _responses.Dequeue();
    }
}
