// ═══════════════════════════════════════════════════════════════════
// Layout.tsx - Next.js 15 root layout.
//
// Professional trader dashboard layout with:
// - Fixed top navbar
// - Main grid area (chart | orderbook | bottom panels)
// - Global SignalR provider
// - Zustand hydration
// ═══════════════════════════════════════════════════════════════════

import type React from 'react';
import type { Metadata } from 'next';
import { Inter, JetBrains_Mono } from 'next/font/google';
import './styles/globals.css';
import './styles/chart-theme.css';
import { SignalRProvider } from '@/components/shared/SignalRProvider';

// ── Typography ──────────────────────────────────────────────────

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-sans',
  display: 'swap',
});

const jetbrainsMono = JetBrains_Mono({
  subsets: ['latin'],
  variable: '--font-mono',
  display: 'swap',
});

// ── Metadata ────────────────────────────────────────────────────

export const metadata: Metadata = {
  title: 'QuantumTrader - Professional Crypto Trading Dashboard',
  description:
    'Ultra-low-latency crypto trading dashboard with automated bot engine. ' +
    'Real-time orderbook, advanced charting, and algorithmic trading.',
  icons: {
    icon: '/favicon.ico',
  },
  // Prevent iOS auto-detection of phone numbers
  other: {
    'format-detection': 'telephone=no',
  },
};

// ═══════════════════════════════════════════════════════════════════
// ROOT LAYOUT
// ═══════════════════════════════════════════════════════════════════

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html
      lang="en"
      className={`${inter.variable} ${jetbrainsMono.variable}`}
      suppressHydrationWarning
    >
      <head>
        {/* Prevent flash of white background on load */}
        <script
          dangerouslySetInnerHTML={{
            __html: `
              document.documentElement.style.backgroundColor = '#0b0e11';
              document.documentElement.style.colorScheme = 'dark';
            `,
          }}
        />
      </head>
      <body className="bg-[#0b0e11] text-[#f0f4f8] antialiased overflow-hidden">
        <SignalRProvider>
          <div className="h-screen w-screen flex flex-col">
            {children}
          </div>
        </SignalRProvider>
      </body>
    </html>
  );
}