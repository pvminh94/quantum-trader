# ═══════════════════════════════════════════════════════════════════
# CRYPTO/FINANCIAL TRADING DASHBOARD & AUTOMATED BOT PLATFORM
# ARCHITECTURE BLUEPRINT v1.0
# ═══════════════════════════════════════════════════════════════════

# ┌──────────────────────────────────────────────────────────────────┐
# │                   HIGH-LEVEL SYSTEM OVERVIEW                    │
# └──────────────────────────────────────────────────────────────────┘
#
#  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐
#  │  Client  │    │  CDN /   │    │  API     │    │ Exchange │
#  │ Browser  │───▶│  LB      │───▶│ Gateway  │───▶│ (Binance │
#  │(Next.js) │    │(CloudFlr)│    │(.NET 9)  │    │  /MEXC)  │
#  └──────────┘    └──────────┘    └──────────┘    └──────────┘
#       │                              │                 │
#       │  WebSocket (SignalR)         │                 │
#       ├───────────────────────────────┤                 │
#       │  REST (Orders/Config)        │                 │
#       │                              │                 │
#  ┌────▼──────────────────────────────▼─────────────────▼──────────┐
#  │                    REAL-TIME DATA PIPELINE                      │
#  │  ┌──────────────────────────────────────────────────────────┐   │
#  │  │  Exchange WebSocket Feed Handler                         │   │
#  │  │  ┌──────────┐  ┌──────────┐  ┌──────────────────────┐   │   │
#  │  │  │Orderbook │  │  Trade   │  │ Ticker / Kline       │   │   │
#  │  │  │Stream    │  │  Stream  │  │ Stream               │   │   │
#  │  │  └────┬─────┘  └────┬─────┘  └──────┬───────────────┘   │   │
#  │  └───────┼──────────────┼───────────────┼───────────────────┘   │
#  │          ▼              ▼               ▼                       │
#  │  ┌──────────────────────────────────────────────────────────┐   │
#  │  │              REDIS PUB/SUB LAYER                         │   │
#  │  │  (Ch: orderbook:{symbol}, trades:{symbol}, ticker:{sym} )│   │
#  │  └──────────────────────────────────────────────────────────┘   │
#  │          │              │               │                       │
#  │          ▼              ▼               ▼                       │
#  │  ┌──────────────────────────────────────────────────────────┐   │
#  │  │  SignalR Hub (Strongly-Typed)                            │   │
#  │  │  Groups: user_{id}, symbol_{symbol}, admin               │   │
#  │  └──────────────────────────────────────────────────────────┘   │
#  └──────────────────────────────────────────────────────────────────┘

# ┌──────────────────────────────────────────────────────────────────┐
# │                   CLIENT-SIDE DATA FLOW                          │
# └──────────────────────────────────────────────────────────────────┘
#
#  SignalR Connection
#       │
#       ▼
#  ┌─────────────────────────────────┐
#  │  useSignalR (Custom Hook)       │
#  │  ─ Manages connection lifecycle  │
#  │  ─ Automatic reconnection w/     │
#  │    exponential backoff           │
#  │  ─ Deserializes & validates      │
#  └──────────┬──────────────────────┘
#             │
#             ▼
#  ┌─────────────────────────────────┐
#  │  Zustand Stores                 │
#  │  ─ orderbookStore (partial upd) │
#  │  ─ tradeStore (circular buffer) │
#  │  ─ chartStore (price history)   │
#  │  ─ orderStore (user orders)     │
#  │  ─ botStore (bot telemetry)     │
#  │  ─ uiStore (layout, prefs)      │
#  └──────────┬──────────────────────┘
#             │ (selector-based subscribe
#             │  to prevent re-renders)
#             ▼
#  ┌─────────────────────────────────┐
#  │  React Components               │
#  │  (skeleton: only re-render when │
#  │   selected slice changes)       │
#  └─────────────────────────────────┘

# ┌──────────────────────────────────────────────────────────────────┐
# │                  BOT EXECUTION LOOP                              │
# └──────────────────────────────────────────────────────────────────┘
#
#  Main Loop (each managed bot instance):
#  ┌──────────────────────────────────────────────┐
#  │ while (running && !circuitBreaker.tripped)   │
#  │  1. FETCH fresh market data from local cache │
#  │     (Redis - sub-millisecond)                │
#  │  2. RUN strategy indicators                  │
#  │     (TA-Lib / custom indicators)             │
#  │  3. EVALUATE entry/exit conditions           │
#  │  4. CALCULATE position size                  │
#  │     (Kelly Criterion / % risk)               │
#  │  5. VALIDATE risk constraints                │
#  │     (max drawdown, daily loss, exposure)     │
#  │  6. SUBMIT order with idempotency key        │
#  │  7. WAIT for acknowledgment / next heartbeat │
#  │  8. UPDATE state machine                     │
#  └──────────────────────────────────────────────┘

# ┌──────────────────────────────────────────────────────────────────┐
# │                  STATE MACHINE: ORDER LIFECYCLE                  │
# └──────────────────────────────────────────────────────────────────┘
#
#                    Idempotency Key Generated
#                            │
#                            ▼
#                     ┌─────────────┐
#                     │   PENDING   │◀──── (re-submission guard)
#                     └──────┬──────┘
#                            │
#                   Exchange ACK received
#                            │
#                            ▼
#                     ┌─────────────┐
#              ┌─────▶│    OPEN     │◀────┐
#              │      └──────┬──────┘     │
#              │             │            │
#         Partial Fill   Fully Filled   Cancel
#              │             │            │
#              ▼             ▼            ▼
#        ┌──────────┐  ┌──────────┐  ┌──────────┐
#        │ PARTIAL  │  │  FILLED  │  │ CANCELED │
#        │   FILL   │  └──────────┘  └──────────┘
#        └────┬─────┘
#             │ (loops back to OPEN
#             │  until filled)
#
#  FAILED state entered on: network error, insufficient funds,
#  exchange rejection, rate-limit exceeded (after 3 retries)

# ┌──────────────────────────────────────────────────────────────────┐
# │                  CIRCUIT BREAKER / KILL SWITCH                   │
# └──────────────────────────────────────────────────────────────────┘
#
#  ┌──────────────────────────────┐
#  │  Global Breaker              │
#  │  - Max Daily Drawdown (e.g.  │
#  │    10% of portfolio)         │
#  │  - Manual kill endpoint      │
#  │  - All bots: STOP            │
#  └──────────┬───────────────────┘
#             │
#  ┌──────────▼───────────────────┐
#  │  Per-Bot Breaker             │
#  │  - Max consecutive losses    │
#  │  - Max daily loss (config %) │
#  │  - Exposure limit exceeded   │
#  │  - Drawdown > threshold      │
#  └──────────┬───────────────────┘
#             │
#  ┌──────────▼───────────────────┐
#  │  Order-Level Risk Check      │
#  │  - Slippage tolerance        │
#  │  - Min/max position size     │
#  │  - Price validation (±5%)    │
#  └──────────────────────────────┘