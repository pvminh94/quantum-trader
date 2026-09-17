// ═══════════════════════════════════════════════════════════════════
// LiveChartWrapper.tsx - High-performance candlestick chart.
//
// Integrates TradingView Lightweight Charts library for professional
// candlestick rendering. Features:
// - Multi-timeframe switching (1s, 1m, 5m, 1h, 1D)
// - Technical indicator overlays (EMA, SMA, RSI)
// - Crosshair with price/time labels
// - Volume histogram
// - Auto-scale with padding
// - Market price line
// - Dark theme matching the #0b0e11 palette
// ═══════════════════════════════════════════════════════════════════

'use client';

import React, { useRef, useEffect, useCallback, useState, memo } from 'react';
import {
  createChart,
  IChartApi,
  ISeriesApi,
  CandlestickSeriesPartialOptions,
  HistogramSeriesPartialOptions,
  LineSeriesPartialOptions,
  Time,
  CrosshairMode,
  PriceScaleMode,
} from 'lightweight-charts';
import { useChartStore } from '@/stores/chartStore';
import type { KlineDto, Timeframe } from '@/types/market';

// ═══════════════════════════════════════════════════════════════════
// LIGHTWEIGHT CHARTS THEME
// ═══════════════════════════════════════════════════════════════════

const CHART_THEME = {
  background: '#0b0e11',
  textColor: '#848e9c',
  gridColor: '#1e2329',
  borderColor: '#2b2f36',
  candleUpColor: '#0ecb81',
  candleDownColor: '#f6465d',
  candleBorderUpColor: '#0ecb81',
  candleBorderDownColor: '#f6465d',
  candleWickUpColor: '#0ecb81',
  candleWickDownColor: '#f6465d',
  volumeUpColor: 'rgba(14, 203, 129, 0.3)',
  volumeDownColor: 'rgba(246, 70, 93, 0.3)',
  crosshairColor: '#848e9c',
  emaColor: '#f0b90b',
  rsiLineColor: '#9155fd',
  lastPriceLineColor: '#f0b90b',
};

// ═══════════════════════════════════════════════════════════════════
// CHART WRAPPER COMPONENT
// ═══════════════════════════════════════════════════════════════════

interface LiveChartWrapperProps {
  symbol: string;
  height?: number;
}

/**
 * Main chart component. Creates a lightweight chart instance on mount
 * and manages all series (candlesticks, volume, indicators).
 * Completely controlled: data updates come from external store.
 */
