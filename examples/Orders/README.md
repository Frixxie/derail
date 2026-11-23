# Register a shipment

`PUT /orders/1/shipment` takes a carrier and tracking number. Send the same shipment twice and you get the same `200 OK`, with only one shipment event recorded.

Start with [the handler](Program.cs). There are three steps:

1. **Validate both fields.** [`Validation.Zip`](Shipping.cs) combines the carrier and tracking checks. If both are blank, the caller gets both errors in one `400` response.
2. **Save the shipment.** [`OrderStore.Ship`](OrderStore.cs) uses [DbReader](https://github.com/seesharper/DbReader) to read the order and write the shipment and event in one PostgreSQL transaction.
3. **Return the result.** `Result.Match` maps the shipment to `200`, a missing order to `404`, or a different shipment already on the order to `409`.

## Try it

From the repository root, with Docker running and the .NET 11 SDK installed:

```sh
docker compose -f examples/Orders/compose.yaml up --detach --wait
docker compose -f examples/Orders/compose.yaml exec postgres psql -c 'INSERT INTO orders (id) VALUES (1) ON CONFLICT DO NOTHING'
ConnectionStrings__Orders='Host=127.0.0.1;Port=55432;Username=sample;Database=orders' \
  dotnet run --project examples/Orders --urls http://127.0.0.1:5080
```

In another terminal:

```sh
curl -X PUT http://127.0.0.1:5080/orders/1/shipment \
  -H 'Content-Type: application/json' -d '{"carrier":"Parcel","tracking":"SYN-001"}'
```

To clean up, run `docker compose -f examples/Orders/compose.yaml down --volumes`.
To run the [integration tests](../Orders.Tests/ShipmentTests.cs), use `dotnet test --project examples/Orders.Tests`; they start their own database.
