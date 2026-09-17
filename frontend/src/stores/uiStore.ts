// ═══════════════════════════════════════════════════════════════════
// uiStore.ts - Zustand store for UI layout and preferences.
//
// Manages:
// - Layout dimensions (draggable/resizable panel sizes)
// - Active symbol and exchange selection
// - Bottom tab selection
// - SignalR connection status display
// - Theme (dark mode only — this is a pro trader dashboard)
// ═══════════════════════════════════════════════════════════════════

import { create } from 'zustand';
import { subscribeWithSelector } from 'zustand/middleware';
import type { SignalRStatus } from '@/types/market';

interface UiState {
  // Layout
  layout: {
    orderbookWidth: number;
    chartHeight: number;
    bottomPanelHeight: number;
  };
  activeBottomTab: string;

  // Trading context
  activeSymbol: string;
  exchange: string;

  // Connection status (updated by SignalRProvider)
  signalRStatus: SignalRStatus;
  signalRLatencyMs: number | null;

  // Actions
  setLayout: (layout: Partial<UiState['layout']>) => void;
  setActiveBottomTab: (tabId: string) => void;
  setActiveSymbol: (symbol: string) => void;
  setExchange: (exchange: string) => void;
}

export const useUiStore = create<UiState>()(
  subscribeWithSelector((set) => ({
    layout: {
      orderbookWidth: 380,
      chartHeight: 500,
      bottomPanelHeight: 300,
    },
    activeBottomTab: 'open-orders',
    activeSymbol: 'BTCUSDT',
    exchange: 'binance',
    signalRStatus: 'disconnected',
    signalRLatencyMs: null,

    setLayout: (layout) =>
      set((s) => ({ layout: { ...s.layout, ...layout } })),

    setActiveBottomTab: (tabId: string) =>
      set({ activeBottomTab: tabId }),

    setActiveSymbol: (symbol: string) =>
      set({ activeSymbol: symbol.toUpperCase() }),

    setExchange: (exchange: string) =>
      set({ exchange: exchange.toLowerCase() }),
  }))
);