// ═══════════════════════════════════════════════════════════════════
// tailwind.config.ts - Professional dark trading theme.
//
// Palette matches TradingView / Binance / MEXC dark mode:
//   #0b0e11    - Deepest background
//   #1e2329    - Panel / card backgrounds
//   #2b2f36    - Borders / dividers
//   #848e9c    - Secondary/muted text
//   #f0f4f8    - Primary text on dark
//   #0ecb81    - Grean (bid, buy, profit)
//   #f6465d    - Red (ask, sell, loss)
//   #f0b90b    - Yellow/gold (active, warning, highlight)
// ═══════════════════════════════════════════════════════════════════

import type { Config } from 'tailwindcss';

const config: Config = {
  content: [
    './src/app/**/*.{js,ts,jsx,tsx,mdx}',
    './src/components/**/*.{js,ts,jsx,tsx,mdx}',
    './src/stores/**/*.{ts,tsx}',
    './src/hooks/**/*.{ts,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        // Deep background
        background: {
          DEFAULT: '#0b0e11',
          panel: '#1e2329',
          hover: '#2b2f36',
        },
        // Text
        foreground: {
          DEFAULT: '#f0f4f8',
          muted: '#848e9c',
          dim: '#5e6673',
        },
        // Borders
        border: {
          DEFAULT: '#2b2f36',
        },
        // Trading-specific
        bid: {
          DEFAULT: '#0ecb81',
          bg: 'rgba(14, 203, 129, 0.12)',
        },
        ask: {
          DEFAULT: '#f6465d',
          bg: 'rgba(246, 70, 93, 0.12)',
        },
        accent: {
          DEFAULT: '#f0b90b',
          bg: 'rgba(240, 185, 11, 0.12)',
        },
        // Status
        success: '#0ecb81',
        danger: '#f6465d',
        warning: '#f0b90b',
        info: '#9155fd',
      },
      fontFamily: {
        sans: ['var(--font-sans)', 'Inter', 'system-ui', '-apple-system', 'sans-serif'],
        mono: ['var(--font-mono)', 'JetBrains Mono', 'IBM Plex Mono', 'Fira Code', 'monospace'],
      },
      fontSize: {
        '2xs': ['10px', '14px'],
      },
      spacing: {
        '4.5': '1.125rem',
        '18': '4.5rem',
      },
      animation: {
        'flash-green': 'flash-green 300ms ease-out',
        'flash-red': 'flash-red 300ms ease-out',
        'toast-in': 'toast-slide-in 200ms ease-out',
        'pulse-dot': 'pulse-dot 2s ease-in-out infinite',
      },
    },
  },
  plugins: [],
};

export default config;