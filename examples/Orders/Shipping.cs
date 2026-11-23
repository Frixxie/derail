using Derail;

namespace Orders;

internal sealed record ShipmentRequest(string? Carrier, string? Tracking);
internal sealed record Shipment(string Carrier, string Tracking);
internal sealed record Order(Shipment? Shipment);
internal sealed record FieldError(string Field, string Code);
internal readonly record struct OrderNotFound;
internal readonly record struct AlreadyShipped;
internal union ShipError(OrderNotFound, AlreadyShipped);
internal sealed record OrderShipped(Shipment Shipment);

internal static class Shipping
{
    public static Validation<Shipment, FieldError> Validate(ShipmentRequest request) =>
        Field(request.Carrier, "carrier", 40).Zip(Field(request.Tracking, "tracking", 80),
            (carrier, tracking) => new Shipment(carrier, tracking));

    private static Validation<string, FieldError> Field(string? input, string name, int maxLength) => input?.Trim() switch
    {
        null or "" => Validation.Invalid<string, FieldError>(new(name, "required")),
        var text when text.Length > maxLength => Validation.Invalid<string, FieldError>(new(name, "too-long")),
        var text => Validation.Valid<string, FieldError>(text),
    };

    // The dependency here is the locked order snapshot, not a connection. A replay proposes no events.
    public static ReaderResult<Option<Order>, Writer<Shipment, OrderShipped>, ShipError> Decide(Shipment requested) =>
        new(snapshot => snapshot.ToResult(ShipError () => new OrderNotFound()).Bind(order => order.Shipment switch
        {
            null => Result.Ok<Writer<Shipment, OrderShipped>, ShipError>(new(requested, [new(requested)])),
            var existing when existing == requested => Result.Ok<Writer<Shipment, OrderShipped>, ShipError>(Writer.Pure<Shipment, OrderShipped>(existing)),
            _ => Result.Error<Writer<Shipment, OrderShipped>, ShipError>(new AlreadyShipped()),
        }));
}
