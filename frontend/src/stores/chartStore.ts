// ═══════════════════════════════════════════════════════════════════
// chartStore.ts - Zustand store for chart/klines data.
//
// Manages candlestick data per symbol + active timeframe + indicators.
// Uses Map for O(1) symbol lookups.
// ═══════════════════════════════════════════════════════════════════

import { create } from 'zustand';
import { subscribeWithSelector } from 'zustand/middleware';
import type { KlineDto, Timeframe } from '@/types/market';

// ── Store shape ─────────────────────────────────────────────────

interface ChartState {
  /** Map of symbol -> array of klines */
  klines: Map<string, KlineDto[]>;
  /** Currently selected timeframe */
  activeTimeframe: Timeframe;
  /** Set of active indicator names */
  indicators: Set<string>;

  // Actions
  appendKline: (symbol: string, kline: KlineDto) => void;
  setKlines: (symbol: string, klines: KlineDto[]) => void;
  setTimeframe: (tf: Timeframe) => void;
  toggleIndicator: (indicator: string) => void;
}

// ── Implementation ───────────────────────────────────────────────

const MAX_KLINES_PER_SYMBOL = 1000; // Keep last 1000 candles

export const useChartStore = create<ChartState>()(
  subscribeWithSelector((set, get) => ({
    klines: new Map(),
    activeTimeframe: '1m',
    indicators: new Set(['EMA']),

    /**
     * Appends a single kline to the symbol's array.
     * If the kline is closed (is_closed = true), we append it.
     * If it's still open, we replace the last entry (updating the
     * in-progress candle).
     */
    appendKline: (symbol: string, kline: KlineDto) => {
      const { klines } = get();
      const existing = klines.get(symbol) ?? [];

      let updated: KlineDto[];

      if (kline.is_closed) {
        // Candle is closed: append to array
        updated = [...existing, kline];
      } else {
        // Candle still forming: update the last element
        if (existing.length > 0) {
          updated = [...existing.slice(0, -1), kline];
        } else {
          updated = [kline];
        }
      }

      // Trim to max length
      if (updated.length > MAX_KLINES_PER_SYMBOL) {
        updated = updated.slice(updated.length - MAX_KLINES_PER_SYMBOL);
      }

      const newMap = new Map(klines);
      newMap.set(symbol, updated);
      set({ klines: newMap });
    },

    /**
     * Replace all klines for a symbol (e.g., initial load or timeframe change).
     */
    setKlines: (symbol: string, data: KlineDto[]) => {
      const { klines } = get();
      const truncated = data.length > MAX_KLINES_PER_SYMBOL
        ? data.slice(data.length - MAX_KLINES_PER_SYMBOL)
        : data;

      const newMap = new Map(klines);
      newMap.set(symbol, truncated);
      set({ klines: newMap });
    },

    setTimeframe: (tf: Timeframe) => set({ activeTimeframe: tf }),

    toggleIndicator: (indicator: string) => {
      const { indicators } = get();
      const newSet = new Set(indicators);
      if (newSet.has(indicator)) {
        newSet.delete(indicator);
      } else {
        newSet.add(indicator);
      }
      set({ indicators: newSet });
    },
  }))
);