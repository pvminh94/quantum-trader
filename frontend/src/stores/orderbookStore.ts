// ═══════════════════════════════════════════════════════════════════
// orderbookStore.ts - Zustand store for real-time orderbook data.
//
// Key design decisions for high-frequency updates (100ms+):
// 1. Immutable state snapshots only - no nested mutations.
// 2. Price levels stored as arrays sorted by price.
// 3. Shallow equality checking in selectors to prevent re-renders.
// 4. Optimized for partial updates from exchange diff streams.
// ═══════════════════════════════════════════════════════════════════

import { create } from 'zustand';
import { subscribeWithSelector } from 'zustand/middleware';
import type { OrderBookSnapshotDto, PriceLevelDto } from '@/types/market';

// ── Constants ────────────────────────────────────────────────────

/** Maximum depth levels to store per side. Keeps memory bounded. */
const MAX_DEPTH_LEVELS = 50;

// ── Store shape ─────────────────────────────────────────────────

export interface OrderBookState {
  // Data
  symbol: string | null;
  bids: PriceLevelDto[];
  asks: PriceLevelDto[];
  spread: number;
  spreadPercentage: number;
  midPrice: number;
  lastUpdateId: number | null;

  // Derived state (computed once per update for performance)
  bidTotal: number;
  askTotal: number;
  maxBidQuantity: number;
  maxAskQuantity: number;

  // Connection
  isLoading: boolean;
  error: string | null;

  // Actions
  applySnapshot: (snapshot: OrderBookSnapshotDto) => void;
  clearOrderBook: () => void;
  setLoading: (loading: boolean) => void;
  setError: (error: string | null) => void;
  setSymbol: (symbol: string) => void;
}

// ═══════════════════════════════════════════════════════════════════
// ORDER BOOK STORE
// ═══════════════════════════════════════════════════════════════════

export const useOrderBookStore = create<OrderBookState>()(
  subscribeWithSelector((set, get) => ({
    // ── Initial state ────────────────────────────────────────────
    symbol: null,
    bids: [],
    asks: [],
    spread: 0,
    spreadPercentage: 0,
    midPrice: 0,
    lastUpdateId: null,
    bidTotal: 0,
    askTotal: 0,
    maxBidQuantity: 0,
    maxAskQuantity: 0,
    isLoading: true,
    error: null,

    // ── Actions ──────────────────────────────────────────────────

    /**
     * Applies a full orderbook snapshot from SignalR.
     * Truncates depth to MAX_DEPTH_LEVELS for memory bounds.
     * Pre-computes derived values (totals, max quantities) once.
     */
    applySnapshot: (snapshot: OrderBookSnapshotDto) => {
      // Truncate depth
      const bids = snapshot.bids.slice(0, MAX_DEPTH_LEVELS);
      const asks = snapshot.asks.slice(0, MAX_DEPTH_LEVELS);

      // Pre-compute totals and max quantities for depth visualization
      let bidTotal = 0;
      let maxBidQty = 0;
      for (let i = 0; i < bids.length; i++) {
        bidTotal += bids[i].quantity;
        if (bids[i].quantity > maxBidQty) maxBidQty = bids[i].quantity;
      }

      let askTotal = 0;
      let maxAskQty = 0;
      for (let i = 0; i < asks.length; i++) {
        askTotal += asks[i].quantity;
        if (asks[i].quantity > maxAskQty) maxAskQty = asks[i].quantity;
      }

      set({
        symbol: snapshot.symbol,
        bids,
        asks,
        spread: snapshot.spread,
        spreadPercentage: snapshot.spread_percentage,
        midPrice: snapshot.mid_price,
        lastUpdateId: snapshot.last_update_id,
        bidTotal,
        askTotal,
        maxBidQuantity: maxBidQty,
        maxAskQuantity: maxAskQty,
        isLoading: false,
        error: null,
      });
    },

    clearOrderBook: () =>
      set({
        bids: [],
        asks: [],
        spread: 0,
        spreadPercentage: 0,
        midPrice: 0,
        lastUpdateId: null,
        bidTotal: 0,
        askTotal: 0,
        maxBidQuantity: 0,
        maxAskQuantity: 0,
      }),

    setLoading: (loading: boolean) => set({ isLoading: loading }),
    setError: (error: string | null) => set({ error }),
    setSymbol: (symbol: string) => set({ symbol, isLoading: true }),
  }))
);

// ═══════════════════════════════════════════════════════════════════
// OPTIMIZED SELECTORS
//
// These selectors use shallow equality to prevent re-renders when
// the underlying data hasn't changed (for the specific slice).
// ═══════════════════════════════════════════════════════════════════

/** Selects aggregated best bid/ask for quick display. */
export function selectBestBidAsk(state: OrderBookState) {
  return {
    bestBid: state.bids[0]?.price ?? 0,
    bestAsk: state.asks[0]?.price ?? 0,
    bidQty: state.bids[0]?.quantity ?? 0,
    askQty: state.asks[0]?.quantity ?? 0,
  };
}

