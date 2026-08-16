namespace JobSearchManager;

public static class RequestSecurity
{
    public static bool IsStateChangingApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api") &&
        (HttpMethods.IsPost(request.Method) ||
         HttpMethods.IsPut(request.Method) ||
         HttpMethods.IsPatch(request.Method) ||
         HttpMethods.IsDelete(request.Method));

    public static bool HasSameOrigin(HttpRequest request)
    {
        var originText = request.Headers.Origin.ToString();
        return Uri.TryCreate(originText, UriKind.Absolute, out var origin) &&
            string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
