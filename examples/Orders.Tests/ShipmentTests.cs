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
        await api.Execute("INSERT INTO orders (id) VALUES (300)");

        var first = await api.Ship(300, new { carrier = " Parcel ", tracking = "SYN-001 " });
        var replay = await api.Ship(300, Requested);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(new ShipmentBody("Parcel", "SYN-001"), await first.Content.ReadFromJsonAsync<ShipmentBody>(token));
        Assert.Equal(new ShipmentBody("Parcel", "SYN-001"), await replay.Content.ReadFromJsonAsync<ShipmentBody>(token));
        Assert.Equal("Parcel:SYN-001", await api.Scalar<string>("SELECT carrier || ':' || tracking FROM orders WHERE id = 300"));
        Assert.Equal(1L, await api.Scalar<long>("SELECT count(*) FROM order_events WHERE order_id = 300 AND kind = 'order-shipped'"));
    }

    [Theory]
    [InlineData(600L, "SYN-001", HttpStatusCode.OK)]
    [InlineData(601L, "SYN-002", HttpStatusCode.Conflict)]
    public async Task Overlapping_shipments_commit_only_one_event(long id, string otherTracking, HttpStatusCode otherStatus)
    {
        await api.Execute("INSERT INTO orders (id) VALUES ($1)", id);
        var first = new ShipmentBody("Parcel", "SYN-001");
        var second = first with { Tracking = otherTracking };

        var (left, right) = await api.ShipTogether(id, first, second);
        Assert.Equal([HttpStatusCode.OK, otherStatus], new[] { left, right }.Order());

        var winner = left == HttpStatusCode.OK ? first : second;
        Assert.Equal(winner.Tracking, await api.Scalar<string>("SELECT tracking FROM orders WHERE id = $1", id));
        Assert.Equal(1L, await api.Scalar<long>("SELECT count(*) FROM order_events WHERE order_id = $1", id));
    }

    [Fact]
    public async Task Different_shipment_conflicts_and_keeps_the_first()
    {
        await api.Execute("INSERT INTO orders (id, carrier, tracking) VALUES (400, 'Parcel', 'SYN-001')");

        var response = await api.Ship(400, new { carrier = "Parcel", tracking = "different" });

        var problem = await Problem(response, HttpStatusCode.Conflict);
        Assert.Equal("already-shipped", problem.GetProperty("code").GetString());
        Assert.Equal("Parcel:SYN-001", await api.Scalar<string>("SELECT carrier || ':' || tracking FROM orders WHERE id = 400"));
    }

    [Fact]
    public async Task Failed_event_insert_rolls_back_the_order_update()
    {
        await api.Execute("INSERT INTO orders (id) VALUES (500)");
        // Reject only the second write for this order, so a committed order update would be visible.
        await api.Execute("ALTER TABLE order_events ADD CHECK (order_id <> 500)");

        var response = await api.Ship(500, Requested);

        var problem = await Problem(response, HttpStatusCode.InternalServerError);
        Assert.False(problem.TryGetProperty("detail", out _), "Exception details must not reach the client.");
        Assert.True(await api.Scalar<bool>("SELECT carrier IS NULL AND tracking IS NULL FROM orders WHERE id = 500"));
        Assert.Equal(0L, await api.Scalar<long>("SELECT count(*) FROM order_events WHERE order_id = 500"));
    }

    private sealed record ShipmentBody(string Carrier, string Tracking);

    private static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
