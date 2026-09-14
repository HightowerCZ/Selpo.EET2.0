using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Selpo.Eet20.Transport;

internal sealed class EetSoapTransport
{
    internal const string SoapAction = "http://fs.gov.cz/eet/OdeslaniTrzby";
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;

    internal EetSoapTransport(HttpClient httpClient, Uri endpoint)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        if (_endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The EET endpoint must use HTTPS.", nameof(endpoint));
    }

    internal async Task<EetTransportResponse> SendAsync(string envelopeXml, CancellationToken cancellationToken)
    {
        if (envelopeXml == null) throw new ArgumentNullException(nameof(envelopeXml));

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(envelopeXml, System.Text.Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", SoapAction);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        response.Headers.TryGetValues("X-Global-Transaction-Id", out var transactionIds);
        var transactionId = transactionIds == null ? null : System.Linq.Enumerable.FirstOrDefault(transactionIds);
        if (!response.IsSuccessStatusCode)
            throw new EetTransportException(response.StatusCode, body, transactionId);
        return new EetTransportResponse(response.StatusCode, body, transactionId);
    }
}

internal sealed class EetTransportResponse
{
    internal EetTransportResponse(HttpStatusCode statusCode, string body, string? globalTransactionId)
    {
        StatusCode = statusCode;
        Body = body;
        GlobalTransactionId = globalTransactionId;
    }

    internal HttpStatusCode StatusCode { get; }
    internal string Body { get; }
    internal string? GlobalTransactionId { get; }
}
