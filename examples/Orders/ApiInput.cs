using Derail;

namespace Orders;

internal sealed record FieldError(string Field, string Code);

internal static class ApiInput
{
    public static Task<IResult> OnValid<T>(this Validation<T, FieldError> input, Func<T, Task<IResult>> action) =>
        input.Match(action, errors => Task.FromResult<IResult>(Results.ValidationProblem(errors.GroupBy(e => e.Field)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray()))));

    internal static Validation<string, FieldError> Field(string? input, string name, int maxLength) =>
        Option.FromNullable(input?.Trim())
            .Where(text => text.Length > 0)
            .ToResult(() => new FieldError(name, "required"))
            .Ensure(text => text.Length <= maxLength, _ => new(name, "too-long"))
            .Ensure(text => !text.Contains('\0'), _ => new(name, "invalid-character"))
            .ToValidation();

    internal static Validation<int, FieldError> Positive(int? input, string name) =>
        Option.FromNullable(input)
            .ToResult(() => new FieldError(name, "required"))
            .Ensure(value => value > 0, _ => new(name, "must-be-positive"))
            .ToValidation();
}
