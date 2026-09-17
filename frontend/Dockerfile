# ═══════════════════════════════════════════════════════════════════
# Dockerfile - Frontend Next.js 15 Trading Dashboard
#
# Multi-stage build:
#   1. deps     - install dependencies (cached layer)
#   2. build    - compile TypeScript + optimize assets
#   3. runner   - standalone Next.js server (minimal image)
# ═══════════════════════════════════════════════════════════════════

# ── Stage 1: Dependencies ──────────────────────────────────────────
FROM node:22-alpine AS deps
WORKDIR /app

# Copy package files
COPY package.json package-lock.json* ./

# Install dependencies (cached unless package.json changes)
RUN npm ci --only=production && npm cache clean --force

# ── Stage 2: Build ─────────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app

# Copy node_modules from deps stage
COPY --from=deps /app/node_modules ./node_modules
COPY . .

# Build Next.js standalone output
ENV NEXT_TELEMETRY_DISABLED=1
ENV NODE_ENV=production
RUN npm run build

# ── Stage 3: Runner ────────────────────────────────────────────────
FROM node:22-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV NEXT_PRIVATE_STANDALONE=true

# Create non-root user
RUN addgroup --system --gid 1001 nodejs && \
    adduser --system --uid 1001 nextjs && \
    chown -R nextjs:nodejs /app

# Copy standalone build (contains only what's needed)
COPY --from=build /app/public ./public
COPY --from=build --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /app/.next/static ./.next/static

# Copy env file for runtime
COPY --from=build /app/.env.production .env.production

USER nextjs

# Expose Next.js default port
EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:3000 || exit 1

CMD ["node", "server.js"]