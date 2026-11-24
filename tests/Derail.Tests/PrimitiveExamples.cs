using AwesomeAssertions;
using Xunit;

namespace Derail.Tests;

public class PrimitiveExamples
{
    [Fact]
    public void NonEmpty_ModelsARequiredBatch()
    {
        Result<NonEmpty<int>, string> batch = NonEmpty.FromEnumerable(new[] { 1, 2, 3 })
            .ToResult(() => "At least one item is required");

        batch.Map((NonEmpty<int> items) => items.Map((int x) => x * 2).Sum())
            .Should().Be(Result.Ok<int, string>(12));
    }
}
