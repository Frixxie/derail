using Derail;

namespace Orders;

internal sealed record FieldError(string Field, string Code);

internal static class ApiInput
{
    public static IResult NotFound(string code) => Results.Problem(statusCode: 404,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    public static Task<IResult> OnValid<T>(this Validation<T, FieldError> input, Func<T, Task<IResult>> action) =>
        input.Match(action, errors => Task.FromResult<IResult>(Results.ValidationProblem(errors.GroupBy(e => e.Field)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray()))));

    internal static Validation<string, FieldError> Field(string? input, string name, int maxLength) => input?.Trim() switch
    {
        null or "" => Validation.Invalid<string, FieldError>(new(name, "required")),
        var text when text.Length > maxLength => Validation.Invalid<string, FieldError>(new(name, "too-long")),
        var text when text.Contains('\0') => Validation.Invalid<string, FieldError>(new(name, "invalid-character")),
        var text => Validation.Valid<string, FieldError>(text),
    };
}
