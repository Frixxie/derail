# Orders and shipments

An order has a description and, optionally, one shipment. Create an order first, then use its ID to manage either resource.

| Resource | Create | Read | Update | Delete |
| --- | --- | --- | --- | --- |
| Order | `POST /orders` | `GET /orders` or `GET /orders/{id}` | `PUT /orders/{id}` | `DELETE /orders/{id}` |
| Shipment | `PUT /orders/{id}/shipment` | `GET /orders/{id}/shipment` | `PUT /orders/{id}/shipment` | `DELETE /orders/{id}/shipment` |

Creating an order returns `201` and a `Location` header. Putting a shipment returns `200` with the saved carrier and tracking number; repeating or replacing it records no extra `order-shipped` event. Deleting a shipment leaves the order intact. Deleting an order removes both. Successful deletes return `204`, missing resources return `404`, and invalid fields return `400`.

[Field validation](ApiInput.cs) uses `Result.Ensure` to report the first failed rule for each field. The [shipment handler](ShipmentEndpoints.cs) uses `Validation.Zip` so a single `400` can report errors for both carrier and tracking. For single-row lookups, the [order](OrderEndpoints.cs) and shipment handlers use `Option` to distinguish a returned row from a missing resource; [`Match` maps those cases to `200` and `404`](ApiResults.cs).

## Try it

From the repository root, with Docker running and the .NET 11 SDK installed:

```sh
docker compose -f examples/Orders/compose.yaml up --detach --wait
ConnectionStrings__Orders='Host=127.0.0.1;Port=55432;Username=sample;Database=orders' \
  dotnet run --project examples/Orders --urls http://127.0.0.1:5080
```

In another terminal, create an order and use the ID from the response (shown here as `1`):

```sh
curl -i http://127.0.0.1:5080/orders -H 'Content-Type: application/json' -d '{"description":"Sample order"}'
curl -X PUT http://127.0.0.1:5080/orders/1/shipment \
  -H 'Content-Type: application/json' -d '{"carrier":"Parcel","tracking":"SYN-001"}'
curl http://127.0.0.1:5080/orders/1/shipment
```

The schema is initialized on first startup. To clean up, or reset an older disposable example database before starting, run `docker compose -f examples/Orders/compose.yaml down --volumes` (this deletes its sample data).
To run the [integration tests](../Orders.Tests/ShipmentTests.cs), use `dotnet test --project examples/Orders.Tests`; they start their own database.
