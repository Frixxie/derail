CREATE TABLE orders (
    id bigint PRIMARY KEY,
    carrier text,
    tracking text,
    CHECK ((carrier IS NULL) = (tracking IS NULL))
);

CREATE TABLE order_events (
    order_id bigint NOT NULL REFERENCES orders(id),
    kind text NOT NULL,
    PRIMARY KEY (order_id, kind)
);
