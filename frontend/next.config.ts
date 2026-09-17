// ═══════════════════════════════════════════════════════════════════
// next.config.ts - Next.js 15 configuration.
// ═══════════════════════════════════════════════════════════════════

import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  reactStrictMode: true,

  // ── Output configuration ─────────────────────────────────────
  output: 'standalone',

  // ── Image optimization ───────────────────────────────────────
  images: {
    formats: ['image/avif', 'image/webp'],
    minimumCacheTTL: 60 * 60 * 24 * 30, // 30 days
  },

  // ── Compress responses ───────────────────────────────────────
  compress: true,

  // ── HTTP headers (security) ──────────────────────────────────
  async headers() {
    return [
      {
        source: '/(.*)',
        headers: [
          {
            key: 'X-Content-Type-Options',
            value: 'nosniff',
          },
          {
            key: 'X-Frame-Options',
            value: 'DENY',
          },
          {
            key: 'X-XSS-Protection',
            value: '1; mode=block',
          },
          {
            key: 'Referrer-Policy',
            value: 'strict-origin-when-cross-origin',
          },
        ],
      },
      {
        source: '/hubs/(.*)',
        headers: [
          {
            key: 'Access-Control-Allow-Origin',
            value: process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000',
          },
        ],
      },
    ];
  },

  // ── Webpack configuration for SignalR ────────────────────────
  webpack: (config) => {
    // SignalR uses some Node.js modules; polyfill is not needed
    // with modern SignalR client library
    return config;
  },
};

export default nextConfig;