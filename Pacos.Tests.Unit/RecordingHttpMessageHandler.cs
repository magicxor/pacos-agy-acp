using System.Net;
using System.Text;

namespace Pacos.Tests.Unit;

/// <summary>
/// Records every request and answers each one with the same JSON body.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseJson;

    public RecordingHttpMessageHandler(string responseJson)
    {
        _responseJson = responseJson;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
        });
    }
}
