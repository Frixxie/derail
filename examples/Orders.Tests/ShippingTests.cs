using Derail;
using FsCheck;
using FsCheck.Xunit;
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

    [Property]
    public bool Shipping_the_saved_shipment_again_records_nothing(NonNull<string> carrier, NonNull<string> tracking, bool shipped)
    {
        var requested = new Shipment(carrier.Get, tracking.Get);
        Option<Shipment> current = shipped ? Option.Some(requested with { Tracking = "previous" }) : Option.None;

        var saved = Shipping.Ship(current, requested).Shipment;

        return Shipping.Ship(Option.Some(saved), requested).Events.Count == 0;
    }
}
