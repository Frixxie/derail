using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orders.Tests;

// Each test owns its order IDs, so the tests share one database without depending on each other.
public sealed class ShipmentTests(OrdersApi api) : IClassFixture<OrdersApi>
{
    private static readonly object Requested = new { carrier = "Parcel", tracking = "SYN-001" };

    [Theory]
    [InlineData(" ", "", "required", "required")]
    [InlineData("Par\0cel", "SYN-\0", "invalid-character", "invalid-character")]
    public async Task Every_invalid_field_is_reported_before_the_order_is_read(
        string carrier, string tracking, string carrierError, string trackingError)
    {
        var response = await api.Ship(100, new { carrier, tracking });

        var problem = await Problem(response, HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>();
        Assert.Equal(new Dictionary<string, string[]> { ["carrier"] = [carrierError], ["tracking"] = [trackingError] }, errors);
    }

    [Fact]
    public async Task Missing_order_is_not_found()
    {
        var response = await api.Ship(200, Requested);

        var problem = await Problem(response, HttpStatusCode.NotFound);
        Assert.Equal("order-not-found", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task First_shipment_is_stored_with_one_event_and_a_replay_adds_none()
    {
        await api.Execute("INSERT INTO orders (id, description) VALUES (300, 'Sample')");

        var first = await api.Ship(300, new { carrier = " Parcel ", tracking = "SYN-001 " });
        var replay = await api.Ship(300, Requested);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(new ShipmentBody("Parcel", "SYN-001"), await first.Content.ReadFromJsonAsync<ShipmentBody>(token));
        Assert.Equal(new ShipmentBody("Parcel", "SYN-001"), await replay.Content.ReadFromJsonAsync<ShipmentBody>(token));
        Assert.Equal("Parcel:SYN-001", await api.Scalar<string>("SELECT carrier || ':' || tracking FROM orders WHERE id = 300"));
        Assert.Equal(1L, await api.Scalar<long>("SELECT count(*) FROM order_events WHERE order_id = 300"));
    }

    [Fact]
    public async Task Changing_a_shipment_records_a_change_event()
    {
        await api.Execute("INSERT INTO orders (id, description) VALUES (310, 'Sample')");

        await api.Ship(310, Requested);
        await api.Ship(310, new { carrier = "Parcel", tracking = "SYN-002" });

        Assert.Equal("order-shipped,shipment-changed",
            await api.Scalar<string>("SELECT string_agg(kind, ',' ORDER BY id) FROM order_events WHERE order_id = 310"));
    }

    [Theory]
    [InlineData(600L, "SYN-001")]
    [InlineData(601L, "SYN-002")]
    public async Task Overlapping_shipments_record_one_order_shipped_event(long id, string otherTracking)
    {
        await api.Execute("INSERT INTO orders (id, description) VALUES ($1, 'Sample')", id);
        var first = new ShipmentBody("Parcel", "SYN-001");
        var second = first with { Tracking = otherTracking };

        var (left, right) = await api.ShipTogether(id, first, second);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (left, right));
        Assert.Contains(await api.Scalar<string>("SELECT tracking FROM orders WHERE id = $1", id), new[] { first.Tracking, second.Tracking });
        Assert.Equal(1L, await api.Scalar<long>("SELECT count(*) FROM order_events WHERE order_id = $1 AND kind = 'order-shipped'", id));
    }

    [Fact]
    public async Task Failed_event_insert_rolls_back_the_order_update()
    {
        await api.Execute("INSERT INTO orders (id, description) VALUES (500, 'Sample')");
        // Reject only the second write for this order, so a committed order update would be visible.
        await api.Execute("ALTER TABLE order_events ADD CHECK (order_id <> 500)");

        var response = await api.Ship(500, Requested);

        var problem = await Problem(response, HttpStatusCode.InternalServerError);
        Assert.False(problem.TryGetProperty("detail", out _), "Exception details must not reach the client.");
        Assert.True(await api.Scalar<bool>("SELECT carrier IS NULL AND tracking IS NULL FROM orders WHERE id = 500"));
        Assert.Equal(0L, await api.Scalar<long>("SELECT count(*) FROM order_events WHERE order_id = 500"));
    }

    [Fact]
    public async Task Delivered_shipment_cannot_be_changed_or_removed()
    {
        await api.Execute("INSERT INTO orders (id, description, carrier, tracking) VALUES (700, 'Sample', 'Parcel', 'SYN-001')");
        var token = TestContext.Current.CancellationToken;

        var delivered = await api.Client.PutAsync("/orders/700/shipment/delivery", null, token);
        var changed = await api.Ship(700, new { carrier = "Parcel", tracking = "SYN-002" });
        var removed = await api.Client.DeleteAsync("/orders/700/shipment", token);

        Assert.Equal(HttpStatusCode.NoContent, delivered.StatusCode);
        Assert.Equal("shipment-delivered", (await Problem(changed, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        Assert.Equal("shipment-delivered", (await Problem(removed, HttpStatusCode.Conflict)).GetProperty("code").GetString());
        Assert.Equal(new DeliveredBody("Parcel", "SYN-001", true),
            await api.Client.GetFromJsonAsync<DeliveredBody>("/orders/700/shipment", token));
    }

    [Fact]
    public async Task Delivery_requires_a_shipment()
    {
        await api.Execute("INSERT INTO orders (id, description) VALUES (710, 'Sample')");

        var response = await api.Client.PutAsync("/orders/710/shipment/delivery", null, TestContext.Current.CancellationToken);

        var problem = await Problem(response, HttpStatusCode.NotFound);
        Assert.Equal("shipment-not-found", problem.GetProperty("code").GetString());
    }

    private sealed record ShipmentBody(string Carrier, string Tracking);
    private sealed record DeliveredBody(string Carrier, string Tracking, bool Delivered);

    private static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
