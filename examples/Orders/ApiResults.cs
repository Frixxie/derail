using Derail;

namespace Orders;

internal static class ApiResults
{
    public static IResult OkOrNotFound<T>(this Option<T> value, string code) =>
        value.Match(item => Results.Ok(item), () => NotFound(code));

    public static IResult NotFound(string code) => Problem(StatusCodes.Status404NotFound, code);

    public static IResult Problem(int statusCode, string code) => Results.Problem(statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
