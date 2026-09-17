// ═══════════════════════════════════════════════════════════════════
// signalr-connection.ts - SignalR client connection manager.
//
// Manages the WebSocket connection lifecycle with automatic
// reconnection using exponential backoff. Designed for high-frequency
// market data where reconnection speed is critical.
//
// Uses @microsoft/signalr library with custom reconnection policy.
// ═══════════════════════════════════════════════════════════════════

import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
  type IRetryPolicy,
  type RetryContext,
} from '@microsoft/signalr';
import type { SignalRStatus } from '@/types/market';

// ── Configuration ────────────────────────────────────────────────

const RECONNECT_DELAYS_MS = [0, 1_000, 2_000, 5_000, 10_000, 30_000];
const MAX_RECONNECT_ATTEMPTS = 10;
const CONNECTION_TIMEOUT_MS = 15_000;
const KEEP_ALIVE_INTERVAL_MS = 15_000;
const SERVER_TIMEOUT_MS = 30_000;

// ── Custom retry policy ──────────────────────────────────────────

/**
 * SignalR automatic reconnect policy with exponential backoff.
 * Tries immediate reconnect, then escalates to 30s max delay.
 */
class ReconnectPolicy implements IRetryPolicy {
  private _attempt = 0;

  nextRetryDelayInMilliseconds(retryContext: RetryContext): number | null {
    this._attempt = retryContext.previousRetryCount;

    if (this._attempt >= RECONNECT_DELAYS_MS.length) {
      // After exhausting exponential delays, keep trying at 30s intervals
      return RECONNECT_DELAYS_MS[RECONNECT_DELAYS_MS.length - 1];
    }

    return RECONNECT_DELAYS_MS[this._attempt];
  }
}

// ── Connection manager class ─────────────────────────────────────

export type MessageHandler = (message: unknown) => void;

/**
 * Singleton-style SignalR connection manager.
 * Multiple consumers share one connection; it's reference-counted.
 */
export class SignalRConnectionManager {
  private static _instance: SignalRConnectionManager | null = null;
  private _connection: HubConnection | null = null;
  private _statusListeners: Set<(status: SignalRStatus, latencyMs: number | null) => void> = new Set();
  private _status: SignalRStatus = 'disconnected';
  private _latencyMs: number | null = null;
  private _latencyIntervalId: ReturnType<typeof setInterval> | null = null;
  private _reconnectAttempts = 0;

  private constructor() {}

  /** Singleton accessor */
  static getInstance(): SignalRConnectionManager {
    if (!SignalRConnectionManager._instance) {
      SignalRConnectionManager._instance = new SignalRConnectionManager();
    }
    return SignalRConnectionManager._instance;
  }

  /**
   * Returns the HubConnection for direct handler registration.
   * Call start() first.
   */
  get connection(): HubConnection | null {
    return this._connection;
  }

  get status(): SignalRStatus {
    return this._status;
  }

  get latencyMs(): number | null {
    return this._latencyMs;
  }

  get reconnectAttempts(): number {
    return this._reconnectAttempts;
  }

  // ── Lifecycle ──────────────────────────────────────────────────

  /**
   * Start (or resume) the SignalR connection. Idempotent.
   * Throws if connection fails.
   */
  async start(hubUrl: string = '/hubs/market-data', accessToken?: string): Promise<void> {
    if (this._connection?.state === HubConnectionState.Connected) {
      return; // Already connected
    }

    // Clean up any stale connection
    await this.stop();

    this._setStatus('connecting');

    const builder = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => accessToken ?? '',
        transport: 1, // WebSocket only (no SSE/long-polling)
      })
      .withAutomaticReconnect(new ReconnectPolicy())
      .withServerTimeoutInMilliseconds(SERVER_TIMEOUT_MS)
      .withKeepAliveIntervalInMilliseconds(KEEP_ALIVE_INTERVAL_MS)
      .configureLogging(LogLevel.Warning);

    this._connection = builder.build();

    // ── Register lifecycle handlers ──────────────────────────────
    this._connection.onreconnecting((error) => {
      console.warn('[SignalR] Reconnecting...', error?.message);
      this._reconnectAttempts++;
      this._setStatus('reconnecting');
    });

    this._connection.onreconnected(() => {
      console.info('[SignalR] Reconnected successfully.');
      this._reconnectAttempts = 0;
      this._setStatus('connected');
      this._startLatencyPing();
    });

    this._connection.onclose((error) => {
      console.warn('[SignalR] Connection closed.', error?.message);
      this._setStatus('disconnected');
      this._stopLatencyPing();
    });

    // ── Connect ─────────────────────────────────────────────────
    try {
      await this._connection.start();
      console.info('[SignalR] Connected to', hubUrl);

      this._reconnectAttempts = 0;
      this._setStatus('connected');
      this._startLatencyPing();
    } catch (err) {
      this._setStatus('error');
      console.error('[SignalR] Connection failed:', err);
      throw err;
    }
  }

  /**
   * Gracefully disconnect. Resets state.
   */
  async stop(): Promise<void> {
    this._stopLatencyPing();

    if (this._connection) {
      try {
        await this._connection.stop();
      } catch (err) {
        console.warn('[SignalR] Error during stop:', err);
      }
      this._connection = null;
    }

    this._setStatus('disconnected');
    this._reconnectAttempts = 0;
  }

  /**
   * Register a listener for connection status changes.
   * Returns an unsubscribe function.
   */
  onStatusChange(
    listener: (status: SignalRStatus, latencyMs: number | null) => void
  ): () => void {
    this._statusListeners.add(listener);
    // Immediately call with current state
    listener(this._status, this._latencyMs);
    return () => this._statusListeners.delete(listener);
  }

  // ── Private helpers ────────────────────────────────────────────

  private _setStatus(status: SignalRStatus): void {
    this._status = status;
    this._statusListeners.forEach((fn) => fn(status, this._latencyMs));
  }

  /** Pings the server every 10s to measure round-trip latency. */
  private _startLatencyPing(): void {
    this._stopLatencyPing();
    this._latencyIntervalId = setInterval(async () => {
      if (!this._connection || this._connection.state !== HubConnectionState.Connected) {
        return;
      }
      try {
        const start = performance.now();
        await this._connection.invoke('Ping');
        this._latencyMs = Math.round(performance.now() - start);
      } catch {
        this._latencyMs = null;
      }
    }, 10_000);

    // Also invoke immediately
    this._pingNow();
  }

  private async _pingNow(): Promise<void> {
    if (!this._connection || this._connection.state !== HubConnectionState.Connected) return;
    try {
      const start = performance.now();
      await this._connection.invoke('Ping');
      this._latencyMs = Math.round(performance.now() - start);
    } catch {
      // Ignore
    }
  }

  private _stopLatencyPing(): void {
    if (this._latencyIntervalId) {
      clearInterval(this._latencyIntervalId);
      this._latencyIntervalId = null;
    }
  }
}

// Export singleton
export const signalRManager = SignalRConnectionManager.getInstance();