using Derail;

namespace Orders;

internal static class Shipping
{
    public static Writer<Shipment, string> Ship(Option<Shipment> current, Shipment requested) =>
        current.Match(
            shipped => shipped == requested
                ? Writer.Pure<Shipment, string>(requested)
                : new Writer<Shipment, string>(requested, ["shipment-changed"]),
            () => new Writer<Shipment, string>(requested, ["order-shipped"]));
}
