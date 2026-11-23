using System.Numerics;
using Derail;

namespace Orders;

internal sealed record FieldError(string Field, string Code);

internal delegate bool TryParse<T>(string input, out T value);

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

    internal static Validation<T, FieldError> Positive<T>(T? input, string name) where T : struct, INumber<T> =>
        Option.FromNullable(input)
            .ToResult(() => new FieldError(name, "required"))
            .Ensure(value => value > T.Zero, _ => new(name, "must-be-positive"))
            .ToValidation();

    internal static Result<Option<T>, FieldError> Optional<T>(string? input, string name, TryParse<T> tryParse) =>
        Option.FromNullable(input).Match(
            text => tryParse(text, out var value)
                ? Result.Ok<Option<T>, FieldError>(Option.Some(value))
                : Result.Error<Option<T>, FieldError>(new(name, "invalid-format")),
            () => Result.Ok<Option<T>, FieldError>(Option.None));
}
