-- ═══════════════════════════════════════════════════════════════════
-- init-db.sql - PostgreSQL schema initialization.
--
-- Creates all tables for the trading engine:
-- - Users & API credentials (encrypted)
-- - Orders & trade history
-- - Bot configurations & state
-- - Account balances / positions
-- ═══════════════════════════════════════════════════════════════════

-- ── Extensions ─────────────────────────────────────────────────

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ── Users ──────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS users (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    email           VARCHAR(255) UNIQUE NOT NULL,
    password_hash   VARCHAR(255) NOT NULL,
    display_name    VARCHAR(100),
    role            VARCHAR(20) DEFAULT 'user',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_users_email ON users(email);

-- ── Exchange API Credentials (encrypted at rest) ───────────────

CREATE TABLE IF NOT EXISTS exchange_credentials (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    exchange_name   VARCHAR(20) NOT NULL,
    api_key_encrypted   TEXT NOT NULL,
    api_secret_encrypted TEXT NOT NULL,
    is_active       BOOLEAN DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, exchange_name)
);

CREATE INDEX idx_exchange_creds_user ON exchange_credentials(user_id);

-- ── Orders ─────────────────────────────────────────────────────

CREATE TYPE order_side AS ENUM ('buy', 'sell');
CREATE TYPE order_type AS ENUM ('market', 'limit', 'stop_market', 'stop_limit');
CREATE TYPE order_status AS ENUM ('pending', 'open', 'partially_filled', 'filled', 'canceled', 'failed');
CREATE TYPE time_in_force AS ENUM ('gtc', 'ioc', 'fok', 'gtd', 'day');

