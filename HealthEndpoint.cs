namespace JobSearchManager;

public static class HealthEndpoint
{
    public const string Path = "/healthz";
    public const string ResponseBody = "Healthy";

    public static async Task<bool> TryHandleAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path != Path)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(ResponseBody, context.RequestAborted);
        return true;
    }
}
