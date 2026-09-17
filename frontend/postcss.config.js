// ═══════════════════════════════════════════════════════════════════
// postcss.config.js - PostCSS configuration with Tailwind + autoprefixer.
// ═══════════════════════════════════════════════════════════════════

/** @type {import('postcss-load-config').Config} */
const config = {
  plugins: {
    tailwindcss: {},
    autoprefixer: {},
  },
};

export default config;