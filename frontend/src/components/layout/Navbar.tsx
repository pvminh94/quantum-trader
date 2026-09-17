// ═══════════════════════════════════════════════════════════════════
// Navbar.tsx - Top navigation bar.
//
// Professional trading toolbar with:
// - Exchange selector
// - Current symbol/price ticker
// - Account balance summary
// - Live API status indicator (latency in ms)
// - Global kill switch button
// - Settings/theme (dark only)
// ═══════════════════════════════════════════════════════════════════

'use client';

import React, { memo } from 'react';
import { useUiStore } from '@/stores/uiStore';
import { useOrderBookStore } from '@/stores/orderbookStore';

interface NavbarProps {
  symbol: string;
  exchange: string;
}

/**
 * Top navigation bar with trading controls and status indicators.
 * Memoized to avoid re-renders on unrelated state changes.
 */
export const Navbar = memo(function Navbar({ symbol, exchange }: NavbarProps) {
  const { signalRStatus, signalRLatencyMs } = useUiStore();
  const lastPrice = useOrderBookStore((s) => s.midPrice);

  const statusColor =
    signalRStatus === 'connected'
      ? 'bg-green-500'
      : signalRStatus === 'connecting' || signalRStatus === 'reconnecting'
        ? 'bg-yellow-500'
        : 'bg-red-500';

  const statusLabel =
    signalRStatus === 'connected'
      ? 'Live'
      : signalRStatus === 'connecting'
        ? 'Connecting'
        : signalRStatus === 'reconnecting'
          ? 'Reconnecting'
          : 'Offline';

  return (
    <header className="h-10 bg-[#1e2329] border-b border-[#2b2f36] flex items-center px-3 gap-4 shrink-0">
      {/* ── Logo / Brand ────────────────────────────────────────── */}
      <div className="flex items-center gap-2 mr-2">
        <span className="text-[#f0b90b] font-bold text-sm font-sans tracking-wider">
          ▲ QUANTUM
        </span>
        <span className="text-[#5e6673] text-[10px] font-mono uppercase">Trader</span>
      </div>

      {/* ── Exchange Selector ────────────────────────────────────── */}
      <select
        value={exchange}
        onChange={(e) => useUiStore.getState().setExchange(e.target.value)}
        className="bg-[#0b0e11] text-[#f0f4f8] text-xs px-2 py-1 rounded border border-[#2b2f36]
          focus:outline-none focus:border-[#f0b90b] cursor-pointer font-sans"
      >
        <option value="binance">Binance</option>
        <option value="mexc">MEXC Global</option>
        <option value="bybit">Bybit</option>
      </select>

      {/* ── Symbol Display ─────────────────────────────────────── */}
      <div className="flex items-center gap-1.5 px-3 border-x border-[#2b2f36]">
        <span className="text-[#f0f4f8] font-bold text-sm font-mono">
          {symbol?.replace('USDT', '/USDT') ?? '---'}
        </span>
        {lastPrice > 0 && (
          <span className="text-[#f0f4f8] font-mono text-sm tabular-nums">
            {lastPrice.toFixed(2)}
          </span>
        )}
      </div>

      {/* ── Spacer ────────────────────────────────────────────────── */}
      <div className="flex-1" />

      {/* ── API Status ────────────────────────────────────────────── */}
      <div className="flex items-center gap-1.5">
        <div className={`w-1.5 h-1.5 rounded-full ${statusColor}`} />
        <span className="text-[#848e9c] text-[10px] font-mono uppercase">{statusLabel}</span>
        {signalRLatencyMs !== null && (
          <span className="text-[#848e9c] text-[10px] font-mono tabular-nums">
            {signalRLatencyMs}ms
          </span>
        )}
        <span className="text-[#5e6673] text-[10px] font-mono ml-1">
          {exchange?.toUpperCase()}
        </span>
      </div>

      {/* ── Balance Ticker ─────────────────────────────────────── */}
      <div className="px-3 border-l border-[#2b2f36] flex items-center gap-2">
        <span className="text-[#848e9c] text-[10px]">Balance</span>
        <span className="text-[#f0f4f8] text-xs font-mono tabular-nums">$12,345.67</span>
        <span className="text-[#0ecb81] text-[10px]">+2.34%</span>
      </div>

      {/* ── Emergency Kill Switch ──────────────────────────────── */}
      <button
        className="px-2 py-1 bg-red-600 hover:bg-red-700 text-white text-[10px] font-bold
          rounded transition-colors uppercase tracking-wider"
        title="Emergency stop all bots and cancel all orders"
        onClick={() => {
          if (confirm('⚠️ EMERGENCY KILL: Stop all bots and cancel all orders?')) {
            // In production: POST /api/v1/admin/kill
            console.warn('EMERGENCY KILL ACTIVATED');
          }
        }}
      >
        ⚡ Kill
      </button>

      {/* ── Settings ───────────────────────────────────────────── */}
      <button
        className="px-2 py-1 text-[#848e9c] hover:text-[#f0f4f8] text-xs transition-colors"
        title="Settings"
      >
        ⚙
      </button>
    </header>
  );
});