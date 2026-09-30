CREATE TABLE IF NOT EXISTS orders (
    id UUID PRIMARY KEY,
    user_id BIGINT NOT NULL,
    price BIGINT NOT NULL CHECK (price > 0),
    status TEXT NOT NULL CHECK (status IN ('processing', 'paid', 'rejected')),
    reason TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS outbox (
    event_id UUID PRIMARY KEY,
    order_id UUID NOT NULL REFERENCES orders(id),
    payload JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    published_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS ix_outbox_pending ON outbox(created_at) WHERE published_at IS NULL;
