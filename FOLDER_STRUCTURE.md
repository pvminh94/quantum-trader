# ═══════════════════════════════════════════════════════════════════
# COMPLETE FOLDER STRUCTURE
# ═══════════════════════════════════════════════════════════════════

# ┌──────────────────────────────────────────────────────────────────┐
# │                   BACKEND (.NET 9 / C#)                          │
# └──────────────────────────────────────────────────────────────────┘
#
# backend/
# ├── TradingEngine.sln
# ├── src/
# │   ├── TradingEngine.Api/                    # WebAPI + SignalR Host
# │   │   ├── Program.cs
# │   │   ├── appsettings.json
# │   │   ├── appsettings.Development.json
# │   │   ├── Properties/
# │   │   │   └── launchSettings.json
# │   │   ├── Controllers/
# │   │   │   ├── AccountController.cs
# │   │   │   ├── OrderController.cs
# │   │   │   ├── BotController.cs
# │   │   │   └── AdminController.cs
# │   │   ├── Hubs/
# │   │   │   ├── MarketDataHub.cs              # Strongly-typed SignalR Hub
# │   │   │   └── BotTelemetryHub.cs
# │   │   ├── Middleware/
# │   │   │   ├── ExceptionHandlingMiddleware.cs
# │   │   │   ├── RequestRateLimitingMiddleware.cs
# │   │   │   └── RequestValidationMiddleware.cs
# │   │   └── Extensions/
# │   │       ├── ServiceCollectionExtensions.cs
# │   │       └── ApplicationBuilderExtensions.cs
# │   │
# │   ├── TradingEngine.Domain/                 # Domain models (pure POCOs)
# │   │   ├── Models/
# │   │   │   ├── Order.cs
# │   │   │   ├── OrderBook.cs
# │   │   │   ├── Trade.cs
# │   │   │   ├── Ticker.cs
# │   │   │   ├── Kline.cs
# │   │   │   ├── BotConfig.cs
# │   │   │   ├── BotState.cs
# │   │   │   ├── Account.cs
# │   │   │   ├── Position.cs
# │   │   │   └── ExchangeApiCredentials.cs
# │   │   ├── Enums/
# │   │   │   ├── OrderSide.cs
# │   │   │   ├── OrderType.cs
# │   │   │   ├── OrderStatus.cs
# │   │   │   ├── TimeInForce.cs
# │   │   │   └── BotStatus.cs
# │   │   └── ValueObjects/
# │   │       ├── Price.cs
# │   │       ├── Quantity.cs
# │   │       └── Money.cs
# │   │
# │   ├── TradingEngine.Application/            # Business logic layer
# │   │   ├── Services/
# │   │   │   ├── OrderService.cs
# │   │   │   ├── AccountService.cs
# │   │   │   ├── BotOrchestratorService.cs
# │   │   │   ├── RiskManagementService.cs
# │   │   │   ├── PositionSizeCalculator.cs
# │   │   │   └── CircuitBreakerService.cs
# │   │   ├── Interfaces/
# │   │   │   ├── IOrderService.cs
# │   │   │   ├── IAccountService.cs
# │   │   │   ├── IExchangeGateway.cs
# │   │   │   ├── IMarketDataService.cs
# │   │   │   └── IRiskManagementService.cs
# │   │   └── BackgroundServices/
# │   │       ├── MarketDataIngestionService.cs  # Main WebSocket feed
# │   │       ├── BotExecutionEngineService.cs   # Bot loop runner
# │   │       ├── RiskMonitorService.cs          # Circuit breaker monitor
# │   │       └── HealthCheckService.cs
# │   │
# │   ├── TradingEngine.Infrastructure/          # Data access + external
# │   │   ├── Persistence/
# │   │   │   ├── AppDbContext.cs
# │   │   │   ├── Repositories/
# │   │   │   │   ├── OrderRepository.cs
# │   │   │   │   ├── AccountRepository.cs
# │   │   │   │   ├── BotConfigRepository.cs
# │   │   │   │   └── TradeHistoryRepository.cs
# │   │   │   └── Migrations/
# │   │   ├── Caching/
# │   │   │   ├── RedisCacheProvider.cs
# │   │   │   ├── OrderBookCache.cs
# │   │   │   └── TickerCache.cs
# │   │   ├── Exchange/
# │   │   │   ├── BinanceGateway.cs
# │   │   │   ├── MexcGateway.cs
# │   │   │   └── WebSocketClientManager.cs
# │   │   └── Security/
# │   │       ├── AesEncryptionProvider.cs
# │   │       ├── JwtTokenService.cs
# │   │       └── ApiKeyManager.cs
# │   │
# │   └── TradingEngine.Shared/                 # Cross-cutting
# │       ├── Constants.cs
# │       ├── Guard.cs
# │       ├── Result.cs
# │       ├── Error.cs
# │       └── Extensions/
# │           ├── StringExtensions.cs
# │           └── DateTimeExtensions.cs
# │
# ├── tests/
# │   ├── TradingEngine.UnitTests/
# │   │   ├── Services/
# │   │   └── Domain/
# │   └── TradingEngine.IntegrationTests/
# │       ├── Exchange/
# │       └── Persistence/
# │
# └── docker-compose.yml
#     (PostgreSQL + Redis + Seq logging)


