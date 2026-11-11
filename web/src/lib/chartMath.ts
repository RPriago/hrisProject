export interface ChartPointCoord {
  x: number;
  y: number;
}

export interface NiceScaleResult {
  domainMin: number;
  domainMax: number;
  step: number;
  ticks: number[];
}

export interface MonotoneSplinePaths {
  linePath: string;
  areaPath: string;
}

/**
 * Parses a formatted IDR string (e.g. "Rp 485,200,000" or "Rp 1.205.850.000")
 * into its integer numeric amount for consistent chart scaling.
 */
export function parseMonetaryAmount(input: string | number): number {
  if (typeof input === 'number') {
    return Number.isFinite(input) ? input : 0;
  }

  const trimmed = input.trim();
  if (!trimmed) return 0;

  const isNegative = trimmed.startsWith('-');
  const digits = trimmed.replace(/[^0-9]/g, '');
  if (!digits) return 0;

  const parsed = Number(digits);
  if (!Number.isFinite(parsed)) return 0;
  return isNegative ? -parsed : parsed;
}

/**
 * Formats a rupiah integer using comma thousand separators (e.g. "Rp 485,200,000"),
 * matching the dashboard KPI typography across Quorum.
 */
export function formatRupiahDisplay(amount: number): string {
  const safe = Math.round(Number.isFinite(amount) ? amount : 0);
  const abs = Math.abs(safe);
  const formatted = abs.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return safe < 0 ? `-Rp ${formatted}` : `Rp ${formatted}`;
}

/**
 * Formats large rupiah numbers concisely for chart Y-axis ticks ("0", "20M", "450M", "1.2B").
 */
export function formatCompactRupiahTick(amount: number): string {
  const abs = Math.abs(amount);
  const sign = amount < 0 ? '-' : '';

  if (abs === 0) return '0';

  if (abs >= 1_000_000_000) {
    const val = (abs / 1_000_000_000).toFixed(1).replace(/\.0$/, '');
    return `${sign}${val}B`;
  }

  if (abs >= 1_000_000) {
    const val = (abs / 1_000_000).toFixed(1).replace(/\.0$/, '');
    return `${sign}${val}M`;
  }

  if (abs >= 1_000) {
    const val = Math.round(abs / 1_000);
    return `${sign}${val}K`;
  }

  return `${sign}${Math.round(abs)}`;
}

/**
 * Computes human-readable round tick values (1, 2, 2.5, 5 * 10^n) that enclose
 * the provided numeric range without cutting off peaks or troughs.
 */
export function computeNiceScale(
  dataMin: number,
  dataMax: number,
  options: {
    desiredIntervals?: number;
    startAtZero?: boolean;
    lowerPaddingRatio?: number;
    upperPaddingRatio?: number;
  } = {}
): NiceScaleResult {
  const {
    desiredIntervals = 3,
    startAtZero = true,
    lowerPaddingRatio = 0.14,
    upperPaddingRatio = 0.14,
  } = options;

  let minVal = Number.isFinite(dataMin) ? dataMin : 0;
  let maxVal = Number.isFinite(dataMax) ? dataMax : 0;

  if (minVal > maxVal) {
    [minVal, maxVal] = [maxVal, minVal];
  }

  if (startAtZero && minVal > 0) {
    minVal = 0;
  }

  // Guard flat or zero series with a sensible visual band
  if (maxVal === minVal) {
    if (maxVal === 0) {
      return {
        domainMin: 0,
        domainMax: 100,
        step: 25,
        ticks: [0, 25, 50, 75, 100],
      };
    }
    const delta = Math.abs(maxVal) * 0.15;
    minVal = startAtZero ? 0 : Math.max(0, minVal - delta);
    maxVal = maxVal + delta;
  }

  const rawRange = maxVal - minVal;
  const paddedMin = startAtZero
    ? Math.min(0, minVal)
    : Math.max(0, minVal - rawRange * lowerPaddingRatio);
  const paddedMax = maxVal + rawRange * upperPaddingRatio;

  const span = Math.max(1, paddedMax - paddedMin);
  const targetStep = span / Math.max(1, desiredIntervals);

  const magnitude = Math.pow(10, Math.floor(Math.log10(targetStep)));
  const residual = targetStep / magnitude;

  let niceFactor: number;
  if (residual <= 1) {
    niceFactor = 1;
  } else if (residual <= 2) {
    niceFactor = 2;
  } else if (residual <= 2.5) {
    niceFactor = 2.5;
  } else if (residual <= 5) {
    niceFactor = 5;
  } else {
    niceFactor = 10;
  }

  const step = niceFactor * magnitude;
  const domainMin = startAtZero
    ? Math.min(0, Math.floor(paddedMin / step) * step)
    : Math.floor(paddedMin / step) * step;
  const domainMax = Math.ceil(paddedMax / step) * step;

  const ticks: number[] = [];
  // Float rounding guard avoids IEEE-754 drift (e.g. 0.30000000000000004)
  const count = Math.round((domainMax - domainMin) / step);
  for (let i = 0; i <= count; i++) {
    ticks.push(Math.round((domainMin + i * step) * 1e6) / 1e6);
  }

  return {
    domainMin,
    domainMax,
    step,
    ticks,
  };
}

