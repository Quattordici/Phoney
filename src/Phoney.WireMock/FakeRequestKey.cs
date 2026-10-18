using WireMock;

namespace Phoney.WireMock;

/// <summary>Common keys of a request for the <see cref="FakeResponseExtensions"/>: requests with the same key get the same fake body.</summary>
public static class FakeRequestKey
{
    /// <summary>The path and the query string (<c>/customers?page=2</c>), the default: every URL has its own body, whatever the method and host.</summary>
    public static readonly Func<IRequestMessage, string> PathAndQuery = request =>
        string.IsNullOrEmpty(request.RawQuery) ? request.Path : request.Path + (request.RawQuery[0] == '?' ? request.RawQuery : "?" + request.RawQuery);

    /// <summary>The path only (<c>/customers</c>), so query strings such as cache busters or tracking parameters don't change the body.</summary>
    public static readonly Func<IRequestMessage, string> Path = request => request.Path;
}
