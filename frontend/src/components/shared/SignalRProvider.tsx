// ═══════════════════════════════════════════════════════════════════
// SignalRProvider.tsx - Client-side provider for SignalR connection.
//
// Initializes the SignalR connection on app mount and subscribes to
// the default symbol's data stream. All child components access
// market data through Zustand stores.
// ═══════════════════════════════════════════════════════════════════

'use client';

import { useEffect, useRef } from 'react';
import { useSignalR } from '@/hooks/useSignalR';
import { useUiStore } from '@/stores/uiStore';

interface SignalRProviderProps {
  children: React.ReactNode;
}

export function SignalRProvider({ children }: SignalRProviderProps) {
  const activeSymbol = useUiStore((s) => s.activeSymbol);
  const { status, latencyMs, connect, disconnect, subscribeSymbol } = useSignalR({
    hubUrl: `${process.env.NEXT_PUBLIC_API_URL ?? ''}/hubs/market-data`,
    symbol: activeSymbol,
    autoConnect: true,
  });

  // Track connection status for the UI
  useEffect(() => {
    useUiStore.setState((s) => ({
      signalRStatus: status,
      signalRLatencyMs: latencyMs,
    }));
  }, [status, latencyMs]);

  // Resubscribe when symbol changes
  useEffect(() => {
    if (status === 'connected' && activeSymbol) {
      subscribeSymbol(activeSymbol);
    }
  }, [activeSymbol, status, subscribeSymbol]);

  return <>{children}</>;
}