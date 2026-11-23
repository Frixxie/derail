# Register an order shipment

`PUT /orders/{id}/shipment` records a shipment for an existing order. The order update
and its `order-shipped` event must commit together. This local example uses ASP.NET
Core, explicit SQL, and Npgsql. The Derail NuGet package does not depend on them.

## Run the integration tests

From the repository root, with the .NET 11 SDK, Docker with Compose, Bash and
Python 3 installed:

```sh
bash examples/Orders/test.sh
```

The script starts a fresh PostgreSQL container from [`compose.yaml`](compose.yaml)
on `127.0.0.1:55432` (set `ORDERS_DB_PORT` to use another port), seeds synthetic
orders, and starts Kestrel on a loopback port. It tests the real HTTP
endpoint and checks the stored rows. To prove rollback, it makes the event insert
fail **after** the order update, expects HTTP 500, and verifies that the order
is still unshipped. It stops the API and removes the container and its data on exit.

## Try a request

Start a local database with the schema, add an order, and point the app at it:

```sh
docker compose -f examples/Orders/compose.yaml up --detach --wait
docker compose -f examples/Orders/compose.yaml exec postgres psql -c 'INSERT INTO orders (id) VALUES (1)'
ConnectionStrings__Orders='Host=127.0.0.1;Port=55432;Username=sample;Database=orders' \
  dotnet run --project examples/Orders --urls http://127.0.0.1:5080
```

```sh
curl -i -X PUT http://127.0.0.1:5080/orders/1/shipment \
  -H 'Content-Type: application/json' -d '{"carrier":"Parcel","tracking":"SYN-001"}'
```

Remove the database with `docker compose -f examples/Orders/compose.yaml down --volumes`.

| Outcome | HTTP contract |
| --- | --- |
| Shipment recorded, or same shipment replayed | `200` with the normalized `{carrier, tracking}`; one event in total |
| Missing/blank or over-length fields | `400` validation problem; `errors` maps field names to `required` or `too-long` |
| Order does not exist | `404` problem with `code: order-not-found` |
| A different shipment already exists | `409` problem with `code: already-shipped` |
| Database command fails | Generic `500` problem; transaction rolls back |

## Follow the workflow

1. [`Shipping.Validate`](Shipping.cs) trims the two fields and uses `Validation.Zip`
   to return every field error before opening a database connection.
2. [`OrderStore.Ship`](OrderStore.cs) builds an `AsyncReaderResult` that takes an
   `NpgsqlDataSource` at `Run`. It opens a transaction and reads the order with
   `FOR UPDATE`, so competing shipment requests decide from a locked row.
3. [`Shipping.Decide`](Shipping.cs) is a synchronous `ReaderResult` over that
   snapshot. `Option<Order>` distinguishes a missing row from an unshipped order;
   `Result.Bind` applies the shipment rule only when the row exists. A `Writer`
   returns the shipment with one event on first registration and none on replay.
4. The store applies each proposed event and commits. An exception leaves the
   transaction uncommitted; it is not converted into `OrderNotFound` or `AlreadyShipped`.
   [`Program.cs`](Program.cs) maps those two typed failures to their HTTP responses.
