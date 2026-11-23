using Derail;
using Xunit;

namespace Orders.Tests;

public sealed class ShippingTests
{
    [Theory]
    [InlineData(null, "order-shipped")]
    [InlineData("SYN-001", null)]
    [InlineData("SYN-000", "shipment-changed")]
    public void Shipping_records_an_event_only_when_the_shipment_changes(string? currentTracking, string? expected)
    {
        var requested = new Shipment("Parcel", "SYN-001");
        var current = Option.FromNullable(currentTracking).Map(tracking => requested with { Tracking = tracking });

        var (shipment, events) = Shipping.Ship(current, requested);

        Assert.Equal(requested, shipment);
        Assert.Equal(expected is null ? [] : new[] { expected }, events);
    }
}
