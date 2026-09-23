-- Existing profiles are preserved, but cannot log in without credentials.
CREATE TABLE IF NOT EXISTS user_credentials
(
    user_id       BIGINT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    password_hash TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS user_sessions
(
    token_hash CHAR(64) PRIMARY KEY,
    user_id    BIGINT NOT NULL REFERENCES user_credentials(user_id) ON DELETE CASCADE,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS user_sessions_user_id_idx ON user_sessions(user_id);
CREATE INDEX IF NOT EXISTS user_sessions_expires_at_idx ON user_sessions(expires_at);

INSERT INTO schema_migrations(version)
VALUES ('002_authentication')
ON CONFLICT (version) DO NOTHING;