/** Selects top N levels for rendering. */
export function selectVisibleLevels(
  state: OrderBookState,
  depth: number = 15
): { bids: PriceLevelDto[]; asks: PriceLevelDto[] } {
  return {
    bids: state.bids.slice(0, depth),
    asks: state.asks.slice(0, depth),
  };
}

/** Selects spread info. */
export function selectSpread(state: OrderBookState) {
  return {
    spread: state.spread,
    spreadPercentage: state.spreadPercentage,
    midPrice: state.midPrice,
  };
}

/** Selects max depth quantities for the depth visualizer bars. */
export function selectDepthMaxes(state: OrderBookState) {
  return {
    maxBid: state.maxBidQuantity,
    maxAsk: state.maxAskQuantity,
    bidTotal: state.bidTotal,
    askTotal: state.askTotal,
  };
}

// ═══════════════════════════════════════════════════════════════════
// TRADE STORE
// ═══════════════════════════════════════════════════════════════════

import type { TradeDto } from '@/types/market';

interface TradeState {
  trades: TradeDto[];
  lastPrice: number | null;
  priceDirection: 'up' | 'down' | 'neutral';

  addTrade: (trade: TradeDto) => void;
  clearTrades: () => void;
}

const MAX_TRADES = 50; // Keep last 50 trades in memory

export const useTradeStore = create<TradeState>()((set, get) => ({
  trades: [],
  lastPrice: null,
  priceDirection: 'neutral',

  addTrade: (trade: TradeDto) => {
    const { trades, lastPrice } = get();
    const direction = lastPrice !== null
      ? trade.price > lastPrice ? 'up'
        : trade.price < lastPrice ? 'down'
          : 'neutral'
      : 'neutral';

    set({
      trades: [trade, ...trades].slice(0, MAX_TRADES),
      lastPrice: trade.price,
      priceDirection: direction,
    });
  },

  clearTrades: () => set({ trades: [], lastPrice: null, priceDirection: 'neutral' }),
}));

// ═══════════════════════════════════════════════════════════════════
// CHART STORE
// ═══════════════════════════════════════════════════════════════════

import type { KlineDto, Timeframe } from '@/types/market';

interface ChartState {
  klines: Map<string, KlineDto[]>; // symbol -> klines
  activeTimeframe: Timeframe;
  indicators: Set<string>; // 'EMA', 'RSI', 'MACD', etc.

  appendKline: (symbol: string, kline: KlineDto) => void;
  setKlines: (symbol: string, klines: KlineDto[]) => void;
  setTimeframe: (tf: Timeframe) => void;
  toggleIndicator: (indicator: string) => void;
}

export const useChartStore = create<ChartState>()((set, get) => ({
  klines: new Map(),
  activeTimeframe: '1m',
  indicators: new Set(['EMA']),

  appendKline: (symbol: string, kline: KlineDto) => {
    const { klines } = get();
    const existing = klines.get(symbol) ?? [];

    const updated = kline.is_closed
      ? [...existing, kline] // Closed candle: append
      : existing.length > 0
        ? [...existing.slice(0, -1), kline] // Open candle: replace last
        : [kline];

    const newMap = new Map(klines);
    newMap.set(symbol, updated);
    set({ klines: newMap });
  },

  setKlines: (symbol: string, klines: KlineDto[]) => {
    const { klines: current } = get();
    const newMap = new Map(current);
    newMap.set(symbol, klines);
    set({ klines: newMap });
  },

  setTimeframe: (tf: Timeframe) => set({ activeTimeframe: tf }),
  toggleIndicator: (indicator: string) => {
    const { indicators } = get();
    const newSet = new Set(indicators);
    if (newSet.has(indicator)) newSet.delete(indicator);
    else newSet.add(indicator);
    set({ indicators: newSet });
  },
}));

// ═══════════════════════════════════════════════════════════════════
// UI STORE
// ═══════════════════════════════════════════════════════════════════

interface UiState {
  layout: {
    orderbookWidth: number;
    chartHeight: number;
    bottomPanelHeight: number;
    activeBottomTab: string;
  };
  sidebarCollapsed: boolean;
  activeSymbol: string;
  exchange: string;

  setLayout: (layout: Partial<UiState['layout']>) => void;
  toggleSidebar: () => void;
  setActiveSymbol: (symbol: string) => void;
  setExchange: (exchange: string) => void;
}

export const useUiStore = create<UiState>()((set) => ({
  layout: {
    orderbookWidth: 380,
    chartHeight: 500,
    bottomPanelHeight: 300,
    activeBottomTab: 'open-orders',
  },
  sidebarCollapsed: false,
  activeSymbol: 'BTCUSDT',
  exchange: 'binance',

  setLayout: (layout) => set((s) => ({ layout: { ...s.layout, ...layout } })),
  toggleSidebar: () => set((s) => ({ sidebarCollapsed: !s.sidebarCollapsed })),
  setActiveSymbol: (symbol) => set({ activeSymbol: symbol }),
  setExchange: (exchange) => set({ exchange }),
}));