/**
 * Generates SVG cubic Bezier path strings using Fritsch-Carlson monotone cubic
 * interpolation (matching D3's curveMonotoneX). Ensures the curve passes through
 * every exact data vertex without artificial overshoot between points.
 */
export function buildMonotoneSplinePath(
  points: ReadonlyArray<ChartPointCoord>,
  baselineY: number
): MonotoneSplinePaths {
  if (points.length === 0) {
    return { linePath: '', areaPath: '' };
  }

  if (points.length === 1) {
    const p = points[0];
    const linePath = `M ${p.x.toFixed(2)} ${p.y.toFixed(2)}`;
    return { linePath, areaPath: '' };
  }

  if (points.length === 2) {
    const [p0, p1] = points;
    const linePath = `M ${p0.x.toFixed(2)} ${p0.y.toFixed(2)} L ${p1.x.toFixed(2)} ${p1.y.toFixed(2)}`;
    const areaPath = `${linePath} L ${p1.x.toFixed(2)} ${baselineY.toFixed(2)} L ${p0.x.toFixed(2)} ${baselineY.toFixed(2)} Z`;
    return { linePath, areaPath };
  }

  const n = points.length;
  const slopes: number[] = [];
  const dxs: number[] = [];

  for (let i = 0; i < n - 1; i++) {
    const dx = points[i + 1].x - points[i].x;
    const dy = points[i + 1].y - points[i].y;
    dxs.push(dx);
    slopes.push(dx === 0 ? 0 : dy / dx);
  }

  const tangents: number[] = new Array(n).fill(0);
  tangents[0] = slopes[0];
  tangents[n - 1] = slopes[n - 2];

  for (let i = 1; i < n - 1; i++) {
    const sPrev = slopes[i - 1];
    const sNext = slopes[i];
    if (sPrev * sNext <= 0) {
      // Zero slope at local extrema prevents spline overshoot
      tangents[i] = 0;
    } else {
      tangents[i] = (sPrev + sNext) / 2;
    }
  }

  // Enforce strict monotonicity across every segment
  for (let i = 0; i < n - 1; i++) {
    const s = slopes[i];
    if (s === 0) {
      tangents[i] = 0;
      tangents[i + 1] = 0;
      continue;
    }

    const alpha = tangents[i] / s;
    const beta = tangents[i + 1] / s;
    const mag = alpha * alpha + beta * beta;
    if (mag > 9) {
      const tau = 3 / Math.sqrt(mag);
      tangents[i] = tau * alpha * s;
      tangents[i + 1] = tau * beta * s;
    }
  }

  const segments: string[] = [
    `M ${points[0].x.toFixed(2)} ${points[0].y.toFixed(2)}`,
  ];

  for (let i = 0; i < n - 1; i++) {
    const p0 = points[i];
    const p1 = points[i + 1];
    const dxThird = dxs[i] / 3;

    const cp1x = p0.x + dxThird;
    const cp1y = p0.y + tangents[i] * dxThird;
    const cp2x = p1.x - dxThird;
    const cp2y = p1.y - tangents[i + 1] * dxThird;

    segments.push(
      `C ${cp1x.toFixed(2)} ${cp1y.toFixed(2)}, ${cp2x.toFixed(2)} ${cp2y.toFixed(2)}, ${p1.x.toFixed(2)} ${p1.y.toFixed(2)}`
    );
  }

  const linePath = segments.join(' ');
  const first = points[0];
  const last = points[n - 1];
  const areaPath = `${linePath} L ${last.x.toFixed(2)} ${baselineY.toFixed(2)} L ${first.x.toFixed(2)} ${baselineY.toFixed(2)} Z`;

  return { linePath, areaPath };
}
