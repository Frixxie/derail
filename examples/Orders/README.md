# Orders and shipments

An order has a description and, optionally, one shipment. Create an order first, then use its ID to manage either resource.

| Resource | Create | Read | Update | Delete |
| --- | --- | --- | --- | --- |
| Order | `POST /orders` | `GET /orders` or `GET /orders/{id}` | `PUT /orders/{id}` | `DELETE /orders/{id}` |
| Shipment | `PUT /orders/{id}/shipment` | `GET /orders/{id}/shipment` | `PUT /orders/{id}/shipment` | `DELETE /orders/{id}/shipment` |

Creating an order returns `201` and a `Location` header. Putting a shipment returns `200` with the saved carrier and tracking number. The first shipment records an `order-shipped` event, a different one records `shipment-changed`, and repeating it records nothing. `PUT /orders/{id}/shipment/delivery` marks the shipment delivered; after that, changing or deleting it returns `409`. Deleting a shipment leaves the order intact. Deleting an order removes both. Successful deletes return `204`, missing resources return `404`, and invalid fields return `400`.

## How it uses Derail

- [Field validation](ApiInput.cs) chains rules with `Result.Ensure`, so each field reports its first failed rule.
- `Validation.Zip` combines independent fields, so one `400` lists every invalid field.
- Single-row reads return an `Option`; [`Match`](ApiResults.cs) turns it into `200` or `404`.
- Shipment changes are [`AsyncReaderResult` steps](ShipmentEndpoints.cs) sharing one connection: `Lock` reads the order `FOR UPDATE`, `Ensure` applies the delivery rule, and `Bind` runs the write. [`InTransaction`](Transactions.cs) runs a workflow for each request and commits only on success.
- A `ShipmentError` says why a step failed, and one `switch` maps it to `404` or `409`.
- [`Shipping.Ship`](Shipping.cs) is a pure function that returns the shipment as a `Writer` with the events it implies. The `Save` step writes the shipment and its events in the same transaction.

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
