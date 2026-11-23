"""Run via test.sh, which provides the disposable database container and base URL."""
import json
import subprocess
import sys
from urllib.error import HTTPError
from urllib.request import Request, urlopen


def ship(order_id, payload, expected_status):
    req = Request(f"{sys.argv[1]}/orders/{order_id}/shipment", method="PUT",
                  headers={"Content-Type": "application/json"}, data=json.dumps(payload).encode())
    try:
        response = urlopen(req, timeout=5)
    except HTTPError as error:
        response = error
    with response:
        body = json.load(response)
        assert response.status == expected_status, (response.status, body)
        if expected_status >= 400:
            assert response.headers.get_content_type() == "application/problem+json"
            assert body["status"] == expected_status, body
        return body


def sql(statement):
    return subprocess.check_output(["docker", "compose", "exec", "-T", "postgres",
                                    "psql", "-XAt", "-v", "ON_ERROR_STOP=1", "-c", statement], text=True).strip()


shipment = {"carrier": "Parcel", "tracking": "SYN-001"}
invalid = ship(1, {"carrier": " ", "tracking": ""}, 400)
assert invalid["errors"] == {"carrier": ["required"], "tracking": ["required"]}, invalid
assert ship(999, shipment, 404)["code"] == "order-not-found"
assert ship(1, shipment, 200) == shipment
assert ship(1, shipment, 200) == shipment
assert ship(1, dict(shipment, tracking="different"), 409)["code"] == "already-shipped"
assert sql("SELECT carrier || ':' || tracking FROM orders WHERE id = 1") == "Parcel:SYN-001"
assert sql("SELECT kind FROM order_events WHERE order_id = 1") == "order-shipped"

# Reject only the second write, so an incorrectly committed order update is visible.
sql("ALTER TABLE order_events ADD CHECK (order_id <> 2)")
fault = ship(2, shipment, 500)
assert "detail" not in fault, fault
assert sql("SELECT carrier IS NULL AND tracking IS NULL FROM orders WHERE id = 2") == "t"
assert sql("SELECT count(*) FROM order_events WHERE order_id = 2") == "0"
print("Passed: validation, missing order, committed shipment, replay, conflict, infrastructure fault and rollback")
