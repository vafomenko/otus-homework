CREATE TABLE IF NOT EXISTS charge_refunds (
    user_id BIGINT NOT NULL REFERENCES accounts(user_id),
    order_id UUID NOT NULL,
    amount BIGINT NOT NULL CHECK (amount > 0),
    refunded_amount BIGINT NOT NULL CHECK (refunded_amount >= 0 AND refunded_amount <= amount),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, order_id)
);
