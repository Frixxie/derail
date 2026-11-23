using Xunit;

namespace Derail.Tests.Examples;

public class ShippingExamplesTests
{
    [Fact]
    public void Parcel_collects_invalid_fields_in_constructor_order()
    {
        Assert.True(Parcel.Create(30, 20, 10, 1.5m) is Valid<Parcel>(Parcel(30, 20, 10, 1.5m)));
        Assert.True(Parcel.Create(0, 20, 10, -1m).Match(_ => false,
            errors => errors.SequenceEqual(["lengthCm", "weightKg"])));
    }

    [Fact]
    public async Task Shipping_quote_uses_each_runs_rates_and_preserves_pricing_events()
    {
        var subtotal = Result.Ok<Writer<decimal, string>, string>(new(20m, ["priced", "discounted"]));
        var shipping = AsyncReaderResult.FromReaderResult(ShippingQuote.WithFreight(subtotal, "Parcel"));
        var token = TestContext.Current.CancellationToken;
        var priced = await shipping.Run(new RateTable(new Dictionary<string, decimal> { ["Parcel"] = 3m }), token);
        var missing = await shipping.Run(new RateTable(new Dictionary<string, decimal>()), token);

        var quote = priced.Match(w => w, e => throw new InvalidOperationException(e));
        Assert.Equal(23m, quote.Value);
        Assert.Equal(["priced", "discounted"], quote.Events);
        Assert.Equal(Result.Error<Writer<decimal, string>, string>("No rate for Parcel"), missing);
    }
}
