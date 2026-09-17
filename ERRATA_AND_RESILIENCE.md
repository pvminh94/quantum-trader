// ═══════════════════════════════════════════════════════════════════
// ERRATA_HANDLING_AND_RESILIENCE.md - Error handling & resilience strategy.
// ═══════════════════════════════════════════════════════════════════

# CRYPTO TRADING PLATFORM — ERROR HANDLING & RESILIENCE STRATEGY

## 1. WEBSOCKET RECONNECTION (SIGNALR + EXCHANGE FEED)

### SignalR Client (Browser → Server)
```
Layer          Strategy                          Parameters
────────────── ───────────────────────────────── ─────────────────────
Transport      WebSockets only (no SSE/fallback)  
Reconnect      Custom IRetryPolicy with           0s, 1s, 2s, 5s, 10s, 30s
               exponential backoff               Max: 30s intervals
Timeouts       ServerTimeout: 30s                KeepAlive: 15s
               
State Mgmt     All subscriptions are re-sent      Hub groups re-joined
               on reconnected event               on reconnect
               
Idempotency    "Ping" invoked immediately         Latency measured on
               after reconnect                    every 10s interval
```

### Exchange WebSocket Feed (Server → Exchange)
```
Layer          Strategy                          Parameters
────────────── ───────────────────────────────── ─────────────────────
Reconnection   Exponential backoff               1s → 2s → 5s → 10s → 30s
               Infinite retries (until service    30s max delay
               cancellation)
               
Stream Resume  Re-subscribe to ALL streams on    Full subscribe message
               reconnection                      sent every reconnect
               
Heartbeat      Exchange sends ping every 3min    Server responds with pong
               (Binance default)                 
               
Data Gap       100ms depth streams → gap is      Full depth snapshot
Recovery       <200ms; acceptable for trading    request on reconnect
```

## 2. DATABASE RESILIENCE (PostgreSQL)

### Connection Pooling
```
Setting           Value            Rationale
────────────────  ──────────────── ─────────────────────────────
MaxPoolSize       100              Supports 20 concurrent bots + API
Keepalive         60s              Detect dead TCP connections
CommandTimeout    30s              Prevent hung queries from blocking
                                    the pool
```

### Deadlock Prevention
```
Strategy                          Implementation
───────────────────────────────── ─────────────────────────────────
Lock Ordering                     All order operations follow:
                                  1. User row (SELECT...FOR UPDATE)
                                  2. Account row
                                  3. Order row
                                  4. Trade row
                                  
Timeout                           All transactions use:
                                  SET lock_timeout = '5s';
                                  SET statement_timeout = '10s';
                                  
Retry Logic                       On serialization_failure (40001)
                                  or deadlock_detected (40P01):
                                  Retry up to 3 times with 100ms delay
                                  
Optimistic Locking                Use row version (xmin) for
                                  account balance updates
```

### Connection Health Check
```csharp
// Every 15 seconds, verify database is reachable
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Name == "postgresql",
    FailureStatus = HealthStatus.Unhealthy
});
```

## 3. ORDER IDEMPOTENCY (PREVENT DUPLICATE FILLS)

### Idempotency Key Flow
```
Client generates UUID v4
       │
       ▼
Server checks Redis: GET idempotency:{key}
       │
       ├── EXISTS → Return existing order (no exchange call)
       │
       └── NOT EXISTS → SET idempotency:{key} = orderId (NX, TTL: 60s)
                        Call exchange API
                        On failure: DELETE idempotency:{key}
                        (allows retry with same key)
```

### Order State Machine (Prevents Invalid Transitions)
```
Valid transitions (enforced by Guard class):
  Pending        → Open | Filled | Failed
  Open           → PartiallyFilled | Filled | Canceled | Failed
  PartiallyFilled → Filled | Canceled | Failed
  Filled/Canceled/Failed → (terminal - no transitions)
```

## 4. RATE LIMITING

