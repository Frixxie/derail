using AwesomeAssertions;
using Xunit;

namespace Derail.Tests;

public class PrimitiveExamples
{
    private sealed record Pricing(decimal UnitPrice, int MaximumQuantity);
    private sealed record Quoted(int Quantity, decimal Total);

    private static ReaderResult<Pricing, Writer<decimal, Quoted>, string> Quote(int quantity) =>
        new(config => quantity > 0 && quantity <= config.MaximumQuantity
            ? Result.Ok<Writer<decimal, Quoted>, string>(
                new(quantity * config.UnitPrice, [new Quoted(quantity, quantity * config.UnitPrice)]))
            : Result.Error<Writer<decimal, Quoted>, string>("Quantity is outside the allowed range"));

    [Fact]
    public void Quote_UsesExplicitConfigurationAndReturnsEventsWithoutPublishingThem()
    {
        var workflow = Quote(3);
        var result = workflow.Run(new Pricing(10m, 5));

        var quote = result.Match(value => value, error => throw new InvalidOperationException(error));
        quote.Value.Should().Be(30m);
        quote.Events.Should().Equal(new Quoted(3, 30m));
        workflow.Run(new Pricing(10m, 2)).IsError.Should().BeTrue();
    }

    [Fact]
    public void NonEmpty_ModelsARequiredBatch()
    {
        var batch = NonEmpty.FromEnumerable(new[] { 1, 2, 3 })
            .ToResult(() => "At least one item is required");

        batch.Map(items => items.Map(x => x * 2).Sum())
            .Should().Be(Result.Ok<int, string>(12));
    }
}
