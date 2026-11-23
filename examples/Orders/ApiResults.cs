using Derail;

namespace Orders;

internal static class ApiResults
{
    public static IResult OkOrNotFound<T>(this Option<T> value, string code) =>
        value.Match(item => Results.Ok(item), () => NotFound(code));

    public static IResult NotFound(string code) => Problem(StatusCodes.Status404NotFound, code);

    public static IResult Problem(int statusCode, string code, int? index = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (index is { } row) extensions["index"] = row;
        return Results.Problem(statusCode: statusCode, extensions: extensions);
    }
}
