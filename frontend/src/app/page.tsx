// ═══════════════════════════════════════════════════════════════════
// Home Page (main dashboard)
//
// Production trading dashboard layout:
// ┌──────────────────────────────────────────────────────────┐
// │ Navbar (Exchange | Balance | Latency | Settings)         │
// ├───────────────────────────┬──────────────────────────────┤
// │                           │ Orderbook Panel              │
// │   Candlestick Chart       │ (Bids | Spread | Asks)       │
// │   (Timeframes + Indicators)│  + Recent Trades           │
// │                           │                              │
// ├───────────────────────────┴──────────────────────────────┤
// │ Multi-tab Bottom Panel (Orders | History | Bots | Assets)│
// └──────────────────────────────────────────────────────────┘
// ═══════════════════════════════════════════════════════════════════

'use client';

import { useState, useCallback } from 'react';
import { Navbar } from '@/components/layout/Navbar';
import { LiveChartWrapper, ChartToolbar } from '@/components/chart/LiveChartWrapper';
import { OrderBookPanel } from '@/components/orderbook/OrderBookPanel';
import { RecentTrades } from '@/components/trades/RecentTrades';
import { useUiStore } from '@/stores/uiStore';
import { useChartStore } from '@/stores/chartStore';
import type { Timeframe } from '@/types/market';

// ── Bottom tabs ─────────────────────────────────────────────────

const BOTTOM_TABS = [
  { id: 'open-orders', label: 'Open Orders' },
  { id: 'order-history', label: 'Order History' },
  { id: 'positions', label: 'Positions' },
  { id: 'assets', label: 'Assets' },
  { id: 'bots', label: 'Bot Telemetry' },
] as const;

// ═══════════════════════════════════════════════════════════════════
// DASHBOARD PAGE
// ═══════════════════════════════════════════════════════════════════

export default function DashboardPage() {
  const { activeSymbol, exchange, layout, setTimeframe: setLayoutTimeframe, setActiveBottomTab } = useUiStore();
  const { activeTimeframe, indicators, setTimeframe, toggleIndicator } = useChartStore();
  const [activeBottomTab, setActiveBottomTabLocal] = useState('open-orders');

  // ── Handlers ──────────────────────────────────────────────────
  const handleTimeframeChange = useCallback(
    (tf: Timeframe) => setTimeframe(tf),
    [setTimeframe]
  );

  const handleToggleIndicator = useCallback(
    (indicator: string) => toggleIndicator(indicator),
    [toggleIndicator]
  );

  const handleBottomTabChange = useCallback(
    (tabId: string) => setActiveBottomTab(tabId),
    [setActiveBottomTab]
  );

  return (
    <>
      {/* ── Top Navbar ───────────────────────────────────────────── */}
      <Navbar symbol={activeSymbol} exchange={exchange} />

      {/* ── Main Grid Area ────────────────────────────────────────── */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left: Chart + Bottom Panel */}
        <div className="flex-1 flex flex-col min-w-0">
          {/* Chart Area */}
          <div
            className="relative flex-1 min-h-[300px]"
            style={{ height: layout.chartHeight }}
          >
            {/* Chart Toolbar (overlaid) */}
            <ChartToolbar
              activeTimeframe={activeTimeframe}
              indicators={indicators}
              onTimeframeChange={handleTimeframeChange}
              onToggleIndicator={handleToggleIndicator}
            />
            {/* Lightweight Charts canvas */}
            <LiveChartWrapper symbol={activeSymbol} height={layout.chartHeight} />
          </div>

          {/* Bottom Panel (Tabs) */}
          <div className="h-[300px] border-t border-[#2b2f36] flex flex-col">
            {/* Tab bar */}
            <div className="flex bg-[#1e2329] border-b border-[#2b2f36]">
              {BOTTOM_TABS.map((tab) => (
                <button
                  key={tab.id}
                  onClick={() => handleBottomTabChange(tab.id)}
                  className={`px-4 py-2 text-[11px] font-sans font-semibold uppercase tracking-wider
                    transition-colors border-r border-[#2b2f36] last:border-r-0
                    ${
                      activeBottomTab === tab.id
                        ? 'bg-[#0b0e11] text-[#f0b90b]'
                        : 'text-[#848e9c] hover:text-[#f0f4f8] hover:bg-[#2b2f36]'
                    }`}
                >
                  {tab.label}
                </button>
              ))}

              {/* Spacer */}
              <div className="flex-1" />

              {/* Symbol count */}
              <span className="px-3 py-2 text-[10px] text-[#848e9c] font-mono">
                {activeSymbol}
              </span>
            </div>

            {/* Tab content */}
            <div className="flex-1 overflow-hidden bg-[#0b0e11]">
              <BottomPanelContent tabId={activeBottomTab} />
            </div>
          </div>
        </div>

        {/* ── Right Sidebar: Orderbook + Recent Trades ──────────── */}
        <div
          className="border-l border-[#2b2f36] flex flex-col"
          style={{ width: layout.orderbookWidth }}
        >
          {/* Orderbook */}
          <div className="flex-1 min-h-[200px]">
            <OrderBookPanel />
          </div>

          {/* Recent Trades */}
          <div className="h-[200px] border-t border-[#2b2f36] flex flex-col">
            <div className="px-3 py-1.5 border-b border-[#2b2f36]">
              <h3 className="text-[#848e9c] uppercase tracking-wider font-semibold text-[10px]">
                Recent Trades
              </h3>
            </div>
            <div className="flex-1 overflow-hidden">
              <RecentTrades />
            </div>
          </div>
        </div>
      </div>
    </>
  );
}

