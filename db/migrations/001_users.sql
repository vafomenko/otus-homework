-- Первоначальная идемпотентная миграция пользователей.
CREATE TABLE IF NOT EXISTS schema_migrations
(
    version    VARCHAR(128) PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS users
(
    id         BIGSERIAL PRIMARY KEY,
    username   VARCHAR(256) NOT NULL,
    first_name VARCHAR(256) NOT NULL,
    last_name  VARCHAR(256) NOT NULL,
    email      VARCHAR(320) NOT NULL,
    phone      VARCHAR(64)
);

CREATE UNIQUE INDEX IF NOT EXISTS users_username_uq ON users (username);
CREATE UNIQUE INDEX IF NOT EXISTS users_email_uq ON users (email);

INSERT INTO schema_migrations(version)
VALUES ('001_users')
ON CONFLICT (version) DO NOTHING;
