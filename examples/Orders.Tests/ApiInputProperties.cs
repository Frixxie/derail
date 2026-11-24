using Derail;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Orders.Tests;

public sealed class ApiInputProperties
{
    [Property]
    public bool Accepted_text_is_trimmed_within_its_limit_and_free_of_nul(string? input, PositiveInt maxLength) =>
        ApiInput.Field(input, "field", maxLength.Get).Match(
            text => text.Length > 0 && text.Length <= maxLength.Get && text == text.Trim() && !text.Contains('\0'),
            _ => true);

    [Property]
    public bool Accepted_text_is_accepted_unchanged_again(string? input, PositiveInt maxLength) =>
        ApiInput.Field(input, "field", maxLength.Get).Match(
            text => ApiInput.Field(text, "field", maxLength.Get).Equals(Validation.Valid<string, FieldError>(text)),
            _ => true);

    [Property]
    public bool Surrounding_whitespace_does_not_change_the_outcome(string? input, PositiveInt maxLength) =>
        ApiInput.Field($" {input}\t", "field", maxLength.Get).Equals(ApiInput.Field(input, "field", maxLength.Get));

    [Property]
    public bool Rejected_text_reports_only_its_first_failed_rule(string? input, PositiveInt maxLength) =>
        ApiInput.Field(input, "field", maxLength.Get).Match(
            _ => true,
            errors => errors.Count == 1 && errors.Head.Field == "field");

    [Theory]
    [InlineData(null, "required")]
    [InlineData("   ", "required")]
    [InlineData("abcdef", "too-long")]
    [InlineData("ab\0", "invalid-character")]
    public void Text_rules_run_in_order_and_stop_at_the_first_failure(string? input, string code) =>
        Assert.Equal(Validation.Invalid<string, FieldError>(new("field", code)), ApiInput.Field(input, "field", 5));

    [Property]
    public bool Positive_numbers_are_accepted_and_others_name_their_rule(int? input) =>
        ApiInput.Positive(input, "quantity").Equals(input switch
        {
            null => Validation.Invalid<int, FieldError>(new("quantity", "required")),
            > 0 and var value => Validation.Valid<int, FieldError>(value),
            _ => Validation.Invalid<int, FieldError>(new("quantity", "must-be-positive")),
        });

    [Property]
    public bool Optional_parameter_is_absent_parsed_or_a_format_error(string? input) =>
        ApiInput.Optional<int>(input, "limit", int.TryParse).Equals(input switch
        {
            null => Validation.Valid<Option<int>, FieldError>(Option.None),
            _ when int.TryParse(input, out var value) => Validation.Valid<Option<int>, FieldError>(Option.Some(value)),
            _ => Validation.Invalid<Option<int>, FieldError>(new("limit", "invalid-format")),
        });

    [Property]
    public bool Independent_fields_report_every_error_in_order(string? carrier, string? tracking) =>
        ApiInput.Field(carrier, "carrier", 3)
            .Zip(ApiInput.Field(tracking, "tracking", 3), (c, t) => (c, t))
            .Match(
                _ => ApiInput.Field(carrier, "carrier", 3).IsValid && ApiInput.Field(tracking, "tracking", 3).IsValid,
                errors => errors.Select(e => e.Field).SequenceEqual(
                    new[] { ApiInput.Field(carrier, "carrier", 3), ApiInput.Field(tracking, "tracking", 3) }
                        .Where(v => v.IsInvalid).Select(v => v.Match(_ => "", e => e.Head.Field))));
}
