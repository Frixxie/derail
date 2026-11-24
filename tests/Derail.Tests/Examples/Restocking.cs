using System.Collections.Immutable;
using Derail;

namespace Derail.Tests.Examples;

// The two boundaries of a request handler:
// incoming values are checked with Validation, so a client sees every invalid field at once;
// reads and writes against the store use Option for a row that may be missing and Result for
// a step that can fail, so the workflow stops at the first failure.

internal sealed record RestockRequest(string? Sku, int? Quantity);
internal sealed record Restock(string Sku, int Quantity);
internal sealed record StockItem(string Sku, int OnHand, bool Discontinued);

internal abstract record StockError
{
    public sealed record UnknownSku(string Sku) : StockError;
    public sealed record Discontinued(string Sku) : StockError;
    public sealed record OverCapacity(string Sku, int Requested, int Free) : StockError;
}

/// <summary>A stand-in for a database table. Lookups return Option; writes return a new store.</summary>
internal sealed record StockStore(ImmutableDictionary<string, StockItem> Rows, int Capacity)
{
    public Option<StockItem> Find(string sku) =>
        Rows.TryGetValue(sku, out var item) ? Option.Some(item) : Option.None;

    public StockStore Save(StockItem item) => this with { Rows = Rows.SetItem(item.Sku, item) };
}

internal static class Restocking
{
    // Incoming values: independent fields are combined with Zip; rules on one field run in order with AndThen.
    public static Validation<Restock, string> Validate(RestockRequest request) =>
        Option.FromNullable(request.Sku?.Trim())
            .Where(sku => sku.Length > 0)
            .ToValidation(() => "sku: required")
            .AndThen(sku => sku.All(char.IsAsciiLetterOrDigit)
                ? Validation.Valid<string, string>(sku.ToUpperInvariant())
                : Validation.Invalid<string, string>("sku: letters and digits only"))
            .Zip(
                Option.FromNullable(request.Quantity)
                    .ToValidation(() => "quantity: required")
                    .AndThen(quantity => quantity > 0
                        ? Validation.Valid<int, string>(quantity)
                        : Validation.Invalid<int, string>("quantity: must be positive")),
                (sku, quantity) => new Restock(sku, quantity));

    // Store interaction: a missing row becomes a typed error, then each rule or write may stop the workflow.
    public static Result<(StockStore Store, StockItem Item), StockError> Apply(StockStore store, Restock restock) =>
        store.Find(restock.Sku)
            .ToResult(StockError () => new StockError.UnknownSku(restock.Sku))
            .Ensure(item => !item.Discontinued, StockError (item) => new StockError.Discontinued(item.Sku))
            .Ensure(item => item.OnHand + restock.Quantity <= store.Capacity,
                StockError (item) => new StockError.OverCapacity(item.Sku, restock.Quantity, store.Capacity - item.OnHand))
            .Map(item => item with { OnHand = item.OnHand + restock.Quantity })
            .Map(item => (store.Save(item), item));

    /// <summary>The handler: validate the request, then run the store workflow on a valid one.</summary>
    public static Result<(StockStore Store, StockItem Item), RestockFailure> Handle(StockStore store, RestockRequest request) =>
        Validate(request)
            .ToResult()
            .MapError(RestockFailure (errors) => new RestockFailure.Invalid(errors))
            .Bind(restock => Apply(store, restock).MapError(RestockFailure (error) => new RestockFailure.Rejected(error)));
}

internal abstract record RestockFailure
{
    public sealed record Invalid(NonEmpty<string> Errors) : RestockFailure;
    public sealed record Rejected(StockError Error) : RestockFailure;
}