// ═══════════════════════════════════════════════════════════════════
// BOTTOM PANEL CONTENT
// ═══════════════════════════════════════════════════════════════════

function BottomPanelContent({ tabId }: { tabId: string }) {
  switch (tabId) {
    case 'open-orders':
      return <OpenOrdersTab />;
    case 'order-history':
      return <OrderHistoryTab />;
    case 'positions':
      return <PositionsTab />;
    case 'assets':
      return <AssetsTab />;
    case 'bots':
      return <BotTelemetryTab />;
    default:
      return null;
  }
}

// ── Placeholder tab content ──────────────────────────────────────
// In production, each of these would be a full virtualized table component.

function OpenOrdersTab() {
  return (
    <div className="p-4 text-[#848e9c] text-xs font-mono">
      <div className="grid grid-cols-8 gap-4 text-[10px] uppercase tracking-wider text-[#5e6673] pb-2 border-b border-[#1e2329]">
        <span>Time</span>
        <span>Pair</span>
        <span>Side</span>
        <span>Type</span>
        <span>Price</span>
        <span>Qty</span>
        <span>Filled</span>
        <span>Status</span>
      </div>
      <div className="py-8 text-center text-[#5e6673]">No open orders</div>
    </div>
  );
}

function OrderHistoryTab() {
  return (
    <div className="p-4 text-[#848e9c] text-xs font-mono">
      <div className="py-8 text-center text-[#5e6673]">Order history loading...</div>
    </div>
  );
}

function PositionsTab() {
  return (
    <div className="p-4 text-[#848e9c] text-xs font-mono">
      <div className="py-8 text-center text-[#5e6673]">No open positions</div>
    </div>
  );
}

function AssetsTab() {
  return (
    <div className="p-4 text-[#848e9c] text-xs font-mono">
      <div className="py-8 text-center text-[#5e6673]">Asset balances loading...</div>
    </div>
  );
}

function BotTelemetryTab() {
  return (
    <div className="p-4 text-[#848e9c] text-xs font-mono">
      <div className="py-8 text-center text-[#5e6673]">Bot telemetry stream connecting...</div>
    </div>
  );
}