// ═══════════════════════════════════════════════════════════════════
// signals.ts - Type definitions for all real-time data structures.
// Mirrors the backend DTOs exactly for type-safe SignalR deserialization.
// ═══════════════════════════════════════════════════════════════════

// ── Market Data ──────────────────────────────────────────────────

export interface OrderBookSnapshotDto {
  symbol: string;
  timestamp_ms: number;
  last_update_id: number;
  bids: PriceLevelDto[];
  asks: PriceLevelDto[];
  spread: number;
  spread_percentage: number;
  mid_price: number;
}

export interface PriceLevelDto {
  price: number;
  quantity: number;
  order_count: number;
}

export interface TradeDto {
  symbol: string;
  trade_id: string;
  price: number;
  quantity: number;
  is_buyer_maker: boolean; // true = sell, false = buy
  timestamp_ms: number;
}

export interface TickerDto {
  symbol: string;
  open_price: number;
  high_price: number;
  low_price: number;
  last_price: number;
  volume: number;
  quote_volume: number;
  price_change_percent: number;
  timestamp_ms: number;
}

export interface KlineDto {
  symbol: string;
  interval: string; // "1s", "1m", "5m", "1h", "1d"
  open_time_ms: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  is_closed: boolean;
}

// ── Orders ──────────────────────────────────────────────────────

export interface OrderDto {
  id: string; // UUID
  symbol: string;
  side: string;
  type: string;
  status: string;
  quantity: number;
  filled_quantity: number;
  price: number | null;
  average_price: number | null;
  created_at_utc: string;
  updated_at_utc: string;
  failure_reason: string | null;
}

// ── Account ─────────────────────────────────────────────────────

export interface BalanceDto {
  asset: string;
  free: number;
  locked: number;
  total: number;
  usd_value: number;
}

// ── Bot Telemetry ───────────────────────────────────────────────

export interface BotTelemetryDto {
  bot_id: string;
  bot_name: string;
  status: string;
  message: string | null;
  pnl: number | null;
  win_rate: number | null;
  trades_today: number;
  timestamp_utc: string;
}

// ── Trading/Chart types ─────────────────────────────────────────

export type Timeframe = '1s' | '1m' | '5m' | '15m' | '30m' | '1h' | '4h' | '1d';

// ── UI State types ──────────────────────────────────────────────

export interface TabConfig {
  id: string;
  label: string;
  icon?: string;
}

export interface LayoutConfig {
  orderbookWidth: number;
  chartHeight: number;
  bottomPanelHeight: number;
  activeBottomTab: string;
}

// ── SignalR connection state ────────────────────────────────────

export type SignalRStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'error';

export interface SignalRState {
  status: SignalRStatus;
  latencyMs: number | null;
  lastPingAt: number | null;
  reconnectAttempts: number;
  error: string | null;
}