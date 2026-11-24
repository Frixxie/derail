using System.Numerics;
using Derail;

namespace Orders;

internal sealed record FieldError(string Field, string Code);

internal delegate bool TryParse<T>(string input, out T value);

// Incoming values are checked with Validation: Zip collects errors across independent fields,
// and AndThen applies one field's rules in order, so each field reports only its first failed rule.
internal static class ApiInput
{
    public static Task<IResult> OnValid<T>(this Validation<T, FieldError> input, Func<T, Task<IResult>> action) =>
        input.Match(action, errors => Task.FromResult<IResult>(Results.ValidationProblem(errors.GroupBy(e => e.Field)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray()))));

    internal static Validation<T, FieldError> Rule<T>(this Validation<T, FieldError> input, Func<T, bool> holds, string name, string code) =>
        input.AndThen(value => holds(value) ? Validation.Valid<T, FieldError>(value) : Validation.Invalid<T, FieldError>(new(name, code)));

    internal static Validation<string, FieldError> Field(string? input, string name, int maxLength) =>
        Option.FromNullable(input?.Trim())
            .Where(text => text.Length > 0)
            .ToValidation(() => new FieldError(name, "required"))
            .Rule(text => text.Length <= maxLength, name, "too-long")
            .Rule(text => !text.Contains('\0'), name, "invalid-character");

    internal static Validation<T, FieldError> Positive<T>(T? input, string name) where T : struct, INumber<T> =>
        Option.FromNullable(input)
            .ToValidation(() => new FieldError(name, "required"))
            .Rule(value => value > T.Zero, name, "must-be-positive");

    internal static Validation<Option<T>, FieldError> Optional<T>(string? input, string name, TryParse<T> tryParse) =>
        Option.FromNullable(input).Match(
            text => tryParse(text, out var value)
                ? Validation.Valid<Option<T>, FieldError>(Option.Some(value))
                : Validation.Invalid<Option<T>, FieldError>(new(name, "invalid-format")),
            () => Validation.Valid<Option<T>, FieldError>(Option.None));
}
