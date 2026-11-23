using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Orders.Tests;

public sealed class CrudTests(OrdersApi api) : IClassFixture<OrdersApi>
{
    private sealed record Order(long Id, string Description);
    private sealed record Shipment(string Carrier, string Tracking);

    [Fact]
    public async Task Order_and_shipment_can_be_managed_independently()
    {
        var token = TestContext.Current.CancellationToken;
        var client = api.Client;
        var created = await client.PostAsJsonAsync("/orders", new { description = " Sample " }, token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = Assert.IsType<Order>(await created.Content.ReadFromJsonAsync<Order>(token));
        var path = $"/orders/{order.Id}";
        Assert.Equal(path, created.Headers.Location?.OriginalString);
        Assert.Equal("Sample", order.Description);
        var orders = await client.GetFromJsonAsync<Order[]>("/orders", token);
        Assert.NotNull(orders);
        Assert.Contains(order, orders);

        var shipment = path + "/shipment";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(shipment, token)).StatusCode);
        foreach (var tracking in new[] { "SYN-1", "SYN-2" })
        {
            var value = new Shipment("Parcel", tracking);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(shipment, value, token)).StatusCode);
            Assert.Equal(value, await client.GetFromJsonAsync<Shipment>(shipment, token));
        }

        var updated = order with { Description = "Updated" };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path, new { updated.Description }, token)).StatusCode);
        Assert.Equal(updated, await client.GetFromJsonAsync<Order>(path, token));
        Assert.Equal(new Shipment("Parcel", "SYN-2"), await client.GetFromJsonAsync<Shipment>(shipment, token));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(shipment, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(shipment, token)).StatusCode);
        Assert.Equal(updated, await client.GetFromJsonAsync<Order>(path, token));

        var replacement = new Shipment("Parcel", "SYN-3");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(shipment, replacement, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(shipment, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(shipment, replacement, token)).StatusCode);
    }

    [Fact]
    public async Task Order_requires_a_description()
    {
        var response = await api.Client.PostAsJsonAsync("/orders", new { description = " " }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