# ┌──────────────────────────────────────────────────────────────────┐
# │                   FRONTEND (Next.js 15 / React 19)               │
# └──────────────────────────────────────────────────────────────────┘
#
# frontend/
# ├── package.json
# ├── tsconfig.json
# ├── next.config.ts
# ├── tailwind.config.ts
# ├── postcss.config.js
# ├── components.json                         # Shadcn config
# ├── .env.local
# ├── .env.production
# │
# ├── public/
# │   └── icons/
# │       └── exchange-logos/
# │
# ├── src/
# │   ├── app/                                # Next.js 15 App Router
# │   │   ├── layout.tsx                      # Root shell (Navbar + Grid)
# │   │   ├── page.tsx                        # Main dashboard page
# │   │   ├── trading/
# │   │   │   ├── [symbol]/
# │   │   │   │   └── page.tsx                # Dynamic symbol route
# │   │   ├── bots/
# │   │   │   ├── page.tsx
# │   │   │   └── [botId]/
# │   │   │       └── page.tsx
# │   │   ├── settings/
# │   │   │   └── page.tsx
# │   │   ├── login/
# │   │   │   └── page.tsx
# │   │   └── api/                            # Next.js API routes (thin BFF)
# │   │       ├── auth/
# │   │       │   └── [...nextauth]/
# │   │       └── proxy/
# │   │           └── [...path]/
# │   │
# │   ├── components/                         # React components
# │   │   ├── ui/                             # Shadcn primitives
# │   │   │   ├── button.tsx
# │   │   │   ├── input.tsx
# │   │   │   ├── select.tsx
# │   │   │   ├── tabs.tsx
# │   │   │   ├── toast.tsx
# │   │   │   ├── tooltip.tsx
# │   │   │   ├── dialog.tsx
# │   │   │   ├── dropdown-menu.tsx
# │   │   │   ├── badge.tsx
# │   │   │   ├── card.tsx
# │   │   │   └── scroll-area.tsx
# │   │   │
# │   │   ├── layout/                         # Layout components
# │   │   │   ├── Navbar.tsx
# │   │   │   ├── Sidebar.tsx
# │   │   │   ├── ResizableGrid.tsx
# │   │   │   └── TabPanel.tsx
# │   │   │
# │   │   ├── chart/                          # Trading Chart components
# │   │   │   ├── LiveChartWrapper.tsx         # Lightweight Charts integration
# │   │   │   ├── ChartToolbar.tsx
# │   │   │   ├── IndicatorsOverlay.tsx
# │   │   │   └── TimeframeSelector.tsx
# │   │   │
# │   │   ├── orderbook/                      # OrderBook components
# │   │   │   ├── OrderBookPanel.tsx           # Main container
# │   │   │   ├── OrderBookRow.tsx             # Highly optimized row
# │   │   │   ├── SpreadIndicator.tsx
# │   │   │   └── DepthVisualizer.tsx
# │   │   │
# │   │   ├── trades/
# │   │   │   └── RecentTrades.tsx
# │   │   │
# │   │   ├── orders/
# │   │   │   ├── OrderEntry.tsx               # Buy/Sell form
# │   │   │   ├── OpenOrdersTable.tsx
# │   │   │   ├── OrderHistoryTable.tsx
# │   │   │   └── PositionRow.tsx
# │   │   │
# │   │   ├── bots/
# │   │   │   ├── BotCard.tsx
# │   │   │   ├── BotConfigForm.tsx
# │   │   │   ├── BotTelemetryPanel.tsx
# │   │   │   └── BotLogViewer.tsx             # Virtualized log viewer
# │   │   │
# │   │   ├── account/
# │   │   │   ├── BalanceSummary.tsx
# │   │   │   ├── AssetBalancesTable.tsx
# │   │   │   └── ApiKeyForm.tsx
# │   │   │
# │   │   └── shared/
# │   │       ├── VirtualizedTable.tsx          # Reusable virtualized grid
# │   │       ├── StatusIndicator.tsx
# │   │       ├── LatencyBadge.tsx
# │   │       ├── ToastNotification.tsx
# │   │       └── SkeletonLoader.tsx
# │   │
# │   ├── hooks/                              # Custom React hooks
# │   │   ├── useSignalR.ts                    # SignalR connection lifecycle
# │   │   ├── useOrderBook.ts                  # Optimistic orderbook subscription
# │   │   ├── useTrades.ts
# │   │   ├── useChartData.ts
# │   │   ├── useOrders.ts
# │   │   ├── useBots.ts
# │   │   ├── useAccount.ts
# │   │   ├── useDebounce.ts
# │   │   └── useVirtualizer.ts
# │   │
# │   ├── stores/                             # Zustand stores
# │   │   ├── orderbookStore.ts
# │   │   ├── tradeStore.ts
# │   │   ├── chartStore.ts
# │   │   ├── orderStore.ts
# │   │   ├── botStore.ts
# │   │   ├── accountStore.ts
# │   │   ├── uiStore.ts
# │   │   └── signalrStore.ts
# │   │
# │   ├── lib/                                # Utilities
# │   │   ├── signalr-connection.ts            # SignalR client factory
# │   │   ├── price-format.ts
# │   │   ├── date-format.ts
# │   │   ├── cn.ts                            # class-variance-authority helper
# │   │   ├── constants.ts
# │   │   └── validation.ts
# │   │
# │   ├── types/                              # TypeScript interfaces
# │   │   ├── market.ts
# │   │   ├── order.ts
# │   │   ├── bot.ts
# │   │   ├── account.ts
# │   │   └── signalr.ts
# │   │
# │   └── styles/
# │       ├── globals.css                     # Tailwind base + component layer
# │       └── chart-theme.css                 # TradingView chart palette
# │
# └── __tests__/
#     ├── components/
#     └── hooks/