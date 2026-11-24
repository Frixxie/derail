# Orders and shipments

An order has a description, one or more lines and, optionally, one shipment. Create an order first, then use its ID to manage either resource.

| Resource | Create | Read | Update | Delete |
| --- | --- | --- | --- | --- |
| Order | `POST /orders` | `GET /orders` or `GET /orders/{id}` | `PUT /orders/{id}` | `DELETE /orders/{id}` |
| Shipment | `PUT /orders/{id}/shipment` | `GET /orders/{id}/shipment` | `PUT /orders/{id}/shipment` | `DELETE /orders/{id}/shipment` |

Two more endpoints change shipments: `PUT /orders/{id}/shipment/delivery` marks a shipment delivered, and `POST /shipments` ships several orders at once.

- **Orders:** Each line has a `sku` and a positive `quantity`. Creating an order returns `201` and a `Location` header; updating it replaces the description and lines. With an `Idempotency-Key` header, a retry with the same order returns the original `201`, and a different order with that key returns `422`. A retry compares against the order as it is now, so after the order is updated, retrying the original create also returns `422`. Once an order is delivered, updating it returns `409`.
- **Listing:** `GET /orders` accepts optional `limit` (1–100, default 50), `after` (only IDs greater than this) and `shipped` (`true` or `false`) query parameters. It returns `{ "items": [...], "next": 42 }`; pass `next` as `after` to get the following page. `next` is `null` on the last page.
- **Shipments:** Putting a shipment returns `200` with the saved carrier and tracking number. The first shipment records an `order-shipped` event, a different one records `shipment-changed`, and repeating it records nothing. Delivery records `shipment-delivered` once, and deleting a shipment records `shipment-removed`. Once the shipment is delivered, changing or deleting it returns `409`. Reading, changing or delivering a shipment returns `404` with `order-not-found` or `shipment-not-found`, so a client can tell which one is missing. Deleting a shipment leaves the order intact; deleting an order removes both.
- **Import:** `POST /shipments` takes a non-empty `[{ "orderId", "carrier", "tracking" }]`. It reports every invalid row at once, such as `[1].orderId` or `[1].carrier`, then ships each order in one transaction. If an order cannot be shipped, it returns that row's `404` or `409` with its `index` and saves nothing.
- **Status codes:** Successful deletes return `204`, missing resources `404`, and invalid input `400` with every error.

## How it uses Derail

Incoming values and database work use different types on purpose. Input is checked with `Validation`, so a client sees every invalid field in one `400`. Database steps use `Option` for a row that may be missing and `Result` for a step that can fail, so a workflow stops at its first failure.

- **`Validation.AndThen`** chains the rules for one [field](ApiInput.cs), so each field reports its first failed rule.
- **`Validation.Zip`** combines independent fields, so one `400` lists every invalid field.
- **`NonEmpty`, `Validation.Traverse` and `MapErrors`** validate [order lines](OrderEndpoints.cs). Traversing a `NonEmpty` keeps the validated lines non-empty, and `MapErrors` prefixes each error with its line, such as `lines[2].quantity`. The [shipment import](ShipmentEndpoints.cs) validates its rows in the same way.
- **`Option`** marks absent values: an unset query parameter (`GetValueOrDefault` supplies the default and `Match` builds the optional `shipped` filter), a row a query did not return (no order, no inserted ID because the idempotency key is taken, nothing deleted), and an unshipped order, where `Zip` combines carrier and tracking.
- **Async `Result` pipelines** compose a workflow from steps that take the connection and token. `ToResult` turns a missing row into a typed error, `Ensure` applies a rule such as the delivery rule, `BindAsync` runs the next fallible step, and `MapAsync` runs a write that cannot fail with a domain error. [`InTransaction`](Transactions.cs) runs a workflow for each request and commits only on success. The import loops over its rows instead of chaining a `Bind` per row.
- **Typed errors:** `ShipmentError` and `OrderError` say why a step failed, and one `switch` for each resource maps them to `404`, `409` or `422`.
- **SQL** is always a compile-time constant with parameters for every input, including the list filters.
- **`RecoverAsync`** makes creating an order safe to retry. The insert step fails with the used idempotency key, and `Replay` returns the stored order or a `KeyReused` error.
- **Pure domain logic:** [`Shipping.Ship`](Shipping.cs) returns the shipment with the events it implies; `Save` writes both in the same transaction.

## Try it

From the repository root, with Docker running and the .NET 11 SDK installed:

```sh
docker compose -f examples/Orders/compose.yaml up --detach --wait
ConnectionStrings__Orders='Host=127.0.0.1;Port=55432;Username=sample;Database=orders' \
  dotnet run --project examples/Orders --urls http://127.0.0.1:5080
```

In another terminal, create an order and use the ID from the response (shown here as `1`):

```sh
curl -i http://127.0.0.1:5080/orders -H 'Content-Type: application/json' \
  -d '{"description":"Sample order","lines":[{"sku":"SKU-1","quantity":2}]}'
curl -X PUT http://127.0.0.1:5080/orders/1/shipment \
  -H 'Content-Type: application/json' -d '{"carrier":"Parcel","tracking":"SYN-001"}'
curl http://127.0.0.1:5080/orders/1/shipment
```

The schema is initialized on first startup. To clean up, or reset an older disposable example database before starting, run `docker compose -f examples/Orders/compose.yaml down --volumes` (this deletes its sample data).
To run the [tests](../Orders.Tests), use `dotnet test --project examples/Orders.Tests`; the integration tests start their own database.
