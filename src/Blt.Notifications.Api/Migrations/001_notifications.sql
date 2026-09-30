CREATE TABLE IF NOT EXISTS notifications (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL UNIQUE,
    order_id UUID UNIQUE,
    user_id BIGINT NOT NULL,
    email TEXT NOT NULL,
    subject TEXT NOT NULL,
    body TEXT NOT NULL,
    outcome TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_notifications_user ON notifications(user_id, created_at);
