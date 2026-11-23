CREATE TABLE IF NOT EXISTS resources (
    id TEXT PRIMARY KEY,
    available INTEGER NOT NULL CHECK (available BETWEEN 0 AND 1),
    courier_id TEXT NOT NULL,
    starts_at TIMESTAMPTZ NOT NULL,
    ends_at TIMESTAMPTZ NOT NULL CHECK (ends_at > starts_at),
    UNIQUE (courier_id, starts_at)
);
CREATE TABLE IF NOT EXISTS reservations (
    order_id UUID PRIMARY KEY,
    user_id BIGINT NOT NULL CHECK (user_id > 0),
    resource_id TEXT NOT NULL,
    quantity INTEGER NOT NULL CHECK (quantity = 1),
    status TEXT NOT NULL CHECK (status IN ('reserved', 'rejected', 'released')),
    reason TEXT,
    courier_id TEXT
);
INSERT INTO resources(id, available, courier_id, starts_at, ends_at)
SELECT 'slot-' || lpad(n::text, 4, '0'), 1, 'courier-' || n, '2030-01-15 10:00:00+00'::timestamptz, '2030-01-15 12:00:00+00'::timestamptz
FROM generate_series(1, 1000) n ON CONFLICT DO NOTHING;
