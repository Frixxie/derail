using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orders.Tests;

public sealed class CrudTests(OrdersApi api) : IClassFixture<OrdersApi>
{
    private static readonly object[] OneLine = [new { sku = "SKU-1", quantity = 1 }];

    private sealed record Order(long Id, string Description);
    private sealed record OrderWithLines(long Id, string Description, Line[] Lines);
    private sealed record Line(string Sku, int Quantity);
    private sealed record Shipment(string Carrier, string Tracking);

    [Fact]
    public async Task Order_and_shipment_can_be_managed_independently()
    {
        var token = TestContext.Current.CancellationToken;
        var client = api.Client;
        var created = await client.PostAsJsonAsync("/orders", new { description = " Sample ", lines = OneLine }, token);
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
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path, new { updated.Description, lines = OneLine }, token)).StatusCode);
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
    public async Task Order_lines_keep_their_order_and_are_replaced_on_update()
    {
        var token = TestContext.Current.CancellationToken;
        var created = await api.Client.PostAsJsonAsync("/orders", new
        {
            description = "Sample",
            lines = new[] { new { sku = "SKU-2", quantity = 3 }, new { sku = " SKU-1 ", quantity = 1 } },
        }, token);
        var order = Assert.IsType<OrderWithLines>(await created.Content.ReadFromJsonAsync<OrderWithLines>(token));
        Assert.Equal([new Line("SKU-2", 3), new Line("SKU-1", 1)], order.Lines);

        await api.Client.PutAsJsonAsync($"/orders/{order.Id}", new { description = "Sample", lines = new[] { new { sku = "SKU-3", quantity = 2 } } }, token);

        var updated = await api.Client.GetFromJsonAsync<OrderWithLines>($"/orders/{order.Id}", token);
        Assert.Equal([new Line("SKU-3", 2)], Assert.IsType<OrderWithLines>(updated).Lines);
    }

    [Fact]
    public async Task Every_invalid_order_field_is_reported_with_its_line()
    {
        var response = await api.Client.PostAsJsonAsync("/orders", new
        {
            description = " ",
            lines = new object[] { new { sku = "", quantity = 0 }, new { sku = "SKU-2", quantity = 1 }, new { sku = "SKU-3" } },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<string, string[]>
        {
            ["description"] = ["required"],
            ["lines[0].sku"] = ["required"],
            ["lines[0].quantity"] = ["must-be-positive"],
            ["lines[2].quantity"] = ["required"],
        }, await Errors(response));
    }

    [Fact]
    public async Task Order_requires_at_least_one_line()
    {
        var response = await api.Client.PostAsJsonAsync("/orders", new { description = "Sample", lines = Array.Empty<object>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<string, string[]> { ["lines"] = ["required"] }, await Errors(response));
    }

    [Fact]
    public async Task Order_list_pages_after_an_id_and_filters_by_shipment()
    {
        var token = TestContext.Current.CancellationToken;
        var ids = new List<long>();
        foreach (var description in new[] { "First", "Second", "Third" })
        {
            var created = await api.Client.PostAsJsonAsync("/orders", new { description, lines = OneLine }, token);
            ids.Add(Assert.IsType<Order>(await created.Content.ReadFromJsonAsync<Order>(token)).Id);
        }
        await api.Client.PutAsJsonAsync($"/orders/{ids[1]}/shipment", new Shipment("Parcel", "SYN-1"), token);
        var after = ids[0] - 1;

        var page = await api.Client.GetFromJsonAsync<Order[]>($"/orders?after={after}&limit=2", token);
        var shipped = await api.Client.GetFromJsonAsync<Order[]>($"/orders?after={after}&shipped=true", token);
        var unshipped = await api.Client.GetFromJsonAsync<Order[]>($"/orders?after={after}&shipped=false", token);

        Assert.Equal(ids[..2], page?.Select(order => order.Id));
        Assert.Equal([ids[1]], shipped?.Select(order => order.Id));
        Assert.Equal([ids[0], ids[2]], unshipped?.Select(order => order.Id));
    }

    [Fact]
    public async Task Every_invalid_list_parameter_is_reported()
    {
        var response = await api.Client.GetAsync("/orders?limit=0&after=x&shipped=maybe", TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<string, string[]>
        {
            ["limit"] = ["out-of-range"],
            ["after"] = ["invalid-format"],
            ["shipped"] = ["invalid-format"],
        }, await Errors(response));
    }

    private static async Task<Dictionary<string, string[]>?> Errors(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>();
    }
}
