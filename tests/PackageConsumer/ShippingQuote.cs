using Derail;

namespace PackageConsumer;

internal sealed record RateTable(IReadOnlyDictionary<string, decimal> Rates)
{
    public Option<decimal> Find(string carrier) => Rates.TryGetValue(carrier, out var rate) ? Option.Some(rate) : Option.None;
}

internal static class ShippingQuote
{
    // The subtotal is already calculated. Only the carrier rate depends on this run's environment.
    public static ReaderResult<RateTable, Writer<decimal, string>, string> WithFreight(
        Result<Writer<decimal, string>, string> subtotal, string carrier) =>
        ReaderResult.FromResult<RateTable, Writer<decimal, string>, string>(subtotal)
            .Bind(quote => ReaderResult.FromLookup((RateTable rates) => rates.Find(carrier), () => $"No rate for {carrier}")
                .Map(rate => quote.Map(amount => amount + rate)));
}