### Three-Layer Approach
```
Layer 1: Nginx/CDN      → IP-based, 100 req/s static assets
Layer 2: API Middleware  → Sliding window per-IP: 1200 req/min
Layer 3: Exchange Gateway→ Token bucket per API key: 20 req/s
```

### Rate Limit Response
```json
{
  "error": "rate_limit_exceeded",
  "message": "Too many requests. Retry after 45 seconds.",
  "retry_after_seconds": 45
}
HTTP 429 Too Many Requests
Retry-After: 45
```

## 5. CIRCUIT BREAKER

### Hierarchy of Breakers
```
Global Breaker (portfolio level)
   │
   ├── Max daily drawdown exceeded (configurable %)
   ├── Emergency kill (manual API call)
   └── Admin kill (dashboard button)
   
Per-Bot Breaker (individual bot)
   │
   ├── Max consecutive losses (default: 5)
   ├── Max daily loss (config: $500)
   ├── Max daily trades (config: 50)
   └── Exposure limit exceeded (config: $5k)
   
Order-Level Risk Check
   │
   ├── Price deviation >5% from market
   ├── Invalid quantity (negative or dust)
   └── Exchange rate limit exceeded (3 retries → fail)
```

## 6. CATASTROPHIC FAILURE SCENARIOS

### Scenario 1: Exchange API Down
```
Detection:    3 consecutive HTTP 502/503 from exchange
Action:       All bots pause (stop placing new orders)
Alert:        Push notification + dashboard red banner
Recovery:     Every 30s attempt health-check ping
              Auto-resume when 3 consecutive pings succeed
```

### Scenario 2: Redis Cluster Failure
```
Detection:    Connection failed / timeout
Fallback:     Switch to in-memory ConcurrentDictionary caches
              (degraded mode - data not shared between instances)
Warning:      Log critical alert; dashboard shows "Degraded Mode"
Recovery:     Auto-reconnect with exponential backoff
```

### Scenario 3: Database Connection Loss
```
Detection:    OpenConnectionAsync throws
Action:       All write operations queue to in-memory buffer
              (max 1000 entries, oldest dropped first)
Impact:       Trade history not persisted until DB recovers
Recovery:     Flush buffer on reconnection (oldest first)
```

### Scenario 4: Power / Network Outage (Server)
```
SignalR:      All clients disconnect
              On reconnect, full state sync:
              - Re-subscribe to symbols
              - Receive fresh orderbook snapshot
              - Bot states reloaded from PostgreSQL
              
Orders:       Exchange retains open orders (GTC)
              On restart, reconcile: cancel all open orders
              that were placed by this instance (by instance ID)
```

## 7. MONITORING & ALERTING

### Key Metrics (Prometheus endpoints)
```
Metric                          Type        Description
─────────────────────────────── ─────────── ──────────────────────────
trading_ws_connections           Gauge       Active WebSocket connections
trading_ws_reconnects_total      Counter     WebSocket reconnection count
trading_order_failures_total     Counter     Order placement failures
trading_order_latency_ms         Histogram   Order placement latency
trading_bot_active_count         Gauge       Currently running bots
trading_circuit_breaker_tripped  Gauge       1 = tripped, 0 = normal
trading_db_connection_pool_size  Gauge       PostgreSQL pool size
trading_redis_cache_hit_ratio    Gauge       Redis cache hit/miss ratio
trading_signalr_message_rate     Gauge       Messages per second
```

### Log Levels
```
FATAL:    Catastrophic failure requiring manual intervention
          (DB cluster down, exchange API unreachable >5min)
          
ERROR:    Operation failed, needs investigation
          (order rejected, rate limit exceeded, WS reconnect loop)
          
WARNING:  Degraded mode, non-critical failure
          (Redis cache miss, high latency >100ms, partial fill)
          
INFO:     Normal operations
          (order placed, bot started, user connected)
          
DEBUG:    Detailed diagnostic information
          (full message payloads, state transitions)
```