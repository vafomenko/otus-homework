CREATE TABLE IF NOT EXISTS resources (
    id TEXT PRIMARY KEY,
    available INTEGER NOT NULL CHECK (available >= 0),
    courier_id TEXT,
    starts_at TIMESTAMPTZ,
    ends_at TIMESTAMPTZ
);
CREATE TABLE IF NOT EXISTS reservations (
    order_id UUID PRIMARY KEY,
    user_id BIGINT NOT NULL CHECK (user_id > 0),
    resource_id TEXT NOT NULL,
    quantity INTEGER NOT NULL CHECK (quantity > 0),
    status TEXT NOT NULL CHECK (status IN ('reserved', 'rejected', 'released')),
    reason TEXT,
    courier_id TEXT
);
INSERT INTO resources(id, available) VALUES ('SKU-1', 100000), ('SKU-EMPTY', 0) ON CONFLICT DO NOTHING;
