using System.Collections.Immutable;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests.Examples;

public class RestockingExamplesTests
{
    private static readonly StockStore Store = new(
        ImmutableDictionary<string, StockItem>.Empty
            .Add("BOLT", new StockItem("BOLT", 10, Discontinued: false))
            .Add("NUT", new StockItem("NUT", 5, Discontinued: true)),
        Capacity: 100);

    [Fact]
    public void Request_reports_every_invalid_field()
    {
        var result = Restocking.Validate(new RestockRequest(" ", -3));

        Assert.Equal(Validation.Invalid<Restock, string>("sku: required", "quantity: must be positive"), result);
    }

    [Theory]
    [InlineData(null, "sku: required")]
    [InlineData("  ", "sku: required")]
    [InlineData("bo-lt", "sku: letters and digits only")]
    public void Field_reports_only_its_first_failed_rule(string? sku, string error) =>
        Assert.Equal(Validation.Invalid<Restock, string>(error), Restocking.Validate(new RestockRequest(sku, 1)));

    [Fact]
    public void Valid_request_is_normalized() =>
        Assert.Equal(Validation.Valid<Restock, string>(new Restock("BOLT", 4)), Restocking.Validate(new RestockRequest(" bolt ", 4)));

    [Fact]
    public void Lookup_returns_none_for_a_missing_row()
    {
        Assert.True(Store.Find("BOLT").IsSome);
        Assert.True(Store.Find("SCREW").IsNone);
    }

    [Theory]
    [InlineData("SCREW", 1, "unknown")]
    // Discontinued is checked before capacity, so only the first broken rule is reported.
    [InlineData("NUT", 1_000, "discontinued")]
    [InlineData("BOLT", 91, "over-capacity")]
    [InlineData("BOLT", int.MaxValue, "over-capacity")]
    public void Store_workflow_stops_at_the_first_failure(string sku, int quantity, string kind)
    {
        StockError expected = kind switch
        {
            "unknown" => new StockError.UnknownSku(sku),
            "discontinued" => new StockError.Discontinued(sku),
            _ => new StockError.OverCapacity(sku, quantity, 90),
        };

        Assert.Equal(Result.Error<(StockStore, StockItem), StockError>(expected), Restocking.Apply(Store, new Restock(sku, quantity)));
    }

    [Fact]
    public void Successful_restock_saves_the_new_row_without_changing_the_old_store()
    {
        var (store, item) = Restocking.Apply(Store, new Restock("BOLT", 5)).GetValueOrElse(error => throw new Xunit.Sdk.XunitException($"{error}"));

        Assert.Equal(new StockItem("BOLT", 15, false), item);
        Assert.Equal(Option.Some(item), store.Find("BOLT"));
        Assert.Equal(10, Store.Find("BOLT").Map(old => old.OnHand).GetValueOrDefault(-1));
    }

    [Fact]
    public void Invalid_request_never_reaches_the_store()
    {
        var empty = Store with { Rows = ImmutableDictionary<string, StockItem>.Empty };

        var result = Restocking.Handle(empty, new RestockRequest(null, null));

        Assert.Equal(
            Result.Error<(StockStore, StockItem), RestockFailure>(
                new RestockFailure.Invalid(new NonEmpty<string>("sku: required", "quantity: required"))),
            result);
    }

    [Property]
    public bool On_hand_never_exceeds_capacity(PositiveInt quantity) =>
        Restocking.Handle(Store, new RestockRequest("bolt", quantity.Get)).Match(
            saved => saved.Item.OnHand <= Store.Capacity && saved.Item.OnHand == 10L + quantity.Get,
            failure => failure is RestockFailure.Rejected(StockError.OverCapacity) && 10L + quantity.Get > Store.Capacity);
}
