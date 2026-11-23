using Derail;

namespace Orders;

internal sealed record ShipmentRequest(string? Carrier, string? Tracking);
internal sealed record Shipment(string Carrier, string Tracking);
internal sealed record FieldError(string Field, string Code);
internal readonly record struct OrderNotFound;
internal readonly record struct AlreadyShipped;
internal union ShipError(OrderNotFound, AlreadyShipped);

internal static class Shipping
{
    public static Validation<Shipment, FieldError> Validate(ShipmentRequest request) =>
        Field(request.Carrier, "carrier", 40).Zip(Field(request.Tracking, "tracking", 80),
            (carrier, tracking) => new Shipment(carrier, tracking));

    private static Validation<string, FieldError> Field(string? input, string name, int maxLength) => input?.Trim() switch
    {
        null or "" => Validation.Invalid<string, FieldError>(new(name, "required")),
        var text when text.Length > maxLength => Validation.Invalid<string, FieldError>(new(name, "too-long")),
        var text when text.Contains('\0') => Validation.Invalid<string, FieldError>(new(name, "invalid-character")),
        var text => Validation.Valid<string, FieldError>(text),
    };
}
