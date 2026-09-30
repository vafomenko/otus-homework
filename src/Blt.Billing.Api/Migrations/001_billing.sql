CREATE TABLE IF NOT EXISTS accounts (
    user_id BIGINT PRIMARY KEY CHECK (user_id > 0),
    balance BIGINT NOT NULL DEFAULT 0 CHECK (balance >= 0 AND balance <= 9000000000000000)
);
CREATE TABLE IF NOT EXISTS operations (
    user_id BIGINT NOT NULL REFERENCES accounts(user_id),
    operation_id UUID NOT NULL,
    amount BIGINT NOT NULL CHECK (amount > 0),
    kind TEXT NOT NULL CHECK (kind IN ('deposit', 'withdrawal', 'charge')),
    status TEXT NOT NULL CHECK (status IN ('succeeded', 'rejected')),
    balance BIGINT NOT NULL,
    reason TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, operation_id)
);
