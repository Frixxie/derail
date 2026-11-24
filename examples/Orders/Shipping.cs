using Derail;

namespace Orders;

internal sealed record Shipped(Shipment Shipment, IReadOnlyList<string> Events);

internal static class Shipping
{
    public static Shipped Ship(Option<Shipment> current, Shipment requested) =>
        current.Match(
            shipped => new Shipped(requested, shipped == requested ? [] : ["shipment-changed"]),
            () => new Shipped(requested, ["order-shipped"]));
}
