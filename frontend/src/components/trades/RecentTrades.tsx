// ═══════════════════════════════════════════════════════════════════
// RecentTrades.tsx - Real-time recent trades stream.
//
// Displays the most recent trades as they arrive from SignalR.
// Features:
// - Price flash color (green for buys, red for sells)
// - Time ago display
// - Auto-scroll with threshold
// - Memoized rows to prevent re-renders
// ═══════════════════════════════════════════════════════════════════

'use client';

import React, { useRef, useEffect, memo, useMemo } from 'react';
import { shallow } from 'zustand/shallow';
import { useTradeStore, selectRecentTrades } from '@/stores/tradeStore';
import type { TradeDto } from '@/types/market';

// ═══════════════════════════════════════════════════════════════════
// MAIN COMPONENT
// ═══════════════════════════════════════════════════════════════════

/**
 * Displays the most recent trades in a compact scrolling list.
 */
export function RecentTrades() {
  const recentTrades = useTradeStore(
    (s) => selectRecentTrades(s, 50),
    shallow
  );

  const containerRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to top on new trades (but only if user hasn't scrolled up)
  const isAtTopRef = useRef(true);

  useEffect(() => {
    if (isAtTopRef.current && containerRef.current) {
      containerRef.current.scrollTop = 0;
    }
  }, [recentTrades]);

  const handleScroll = () => {
    if (containerRef.current) {
      isAtTopRef.current = containerRef.current.scrollTop < 50;
    }
  };

  if (recentTrades.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-[#5e6673] text-[10px] font-mono">
        Waiting for trades...
      </div>
    );
  }

  return (
    <div
      ref={containerRef}
      onScroll={handleScroll}
      className="h-full overflow-y-auto scrollbar-thin"
    >
      {/* Header */}
      <div className="flex text-[10px] text-[#5e6673] px-3 py-1 border-b border-[#1e2329] sticky top-0 bg-[#0b0e11]/90 backdrop-blur-sm">
        <span className="w-16 text-left">Price</span>
        <span className="w-16 text-right">Qty</span>
        <span className="flex-1 text-right">Time</span>
      </div>

      {/* Rows */}
      {recentTrades.map((trade) => (
        <TradeRow key={trade.trade_id} trade={trade} />
      ))}
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════════
// SINGLE TRADE ROW (Highly memoized)
// ═══════════════════════════════════════════════════════════════════

interface TradeRowProps {
  trade: TradeDto;
}

const TradeRow = memo(function TradeRow({ trade }: TradeRowProps) {
  const isBuy = !trade.is_buyer_maker; // buyer maker = sell taker
  const priceColor = isBuy ? 'text-green-500' : 'text-red-500';
  const timeAgo = getTimeAgo(trade.timestamp_ms);

  return (
    <div className="flex items-center h-[20px] px-3 text-[11px] font-mono hover:bg-[#1e2329] transition-colors">
      <span className={`w-16 text-left font-medium tabular-nums ${priceColor}`}>
        {trade.price.toFixed(2)}
      </span>
      <span className="w-16 text-right text-[#f0f4f8] tabular-nums">
        {trade.quantity.toFixed(5)}
      </span>
      <span className="flex-1 text-right text-[#5e6673] tabular-nums">
        {timeAgo}
      </span>
    </div>
  );
});

// ═══════════════════════════════════════════════════════════════════
// UTILITY
// ═══════════════════════════════════════════════════════════════════

/**
 * Returns a human-readable "time ago" string from a Unix timestamp (ms).
 * Cached computation: not performance-critical for trade display.
 */
function getTimeAgo(timestampMs: number): string {
  const diffSeconds = Math.floor((Date.now() - timestampMs) / 1000);

  if (diffSeconds < 5) return 'now';
  if (diffSeconds < 60) return `${diffSeconds}s`;
  if (diffSeconds < 3600) return `${Math.floor(diffSeconds / 60)}m`;

  const hours = Math.floor(diffSeconds / 3600);
  return `${hours}h`;
}