CREATE TABLE IF NOT EXISTS orders (
    id                  UUID PRIMARY KEY,
    user_id             UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    exchange_order_id   VARCHAR(64),
    idempotency_key     VARCHAR(64) UNIQUE NOT NULL,
    symbol              VARCHAR(20) NOT NULL,
    side                order_side NOT NULL,
    type                order_type NOT NULL,
    status              order_status NOT NULL DEFAULT 'pending',
    price               DECIMAL(30,8),
    stop_price          DECIMAL(30,8),
    quantity            DECIMAL(30,8) NOT NULL,
    filled_quantity     DECIMAL(30,8) NOT NULL DEFAULT 0,
    average_price       DECIMAL(30,8),
    quote_quantity_filled DECIMAL(30,8) NOT NULL DEFAULT 0,
    time_in_force       time_in_force NOT NULL DEFAULT 'gtc',
    retry_count         INT NOT NULL DEFAULT 0,
    failure_reason      TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_orders_user_id ON orders(user_id);
CREATE INDEX idx_orders_symbol ON orders(symbol);
CREATE INDEX idx_orders_status ON orders(status);
CREATE INDEX idx_orders_created_at ON orders(created_at DESC);
CREATE INDEX idx_orders_idempotency ON orders(idempotency_key);

-- ── Trades (fill history) ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS trades (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    order_id        UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    symbol          VARCHAR(20) NOT NULL,
    side            order_side NOT NULL,
    price           DECIMAL(30,8) NOT NULL,
    quantity        DECIMAL(30,8) NOT NULL,
    commission      DECIMAL(30,8) DEFAULT 0,
    commission_asset VARCHAR(10),
    trade_timestamp TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_trades_order_id ON trades(order_id);
CREATE INDEX idx_trades_user_id ON trades(user_id);
CREATE INDEX idx_trades_symbol ON trades(symbol);
CREATE INDEX idx_trades_timestamp ON trades(trade_timestamp DESC);

-- ── Bot Configurations ─────────────────────────────────────────

CREATE TABLE IF NOT EXISTS bot_configs (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id                 UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name                    VARCHAR(100) NOT NULL,
    symbol                  VARCHAR(20) NOT NULL,
    scan_interval_ms        INT NOT NULL DEFAULT 1000,
    enabled                 BOOLEAN DEFAULT false,

    -- Strategy
    use_ema_crossover       BOOLEAN DEFAULT true,
    fast_ema_period         INT DEFAULT 9,
    slow_ema_period         INT DEFAULT 21,
    use_rsi                 BOOLEAN DEFAULT false,
    rsi_period              INT DEFAULT 14,
    rsi_oversold_threshold  DECIMAL(5,2) DEFAULT 30.00,
    rsi_overbought_threshold DECIMAL(5,2) DEFAULT 70.00,

    -- Risk
    risk_mode               VARCHAR(20) DEFAULT 'fixed_percentage',
    risk_per_trade_percent  DECIMAL(5,2) DEFAULT 2.00,
    account_balance         DECIMAL(30,8) DEFAULT 10000.00,
    win_rate                DECIMAL(5,4) DEFAULT 0.5500,
    payoff_ratio            DECIMAL(10,4) DEFAULT 1.5000,
    max_consecutive_losses  INT DEFAULT 5,
    max_daily_loss          DECIMAL(30,8) DEFAULT 500.00,
    max_daily_trades        INT DEFAULT 50,
    max_exposure            DECIMAL(30,8) DEFAULT 5000.00,
    max_drawdown_percent    DECIMAL(5,2) DEFAULT 15.00,

    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bot_configs_user ON bot_configs(user_id);
CREATE INDEX idx_bot_configs_symbol ON bot_configs(symbol);

-- ── Positions (open/closed) ────────────────────────────────────

CREATE TYPE position_side AS ENUM ('long', 'short');
CREATE TYPE position_status AS ENUM ('open', 'closed');

CREATE TABLE IF NOT EXISTS positions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    symbol          VARCHAR(20) NOT NULL,
    side            position_side NOT NULL,
    status          position_status NOT NULL DEFAULT 'open',
    quantity        DECIMAL(30,8) NOT NULL,
    entry_price     DECIMAL(30,8) NOT NULL,
    current_price   DECIMAL(30,8),
    liquidation_price DECIMAL(30,8),
    margin          DECIMAL(30,8),
    leverage        DECIMAL(10,2) DEFAULT 1.00,
    unrealized_pnl  DECIMAL(30,8) DEFAULT 0,
    realized_pnl    DECIMAL(30,8) DEFAULT 0,
    opened_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    closed_at       TIMESTAMPTZ
);

CREATE INDEX idx_positions_user_id ON positions(user_id);
CREATE INDEX idx_positions_symbol ON positions(symbol);
CREATE INDEX idx_positions_status ON positions(status);

-- ── Account Balances (snapshot) ─────────────────────────────────

CREATE TABLE IF NOT EXISTS account_balances (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    asset           VARCHAR(10) NOT NULL,
    free            DECIMAL(30,8) NOT NULL DEFAULT 0,
    locked          DECIMAL(30,8) NOT NULL DEFAULT 0,
    usd_value       DECIMAL(30,8) DEFAULT 0,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, asset)
);

CREATE INDEX idx_account_balances_user ON account_balances(user_id);

-- ── Audit Log (for compliance / debugging) ─────────────────────

CREATE TABLE IF NOT EXISTS audit_log (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID REFERENCES users(id),
    action          VARCHAR(50) NOT NULL,
    entity_type     VARCHAR(50),
    entity_id       UUID,
    details         JSONB,
    ip_address      INET,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_audit_log_user ON audit_log(user_id);
CREATE INDEX idx_audit_log_action ON audit_log(action);
CREATE INDEX idx_audit_log_created_at ON audit_log(created_at DESC);

-- ── Trigger: auto-update updated_at ────────────────────────────

CREATE OR REPLACE FUNCTION update_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_users_updated_at
    BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();

CREATE TRIGGER trg_orders_updated_at
    BEFORE UPDATE ON orders
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();

CREATE TRIGGER trg_bot_configs_updated_at
    BEFORE UPDATE ON bot_configs
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();

-- ═══════════════════════════════════════════════════════════════════
-- END OF SCHEMA
-- ═══════════════════════════════════════════════════════════════════