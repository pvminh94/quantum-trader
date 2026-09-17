// ═══════════════════════════════════════════════════════════════════
// useSignalR.ts - React hook for SignalR connection lifecycle.
//
// Connects to the SignalR hub and subscribes to market data streams.
// Registers all data handlers that feed into Zustand stores.
// ═══════════════════════════════════════════════════════════════════

'use client';

import { useEffect, useCallback, useRef, useState } from 'react';
import { signalRManager } from '@/lib/signalr-connection';
import { useOrderBookStore } from '@/stores/orderbookStore';
import { useTradeStore } from '@/stores/tradeStore';
import type { OrderBookSnapshotDto, TradeDto, SignalRStatus } from '@/types/market';

interface UseSignalROptions {
  hubUrl?: string;
  symbol?: string;
  autoConnect?: boolean;
}

interface UseSignalRResult {
  status: SignalRStatus;
  latencyMs: number | null;
  error: string | null;
  connect: () => Promise<void>;
  disconnect: () => Promise<void>;
  subscribeSymbol: (symbol: string) => Promise<void>;
  unsubscribeSymbol: (symbol: string) => Promise<void>;
}

/**
 * Hook that manages SignalR connection and routes data to Zustand stores.
 * Call once at the root layout level; child components access data via stores.
 */
export function useSignalR(options: UseSignalROptions = {}): UseSignalRResult {
  const {
    hubUrl = '/hubs/market-data',
    symbol: initialSymbol,
    autoConnect = true,
  } = options;

  const [status, setStatus] = useState<SignalRStatus>('disconnected');
  const [latencyMs, setLatencyMs] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Store references to avoid stale closures in handlers
  const applySnapshot = useOrderBookStore((s) => s.applySnapshot);
  const addTrade = useTradeStore((s) => s.addTrade);
  const subscribedSymbolRef = useRef<string | undefined>(initialSymbol);

  // ── Handle OrderBook updates ────────────────────────────────────
  const handleOrderBook = useCallback(
    (snapshot: OrderBookSnapshotDto) => {
      applySnapshot(snapshot);
    },
    [applySnapshot]
  );

  // ── Handle Trade updates ────────────────────────────────────────
  const handleTrade = useCallback(
    (trade: TradeDto) => {
      addTrade(trade);
    },
    [addTrade]
  );

  // ── Connect function ────────────────────────────────────────────
  const connect = useCallback(async () => {
    try {
      setError(null);
      await signalRManager.start(hubUrl);

      // Register message handlers
      const conn = signalRManager.connection;
      if (conn) {
        conn.on('OrderBookUpdate', handleOrderBook);
        conn.on('TradeUpdate', handleTrade);

        // Subscribe to symbol if provided
        if (subscribedSymbolRef.current) {
          await conn.invoke('SubscribeSymbol', subscribedSymbolRef.current);
        }
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      setError(msg);
      console.error('[useSignalR] Connection error:', msg);
    }
  }, [hubUrl, handleOrderBook, handleTrade]);

  // ── Disconnect function ─────────────────────────────────────────
  const disconnect = useCallback(async () => {
    const conn = signalRManager.connection;
    if (conn) {
      conn.off('OrderBookUpdate', handleOrderBook);
      conn.off('TradeUpdate', handleTrade);
    }
    await signalRManager.stop();
  }, [handleOrderBook, handleTrade]);

  // ── Subscribe to a symbol ───────────────────────────────────────
  const subscribeSymbol = useCallback(async (symbol: string) => {
    subscribedSymbolRef.current = symbol;
    const conn = signalRManager.connection;
    if (conn) {
      try {
        await conn.invoke('SubscribeSymbol', symbol);
      } catch (err) {
        console.error('[useSignalR] Subscribe failed:', err);
      }
    }
  }, []);

  // ── Unsubscribe from a symbol ───────────────────────────────────
  const unsubscribeSymbol = useCallback(async (symbol: string) => {
    const conn = signalRManager.connection;
    if (conn) {
      try {
        await conn.invoke('UnsubscribeSymbol', symbol);
      } catch {
        // Best effort
      }
    }
  }, []);

  // ── Auto-connect on mount ───────────────────────────────────────
  useEffect(() => {
    if (autoConnect) {
      connect();
    }

    // Listen for status changes from the connection manager
    const unsubStatus = signalRManager.onStatusChange((newStatus, newLatency) => {
      setStatus(newStatus);
      setLatencyMs(newLatency);
    });

    return () => {
      unsubStatus();
      if (autoConnect) {
        disconnect();
      }
    };
  }, [autoConnect, connect, disconnect]);

  return {
    status,
    latencyMs,
    error,
    connect,
    disconnect,
    subscribeSymbol,
    unsubscribeSymbol,
  };
}