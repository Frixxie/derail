using Derail;
using FsCheck;
using FsCheck.Xunit;

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
}
