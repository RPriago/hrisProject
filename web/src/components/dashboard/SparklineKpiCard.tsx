import { useId, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react';
import { AlertCircle, Info, TrendingUp } from 'lucide-react';
import { DropdownSelect } from '@/components/ui/DropdownMenu';
import {
  buildMonotoneSplinePath,
  computeNiceScale,
  formatCompactRupiahTick,
  formatRupiahDisplay,
  parseMonetaryAmount,
} from '@/lib/chartMath';
import { cn } from '@/lib/utils';
import { DASHBOARD_PERIOD_OPTIONS, type DashboardPeriodOption } from './periodOptions';
import { useChartWidth } from './useChartDimensions';

export interface SparklineDataPoint {
  label: string;
  tooltipLabel?: string;
  amount: number;
}

export interface SparklineKpiCardProps {
  title?: string;
  value?: string;
  changePercent?: number;
  comparisonPeriod?: string;
  formula?: string;
  dataScope?: string;
  points?: SparklineDataPoint[];
  isLoading?: boolean;
  error?: string | null;
  className?: string;
}

const REFERENCE_BASE_AMOUNT = 485_200_000;
const SVG_HEIGHT = 176;
const PAD_TOP = 46;
const PAD_BOTTOM = 10;
const PAD_LEFT = 18;
const PAD_RIGHT = 18;

export function SparklineKpiCard({
  title = 'Payroll Overview',
  value = 'Rp 485,200,000',
  changePercent = 1.5,
  comparisonPeriod = 'vs last month',
  formula = 'Accumulated net salary, allowances, and approved overtime in the current active cut-off cycle.',
  dataScope = 'Active Legal Entity',
  points: customPoints,
  isLoading = false,
  error = null,
  className,
}: SparklineKpiCardProps) {
  const [showInfo, setShowInfo] = useState(false);
  const [selectedPeriod, setSelectedPeriod] = useState<DashboardPeriodOption>('This Month');
  const [activePointIndex, setActivePointIndex] = useState<number>(3);
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);

  const plotContainerRef = useRef<HTMLDivElement | null>(null);
  const svgWidth = useChartWidth(plotContainerRef, 520);
  const rawGradientId = useId();
  const gradientId = `sparkline-area-${rawGradientId.replace(/[^a-zA-Z0-9_-]/g, '')}`;

  const parsedHeadline = parseMonetaryAmount(value);
  const scaleRatio = parsedHeadline > 0 ? parsedHeadline / REFERENCE_BASE_AMOUNT : 1;

  const periodDataset = useMemo(() => {
    if (customPoints) {
      return {
        headlineValue: value,
        headlineChange: changePercent,
        series: customPoints,
      };
    }

    if (selectedPeriod === 'Last 6 Months') {
      const scaled = (raw: number) => Math.round(raw * scaleRatio);
      const series: SparklineDataPoint[] = [
        { label: 'Nov', tooltipLabel: 'Nov 2025', amount: scaled(388_400_000) },
        { label: 'Dec', tooltipLabel: 'Dec 2025', amount: scaled(426_900_000) },
        { label: 'Jan', tooltipLabel: 'Jan 2026', amount: scaled(410_500_000) },
        { label: 'Feb', tooltipLabel: 'Feb 2026', amount: scaled(435_000_000) },
        { label: 'Mar', tooltipLabel: 'Mar 2026', amount: scaled(448_700_000) },
        {
          label: 'Apr',
          tooltipLabel: 'Apr 2026',
          amount: parsedHeadline > 0 ? parsedHeadline : 485_200_000,
        },
      ];
      const sum = series.reduce((acc, pt) => acc + pt.amount, 0);
      return {
        headlineValue: formatRupiahDisplay(sum),
        headlineChange: 3.2,
        series,
      };
    }

    if (selectedPeriod === 'Year 2026') {
      const scaled = (raw: number) => Math.round(raw * scaleRatio);
      const series: SparklineDataPoint[] = [
        { label: 'Jan', tooltipLabel: 'Jan 2026', amount: scaled(410_500_000) },
        { label: 'Feb', tooltipLabel: 'Feb 2026', amount: scaled(435_000_000) },
        { label: 'Mar', tooltipLabel: 'Mar 2026', amount: scaled(420_200_000) },
        {
          label: 'Apr',
          tooltipLabel: 'Apr 2026',
          amount: parsedHeadline > 0 ? parsedHeadline : 485_200_000,
        },
        { label: 'May', tooltipLabel: 'May 2026', amount: scaled(462_800_000) },
        { label: 'Jun', tooltipLabel: 'Jun 2026', amount: scaled(478_000_000) },
        { label: 'Jul', tooltipLabel: 'Jul 2026', amount: scaled(472_400_000) },
        { label: 'Aug', tooltipLabel: 'Aug 2026', amount: scaled(496_200_000) },
        { label: 'Sep', tooltipLabel: 'Sep 2026', amount: scaled(488_900_000) },
        { label: 'Oct', tooltipLabel: 'Oct 2026', amount: scaled(504_100_000) },
        { label: 'Nov', tooltipLabel: 'Nov 2026', amount: scaled(518_600_000) },
        { label: 'Dec', tooltipLabel: 'Dec 2026', amount: scaled(536_400_000) },
      ];
      const ytdSum = series.reduce((acc, pt) => acc + pt.amount, 0);
      return {
        headlineValue: formatRupiahDisplay(ytdSum),
        headlineChange: 4.8,
        series,
      };
    }

    // Default "This Month" keeps exact caller-supplied KPI headline & active 6-month cut-off window
    const scaled = (raw: number) => Math.round(raw * scaleRatio);
    const series: SparklineDataPoint[] = [
      { label: 'Jan', tooltipLabel: 'Jan 2026', amount: scaled(410_500_000) },
      { label: 'Feb', tooltipLabel: 'Feb 2026', amount: scaled(435_000_000) },
      { label: 'Mar', tooltipLabel: 'Mar 2026', amount: scaled(420_200_000) },
      {
        label: 'Apr',
        tooltipLabel: 'Apr 2026',
        amount: parsedHeadline > 0 ? parsedHeadline : 485_200_000,
      },
      { label: 'May', tooltipLabel: 'May 2026', amount: scaled(462_800_000) },
      { label: 'Jun', tooltipLabel: 'Jun 2026', amount: scaled(478_000_000) },
    ];
    return {
      headlineValue: value,
      headlineChange: changePercent,
      series,
    };
  }, [customPoints, value, changePercent, selectedPeriod, scaleRatio, parsedHeadline]);

  const series = periodDataset.series;
  const safeActiveIndex = Math.min(Math.max(activePointIndex, 0), Math.max(0, series.length - 1));
  const focusedIndex =
    hoveredIndex !== null && hoveredIndex >= 0 && hoveredIndex < series.length
      ? hoveredIndex
      : safeActiveIndex;

  const yScale = useMemo(() => {
    if (series.length === 0) {
      return computeNiceScale(0, 100, { desiredIntervals: 3, startAtZero: false });
    }
    const amounts = series.map((pt) => pt.amount);
    return computeNiceScale(Math.min(...amounts), Math.max(...amounts), {
      desiredIntervals: 3,
      startAtZero: false,
      lowerPaddingRatio: 0.24,
      upperPaddingRatio: 0.18,
    });
  }, [series]);

  const innerLeft = PAD_LEFT;
  const innerRight = Math.max(innerLeft + 80, svgWidth - PAD_RIGHT);
  const innerWidth = innerRight - innerLeft;
  const innerTop = PAD_TOP;
  const innerBottom = SVG_HEIGHT - PAD_BOTTOM;
  const innerHeight = innerBottom - innerTop;
  const domainSpan = Math.max(1, yScale.domainMax - yScale.domainMin);

  const valueToY = (amount: number): number => {
    const ratio = (amount - yScale.domainMin) / domainSpan;
    return innerBottom - ratio * innerHeight;
  };

  const indexToX = (idx: number): number => {
    if (series.length <= 1) {
      return innerLeft + innerWidth / 2;
    }
    return innerLeft + (idx / (series.length - 1)) * innerWidth;
  };

  const coords = series.map((pt, idx) => ({
    x: indexToX(idx),
    y: valueToY(pt.amount),
  }));

  const { linePath, areaPath } = useMemo(
    () => buildMonotoneSplinePath(coords, innerBottom),
    [coords, innerBottom]
  );

  const focusedPoint = series[focusedIndex];
  const focusedCoord = coords[focusedIndex];

  const handlePeriodChange = (nextPeriod: DashboardPeriodOption) => {
    setSelectedPeriod(nextPeriod);
    setHoveredIndex(null);
    if (nextPeriod === 'Last 6 Months') {
      setActivePointIndex(5);
    } else if (nextPeriod === 'Year 2026') {
      setActivePointIndex(7);
    } else {
      setActivePointIndex(3);
    }
  };

  const handleKeyNavigation = (e: KeyboardEvent<HTMLElement>) => {
    if (series.length === 0) return;
    if (e.key === 'ArrowRight') {
      e.preventDefault();
      setActivePointIndex((prev) => Math.min(series.length - 1, prev + 1));
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault();
      setActivePointIndex((prev) => Math.max(0, prev - 1));
    } else if (e.key === 'Home') {
      e.preventDefault();
      setActivePointIndex(0);
    } else if (e.key === 'End') {
      e.preventDefault();
      setActivePointIndex(series.length - 1);
    }
  };

  const getTooltipStyle = (xPos: number, yPos: number): { style: CSSProperties; centered: boolean } => {
    const topPx = Math.max(0, Math.round(yPos - 44));
    const leftBoundary = svgWidth * 0.34;
    const rightBoundary = svgWidth * 0.66;

    if (xPos < leftBoundary) {
      return {
        centered: false,
        style: {
          top: `${topPx}px`,
          left: '0px',
        },
      };
    }

    if (xPos > rightBoundary) {
      return {
        centered: false,
        style: {
          top: `${topPx}px`,
          right: '0px',
        },
      };
    }

    return {
      centered: true,
      style: {
        top: `${topPx}px`,
        left: `${Math.round(xPos)}px`,
      },
    };
  };

  const tooltipPlacement = focusedCoord ? getTooltipStyle(focusedCoord.x, focusedCoord.y) : null;
  const compactXAxis = series.length > 7 && svgWidth < 400;

  return (
    <div
      className={cn(
        'relative p-5 sm:p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs flex flex-col justify-between gap-4 overflow-visible',
        className
      )}
    >
      {/* Header Row */}
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <span className="text-base font-medium text-[#1E232D] tracking-tight">
            {title}
          </span>

          {/* Info Button with Formula Popover */}
          <div className="relative">
            <button
              type="button"
              onClick={() => setShowInfo((prev) => !prev)}
              aria-expanded={showInfo}
              className="h-7 w-7 rounded-lg hover:bg-slate-100 flex items-center justify-center text-slate-400 hover:text-slate-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
              aria-label={`Metric calculation information for ${title}`}
            >
              <Info className="h-4 w-4" />
            </button>

            {showInfo && (
              <div
                className="absolute left-0 top-full mt-1.5 w-64 p-3.5 rounded-xl bg-slate-900 text-white shadow-xl z-30 text-xs space-y-2 animate-in fade-in zoom-in-95 duration-150"
                role="tooltip"
              >
                <div className="border-b border-slate-700 pb-1 font-bold text-blue-400">
                  Calculation Formula
                </div>
                <p className="text-slate-300 text-[11px] leading-relaxed">{formula}</p>
                <div className="text-[10px] text-slate-400 pt-1 border-t border-slate-800">
                  Data Scope: <span className="text-slate-200 font-semibold">{dataScope}</span>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Period Selector Dropdown */}
        <DropdownSelect
          value={selectedPeriod}
          options={DASHBOARD_PERIOD_OPTIONS}
          onChange={handlePeriodChange}
          panelClassName="min-w-[208px]"
        />
      </div>

      {/* Metric Row with Delta Tag */}
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-3xl sm:text-4xl font-semibold text-[#111827] tracking-tight tabular-nums">
          {periodDataset.headlineValue}
        </span>
        {periodDataset.headlineChange !== undefined && (
          <div
            title={`${periodDataset.headlineChange}% ${comparisonPeriod}`}
            className="inline-flex items-center gap-1 px-3.5 py-1.5 rounded-full bg-[#11141D] text-white text-xs sm:text-[13px] font-semibold leading-none tabular-nums"
          >
            <span>
              {periodDataset.headlineChange >= 0 ? `+${periodDataset.headlineChange}%` : `${periodDataset.headlineChange}%`}
            </span>
            <span className="text-xs leading-none">
              {periodDataset.headlineChange >= 0 ? '↑' : '↓'}
            </span>
          </div>
        )}
      </div>

      {/* States: Loading / Error / Empty / Chart */}
      {isLoading ? (
        <div
          className="h-[214px] w-full rounded-xl bg-slate-50 border border-slate-100 p-4 flex flex-col justify-between animate-pulse"
          aria-busy="true"
          aria-label={`Loading ${title} chart`}
        >
          <div className="h-6 w-36 rounded-md bg-slate-200/80" />
          <div className="h-24 w-full rounded-lg bg-slate-200/60" />
          <div className="flex justify-between gap-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <div key={i} className="h-3 w-8 rounded bg-slate-200/80" />
            ))}
          </div>
        </div>
      ) : error ? (
        <div
          role="alert"
          className="h-[214px] w-full rounded-xl bg-rose-50/60 border border-rose-200 p-5 flex flex-col items-center justify-center text-center gap-2"
        >
          <AlertCircle className="h-5 w-5 text-rose-600" />
          <p className="text-sm font-medium text-rose-900">Unable to load chart data</p>
          <p className="text-xs text-rose-700 max-w-xs">{error}</p>
        </div>
      ) : series.length === 0 ? (
        <div className="h-[214px] w-full rounded-xl bg-slate-50/70 border border-dashed border-slate-200 p-5 flex flex-col items-center justify-center text-center gap-2">
          <TrendingUp className="h-5 w-5 text-slate-400" />
          <p className="text-sm font-medium text-slate-700">No payroll trend points yet</p>
          <p className="text-xs text-slate-500">Trend visualization appears after the first completed payroll run.</p>
        </div>
      ) : (
        <div
          className="pt-1 relative"
          onMouseLeave={() => setHoveredIndex(null)}
        >
          <div className="flex items-start gap-2 sm:gap-3">
            {/* Synchronized Y-Axis Ticks */}
            <div
              className="w-10 sm:w-12 shrink-0 relative select-none"
              style={{ height: `${SVG_HEIGHT}px` }}
              aria-hidden="true"
            >
              {yScale.ticks.map((tick) => {
                const topPx = valueToY(tick);
                return (
                  <span
                    key={tick}
                    className="absolute right-0 -translate-y-1/2 text-[11px] font-medium text-[#64748B] tabular-nums leading-none"
                    style={{ top: `${topPx}px` }}
                  >
                    {formatCompactRupiahTick(tick)}
                  </span>
                );
              })}
            </div>

            {/* Plot Area + Synchronized X-Axis */}
            <div ref={plotContainerRef} className="flex-1 min-w-0 relative">
              {/* Floating Tooltip above focused point */}
              {focusedPoint && tooltipPlacement && (
                <div
                  className={cn(
                    'absolute z-20 pointer-events-none transition-all duration-150',
                    tooltipPlacement.centered && '-translate-x-1/2'
                  )}
                  style={tooltipPlacement.style}
                >
                  <div className="px-2.5 py-1 sm:px-3 sm:py-1.5 rounded-lg bg-white border border-slate-200 shadow-md text-[11px] sm:text-[13px] font-medium text-slate-700 whitespace-nowrap">
                    <span className="text-slate-500 mr-1.5">
                      {focusedPoint.tooltipLabel || `${focusedPoint.label} 2026`}:
                    </span>
                    <span className="font-semibold text-slate-900 tabular-nums">
                      {formatRupiahDisplay(focusedPoint.amount)}
                    </span>
                  </div>
                </div>
              )}

              {/* 1:1 Pixel SVG Plot (no preserveAspectRatio="none") */}
              <svg
                viewBox={`0 0 ${svgWidth} ${SVG_HEIGHT}`}
                width={svgWidth}
                height={SVG_HEIGHT}
                className="block w-full overflow-visible"
                role="img"
                aria-label={`${title} trend chart from ${series[0].label} to ${series[series.length - 1].label}. Active ${focusedPoint?.tooltipLabel || focusedPoint?.label}: ${focusedPoint ? formatRupiahDisplay(focusedPoint.amount) : value}`}
              >
                <defs>
                  <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#1859E9" stopOpacity="0.24" />
                    <stop offset="85%" stopColor="#1859E9" stopOpacity="0.04" />
                    <stop offset="100%" stopColor="#1859E9" stopOpacity="0" />
                  </linearGradient>
                </defs>

                {/* Horizontal Y-Axis Gridlines */}
                {yScale.ticks.map((tick, idx) => {
                  const yPos = valueToY(tick);
                  const isBaseline = idx === 0;
                  return (
                    <line
                      key={tick}
                      x1={innerLeft}
                      y1={yPos}
                      x2={innerRight}
                      y2={yPos}
                      stroke={isBaseline ? '#E2E8F0' : '#F1F5F9'}
                      strokeWidth={1}
                      strokeDasharray={isBaseline ? undefined : '3 3'}
                    />
                  );
                })}

                {/* Vertical Month Guide Gridlines aligned to exact curve vertices */}
                {coords.map((pt, idx) => (
                  <line
                    key={idx}
                    x1={pt.x}
                    y1={innerTop}
                    x2={pt.x}
                    y2={innerBottom}
                    stroke="#F1F5F9"
                    strokeWidth={1}
                    strokeDasharray="3 3"
                  />
                ))}

                {/* Area Gradient Fill */}
                {areaPath && <path d={areaPath} fill={`url(#${gradientId})`} />}

                {/* Monotone Cubic Spline Curve */}
                {linePath && (
                  <path
                    d={linePath}
                    fill="none"
                    stroke="#1859E9"
                    strokeWidth={2.75}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                )}

                {/* Focused Point Stem + True Circular Halo & Marker */}
                {focusedCoord && (
                  <>
                    <line
                      x1={focusedCoord.x}
                      y1={focusedCoord.y + 6}
                      x2={focusedCoord.x}
                      y2={innerBottom}
                      stroke="#1859E9"
                      strokeWidth={1.5}
                      strokeDasharray="3 3"
                    />
                    <circle
                      cx={focusedCoord.x}
                      cy={focusedCoord.y}
                      r={10}
                      fill="#1859E9"
                      fillOpacity={0.14}
                    />
                    <circle
                      cx={focusedCoord.x}
                      cy={focusedCoord.y}
                      r={5.5}
                      fill="#1859E9"
                      stroke="#FFFFFF"
                      strokeWidth={2.5}
                    />
                  </>
                )}

                {/* Wide Pointer Hit Targets per Data Point */}
                {coords.map((pt, idx) => {
                  const sliceWidth = series.length > 1 ? innerWidth / (series.length - 1) : innerWidth;
                  const rectX = Math.max(0, pt.x - sliceWidth / 2);
                  const rectWidth = Math.min(svgWidth - rectX, sliceWidth);

                  return (
                    <rect
                      key={idx}
                      x={rectX}
                      y={innerTop - 12}
                      width={Math.max(12, rectWidth)}
                      height={innerHeight + 16}
                      fill="transparent"
                      className="cursor-pointer"
                      onMouseEnter={() => setHoveredIndex(idx)}
                      onClick={() => setActivePointIndex(idx)}
                    />
                  );
                })}
              </svg>

              {/* X-Axis Labels aligned to exact vertex coordinates */}
              <div
                className="relative h-8 mt-1 border-t border-slate-100"
                role="group"
                aria-label={`${title} period selector`}
                onKeyDown={handleKeyNavigation}
              >
                {series.map((d, idx) => {
                  const isFocused = focusedIndex === idx;
                  const isPinned = safeActiveIndex === idx;
                  const xPx = indexToX(idx);
                  const showLabelText = !compactXAxis;

                  return (
                    <button
                      key={`${d.label}-${idx}`}
                      type="button"
                      onClick={() => setActivePointIndex(idx)}
                      onMouseEnter={() => setHoveredIndex(idx)}
                      onFocus={() => setHoveredIndex(idx)}
                      onBlur={() => setHoveredIndex(null)}
                      aria-pressed={isPinned}
                      aria-label={`${d.tooltipLabel || d.label}: ${formatRupiahDisplay(d.amount)}`}
                      style={{ left: `${xPx}px` }}
                      className={cn(
                        'absolute top-1.5 -translate-x-1/2 rounded-md border transition-colors tabular-nums whitespace-nowrap focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-1',
                        compactXAxis ? 'px-1.5 py-0.5 text-[11px]' : 'px-2 py-0.5 text-xs sm:text-[13px]',
                        isFocused
                          ? 'text-slate-900 font-semibold bg-white border-[#E2E8F0] shadow-2xs z-10'
                          : 'text-[#64748B] font-medium border-transparent hover:text-slate-900'
                      )}
                    >
                      {showLabelText ? d.label : d.label.slice(0, 1)}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
