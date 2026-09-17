// ═══════════════════════════════════════════════════════════════════
// tradeStore.ts - Zustand store for real-time trades feed.
//
// Maintains a circular buffer of recent trades with price direction
// tracking for visual flash effects.
// ═══════════════════════════════════════════════════════════════════

import { create } from 'zustand';
import type { TradeDto } from '@/types/market';

const MAX_TRADES = 100;

interface TradeState {
  trades: TradeDto[];
  lastPrice: number | null;
  priceDirection: 'up' | 'down' | 'neutral';

  addTrade: (trade: TradeDto) => void;
  setTrades: (trades: TradeDto[]) => void;
  clearTrades: () => void;
}

export const useTradeStore = create<TradeState>()((set, get) => ({
  trades: [],
  lastPrice: null,
  priceDirection: 'neutral',

  /**
   * Add a trade to the front of the list.
   * Tracks price direction (up/down/neutral) for the flash effect.
   * Trims to MAX_TRADES to keep memory bounded.
   */
  addTrade: (trade: TradeDto) => {
    const { trades, lastPrice } = get();

    const direction = lastPrice !== null
      ? trade.price > lastPrice
        ? 'up'
        : trade.price < lastPrice
          ? 'down'
          : 'neutral'
      : 'neutral';

    set({
      trades: [trade, ...trades].slice(0, MAX_TRADES),
      lastPrice: trade.price,
      priceDirection: direction,
    });
  },

  /** Bulk set trades (e.g., initial load). */
  setTrades: (trades: TradeDto[]) =>
    set({
      trades: trades.slice(0, MAX_TRADES),
      lastPrice: trades[0]?.price ?? null,
      priceDirection: 'neutral',
    }),

  clearTrades: () =>
    set({ trades: [], lastPrice: null, priceDirection: 'neutral' }),
}));

/**
 * Selector for the last N trades (for rendering).
 * Returns a new array slice on every call; consumer should use
 * React.memo or shallow comparison.
 */
export function selectRecentTrades(state: TradeState, count: number): TradeDto[] {
  return state.trades.slice(0, count);
}