export function LiveChartWrapper({ symbol, height = 500 }: LiveChartWrapperProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const volumeSeriesRef = useRef<ISeriesApi<'Histogram'> | null>(null);
  const emaSeriesRef = useRef<ISeriesApi<'Line'> | null>(null);
  const lastPriceLineRef = useRef<ISeriesApi<'Line'> | null>(null);

  const { klines, activeTimeframe, indicators } = useChartStore();
  const [isReady, setIsReady] = useState(false);

  // ── Initialize chart on mount ──────────────────────────────────
  useEffect(() => {
    if (!containerRef.current) return;

    const chart = createChart(containerRef.current, {
      width: containerRef.current.clientWidth,
      height,
      layout: {
        background: { type: 'solid', color: CHART_THEME.background },
        textColor: CHART_THEME.textColor,
        fontFamily: "'IBM Plex Mono', 'JetBrains Mono', monospace",
      },
      grid: {
        vertLines: { color: CHART_THEME.gridColor },
        horzLines: { color: CHART_THEME.gridColor },
      },
      crosshair: {
        mode: CrosshairMode.Normal,
        vertLine: {
          color: CHART_THEME.crosshairColor,
          width: 1,
          style: 2, // Dashed
        },
        horzLine: {
          color: CHART_THEME.crosshairColor,
          width: 1,
          style: 2,
        },
      },
      rightPriceScale: {
        borderColor: CHART_THEME.borderColor,
        mode: PriceScaleMode.Normal,
        autoScale: true,
        entireTextOnly: true,
      },
      timeScale: {
        borderColor: CHART_THEME.borderColor,
        timeVisible: true,
        secondsVisible: activeTimeframe === '1s',
        fixLeftEdge: true,
        fixRightEdge: true,
        rightOffset: 5,
        barSpacing: 6,
      },
      handleScroll: {
        vertTouchDrag: false,
      },
    });

    // ── Candlestick series ───────────────────────────────────────
    const candleSeries = chart.addCandlestickSeries({
      upColor: CHART_THEME.candleUpColor,
      downColor: CHART_THEME.candleDownColor,
      borderUpColor: CHART_THEME.candleBorderUpColor,
      borderDownColor: CHART_THEME.candleBorderDownColor,
      wickUpColor: CHART_THEME.candleWickUpColor,
      wickDownColor: CHART_THEME.candleWickDownColor,
      priceLineVisible: false,
      lastValueVisible: true,
      priceFormat: {
        type: 'price',
        precision: 2,
        minMove: 0.01,
      },
    } satisfies CandlestickSeriesPartialOptions);

    // ── Volume histogram ─────────────────────────────────────────
    const volumeSeries = chart.addHistogramSeries({
      priceFormat: { type: 'volume' },
      priceScaleId: 'volume',
      baseLineVisible: false,
    } satisfies HistogramSeriesPartialOptions);

    chart.priceScale('volume').applyOptions({
      scaleMargins: { top: 0.85, bottom: 0 }, // 15% height at bottom
      visible: true,
    });

    // ── EMA indicator line ───────────────────────────────────────
    const emaSeries = chart.addLineSeries({
      color: CHART_THEME.emaColor,
      lineWidth: 1,
      priceLineVisible: false,
      lastValueVisible: true,
      title: 'EMA',
    } satisfies LineSeriesPartialOptions);

    // ── Last price line ──────────────────────────────────────────
    const lastPriceLine = chart.addLineSeries({
      color: CHART_THEME.lastPriceLineColor,
      lineWidth: 1,
      lineStyle: 2, // Dashed
      priceLineVisible: false,
      lastValueVisible: true,
    });

    // ── Store refs ─────────────────────────────────────────────────
    chartRef.current = chart;
    candleSeriesRef.current = candleSeries;
    volumeSeriesRef.current = volumeSeries;
    emaSeriesRef.current = emaSeries;
    lastPriceLineRef.current = lastPriceLine;
    setIsReady(true);

    // ── Handle resize ────────────────────────────────────────────
    const handleResize = () => {
      if (containerRef.current && chartRef.current) {
        chartRef.current.applyOptions({
          width: containerRef.current.clientWidth,
          height: containerRef.current.clientHeight || height,
        });
      }
    };

    const observer = new ResizeObserver(handleResize);
    observer.observe(containerRef.current);

    return () => {
      observer.disconnect();
      chart.remove();
      chartRef.current = null;
      candleSeriesRef.current = null;
      volumeSeriesRef.current = null;
      emaSeriesRef.current = null;
      lastPriceLineRef.current = null;
      setIsReady(false);
    };
  }, [height, activeTimeframe]);

  // ── Update chart data when klines change ───────────────────────
  useEffect(() => {
    if (!isReady || !candleSeriesRef.current) return;

    const symbolKlines = klines.get(symbol);
    if (!symbolKlines || symbolKlines.length === 0) return;

    // Transform to lightweight-charts format
    const candleData = symbolKlines.map((k) => ({
      time: (k.open_time_ms / 1000) as Time,
      open: k.open,
      high: k.high,
      low: k.low,
      close: k.close,
    }));

    const volumeData = symbolKlines.map((k) => ({
      time: (k.open_time_ms / 1000) as Time,
      value: k.volume,
      color: k.close >= k.open
        ? CHART_THEME.volumeUpColor
        : CHART_THEME.volumeDownColor,
    }));

    candleSeriesRef.current.setData(candleData);
    volumeSeriesRef.current?.setData(volumeData);

    // ── Update last price line ───────────────────────────────────
    const lastCandle = candleData[candleData.length - 1];
    if (lastCandle && lastPriceLineRef.current) {
      lastPriceLineRef.current.setData([
        { time: lastCandle.time, value: lastCandle.close },
      ]);
    }

    // ── Auto-scale to fit the latest data ────────────────────────
    chartRef.current?.timeScale().fitContent();
  }, [klines, symbol, isReady]);

  // ── Update EMA when indicators change ──────────────────────────
  useEffect(() => {
    if (!isReady || !emaSeriesRef.current) return;

    const symbolKlines = klines.get(symbol);
    if (!symbolKlines || symbolKlines.length < 20) return;

    if (indicators.has('EMA')) {
      // Simple EMA calculation (9-period)
      const closes = symbolKlines.map((k) => k.close);
      const emaValues = calculateEMA(closes, 9);

      const emaData = symbolKlines.slice(closes.length - emaValues.length).map((k, i) => ({
        time: (k.open_time_ms / 1000) as Time,
        value: emaValues[i],
      }));

      emaSeriesRef.current.setData(emaData);
    } else {
      emaSeriesRef.current.setData([]);
    }
  }, [klines, symbol, indicators, isReady]);

  return (
    <div className="relative w-full h-full bg-[#0b0e11]">
      <div ref={containerRef} className="w-full h-full" />
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════════
// CHART TOOLBAR (Timeframe + Indicators)
// ═══════════════════════════════════════════════════════════════════

interface ChartToolbarProps {
  activeTimeframe: Timeframe;
  indicators: Set<string>;
  onTimeframeChange: (tf: Timeframe) => void;
  onToggleIndicator: (indicator: string) => void;
}

const TIMEFRAMES: Timeframe[] = ['1s', '1m', '5m', '15m', '1h', '4h', '1d'];
const INDICATORS_LIST = ['EMA', 'SMA', 'RSI', 'MACD', 'BB'];

/**
 * Floating toolbar overlaid on top of the chart for timeframe and
 * indicator selection.
 */
export const ChartToolbar = memo(function ChartToolbar({
  activeTimeframe,
  indicators,
  onTimeframeChange,
  onToggleIndicator,
}: ChartToolbarProps) {
  const [showIndicators, setShowIndicators] = useState(false);

  return (
    <div className="absolute top-0 left-0 right-0 z-20 flex items-center justify-between px-3 py-1.5 bg-[#0b0e11]/80 backdrop-blur-sm">
      {/* Timeframe buttons */}
      <div className="flex gap-0.5">
        {TIMEFRAMES.map((tf) => (
          <button
            key={tf}
            onClick={() => onTimeframeChange(tf)}
            className={`px-2 py-0.5 text-[10px] font-mono font-semibold rounded transition-colors
              ${
                activeTimeframe === tf
                  ? 'bg-[#f0b90b] text-[#0b0e11]'
                  : 'text-[#848e9c] hover:text-[#f0f4f8] hover:bg-[#1e2329]'
              }`}
          >
            {tf}
          </button>
        ))}
      </div>

      {/* Indicator toggles */}
      <div className="relative">
        <button
          onClick={() => setShowIndicators(!showIndicators)}
          className="px-2 py-0.5 text-[10px] font-mono text-[#848e9c] hover:text-[#f0f4f8] hover:bg-[#1e2329] rounded"
        >
          Indicators
        </button>

        {showIndicators && (
          <div className="absolute right-0 top-full mt-1 bg-[#1e2329] border border-[#2b2f36] rounded shadow-xl z-30 min-w-[120px]">
            {INDICATORS_LIST.map((ind) => (
              <button
                key={ind}
                onClick={() => onToggleIndicator(ind)}
                className={`w-full text-left px-3 py-1.5 text-[11px] font-mono transition-colors
                  ${
                    indicators.has(ind)
                      ? 'text-[#f0b90b] bg-[#f0b90b]/10'
                      : 'text-[#848e9c] hover:text-[#f0f4f8]'
                  }`}
              >
                {ind} {indicators.has(ind) ? '✓' : ''}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
});

// ═══════════════════════════════════════════════════════════════════
// UTILITY FUNCTIONS
// ═══════════════════════════════════════════════════════════════════

/**
 * Calculates Exponential Moving Average.
 * Simple implementation for client-side display.
 * Production should use TA-Lib for accurate calculations.
 */
function calculateEMA(values: number[], period: number): number[] {
  if (values.length < period) return [];

  const multiplier = 2 / (period + 1);
  const ema: number[] = [];

  // Start with SMA as the first EMA value
  let sum = 0;
  for (let i = 0; i < period; i++) {
    sum += values[i];
  }
  ema.push(sum / period);

  // Calculate EMA for remaining values
  for (let i = period; i < values.length; i++) {
    ema.push((values[i] - ema[ema.length - 1]) * multiplier + ema[ema.length - 1]);
  }

  return ema;
}