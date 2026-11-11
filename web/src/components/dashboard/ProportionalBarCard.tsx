import { useMemo, useState } from 'react';
import { AlertCircle, Info, Layers } from 'lucide-react';
import { DropdownSelect } from '@/components/ui/DropdownMenu';
import { formatRupiahDisplay, parseMonetaryAmount } from '@/lib/chartMath';
import { cn } from '@/lib/utils';
import { DASHBOARD_PERIOD_OPTIONS, type DashboardPeriodOption } from './periodOptions';

export interface ProportionalSegment {
  id: string;
  label: string;
  amount: string;
  percent: number;
  color: string;
  swatchClass: string;
  labelTextClass?: string;
}

export interface ProportionalBarCardProps {
  title?: string;
  value?: string;
  changePercent?: number;
  segments?: ProportionalSegment[];
  formula?: string;
  dataScope?: string;
  isLoading?: boolean;
  error?: string | null;
  className?: string;
}

const PERIOD_MULTIPLIERS: Record<DashboardPeriodOption, { multiplier: number; change: number }> = {
  'This Month': { multiplier: 1, change: -1.5 },
  'Last 6 Months': { multiplier: 5.72, change: 2.1 },
  'Year 2026': { multiplier: 11.4, change: 3.4 },
};

export function ProportionalBarCard({
  title = 'Cost Overview',
  value = 'Rp 485,200,000',
  changePercent = -1.5,
  segments: customSegments,
  formula = 'Distribution of gross payroll components including base wages, allowances, and statutory employer taxes.',
  dataScope = 'Active Legal Entity',
  isLoading = false,
  error = null,
  className,
}: ProportionalBarCardProps) {
  const [showInfo, setShowInfo] = useState(false);
  const [selectedPeriod, setSelectedPeriod] = useState<DashboardPeriodOption>('This Month');
  const [hoveredSegmentId, setHoveredSegmentId] = useState<string | null>(null);

  const periodConfig = PERIOD_MULTIPLIERS[selectedPeriod];
  const baseNumeric = parseMonetaryAmount(value);

  const displayHeadline = useMemo(() => {
    if (selectedPeriod === 'This Month' || baseNumeric <= 0) {
      return value;
    }
    return formatRupiahDisplay(Math.round(baseNumeric * periodConfig.multiplier));
  }, [value, selectedPeriod, baseNumeric, periodConfig.multiplier]);

  const displayChange = selectedPeriod === 'This Month' ? changePercent : periodConfig.change;

  const activeSegments: ProportionalSegment[] = useMemo(() => {
    if (customSegments) return customSegments;

    const totalNumeric = parseMonetaryAmount(displayHeadline);
    // Keep canonical HrAdmin monthly breakdown numbers exact when value = Rp 485,200,000
    if (totalNumeric === 485_200_000) {
      return [
        {
          id: 'base',
          label: 'Base Salary',
          amount: 'Rp 340,000,000',
          percent: 70,
          color: 'bg-gradient-to-r from-[#1859E9] to-[#2B6FF5]',
          swatchClass: 'bg-[#1859E9]',
          labelTextClass: 'text-white',
        },
        {
          id: 'allowance',
          label: 'Allowances',
          amount: 'Rp 96,966,000',
          percent: 20,
          color: 'bg-[#9BB8F8]',
          swatchClass: 'bg-[#9BB8F8]',
          labelTextClass: 'text-[#0F296D]',
        },
        {
          id: 'tax',
          label: 'Tax & BPJS',
          amount: 'Rp 48,234,000',
          percent: 10,
          color: 'bg-[#EEF2F7]',
          swatchClass: 'bg-[#CBD5E1]',
          labelTextClass: 'text-slate-800',
        },
      ];
    }

    const baseAmt = Math.round(totalNumeric * 0.7);
    const allowAmt = Math.round(totalNumeric * 0.2);
    const taxAmt = Math.max(0, totalNumeric - baseAmt - allowAmt);

    return [
      {
        id: 'base',
        label: 'Base Salary',
        amount: formatRupiahDisplay(baseAmt),
        percent: 70,
        color: 'bg-gradient-to-r from-[#1859E9] to-[#2B6FF5]',
        swatchClass: 'bg-[#1859E9]',
        labelTextClass: 'text-white',
      },
      {
        id: 'allowance',
        label: 'Allowances',
        amount: formatRupiahDisplay(allowAmt),
        percent: 20,
        color: 'bg-[#9BB8F8]',
        swatchClass: 'bg-[#9BB8F8]',
        labelTextClass: 'text-[#0F296D]',
      },
      {
        id: 'tax',
        label: 'Tax & BPJS',
        amount: formatRupiahDisplay(taxAmt),
        percent: 10,
        color: 'bg-[#EEF2F7]',
        swatchClass: 'bg-[#CBD5E1]',
        labelTextClass: 'text-slate-800',
      },
    ];
  }, [customSegments, displayHeadline]);

  const hoveredSegment = activeSegments.find((seg) => seg.id === hoveredSegmentId) ?? null;

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

          {/* Info Button */}
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
                <div className="border-b border-slate-700 pb-1 font-semibold text-blue-400">
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
          onChange={setSelectedPeriod}
          panelClassName="min-w-[208px]"
        />
      </div>

      {/* Metric Row with Delta Tag */}
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-3xl sm:text-4xl font-semibold text-[#111827] tracking-tight tabular-nums">
          {displayHeadline}
        </span>
        {displayChange !== undefined && (
          <div
            className={cn(
              'inline-flex items-center gap-1 px-3.5 py-1.5 rounded-full text-xs sm:text-[13px] font-semibold leading-none tabular-nums',
              displayChange < 0 ? 'bg-[#FFE4E6] text-[#BE123C]' : 'bg-[#DCFCE7] text-[#15803D]'
            )}
          >
            <span>{displayChange >= 0 ? `+${displayChange}%` : `+${Math.abs(displayChange)}%`}</span>
            <span className="text-xs leading-none">{displayChange < 0 ? '↓' : '↑'}</span>
          </div>
        )}
      </div>

      {/* States / Spending Breakdown Section */}
      {isLoading ? (
        <div className="space-y-3.5 pt-1 animate-pulse" aria-busy="true" aria-label={`Loading ${title}`}>
          <div className="h-4 w-44 rounded bg-slate-200" />
          <div className="grid grid-cols-3 gap-3">
            {Array.from({ length: 3 }).map((_, idx) => (
              <div key={idx} className="space-y-1.5">
                <div className="h-3 w-20 rounded bg-slate-200" />
                <div className="h-4 w-24 rounded bg-slate-200" />
              </div>
            ))}
          </div>
          <div className="h-12 sm:h-14 w-full rounded-xl bg-slate-200" />
        </div>
      ) : error ? (
        <div
          role="alert"
          className="rounded-xl bg-rose-50/60 border border-rose-200 p-4 flex items-center gap-2.5 text-xs text-rose-800"
        >
          <AlertCircle className="h-4 w-4 text-rose-600 shrink-0" />
          <span>{error}</span>
        </div>
      ) : activeSegments.length === 0 ? (
        <div className="rounded-xl bg-slate-50/70 border border-dashed border-slate-200 p-5 flex items-center justify-center gap-2 text-xs text-slate-500">
          <Layers className="h-4 w-4 text-slate-400" />
          <span>No workforce cost breakdown available.</span>
        </div>
      ) : (
        <div
          className="space-y-3.5 pt-1"
          onMouseLeave={() => setHoveredSegmentId(null)}
        >
          <div className="flex items-center justify-between gap-2 min-h-[22px]">
            <span className="text-sm sm:text-[15px] font-medium text-slate-800 block">
              Workforce Cost Breakdown
            </span>

            {hoveredSegment && (
              <span className="text-xs font-medium text-slate-700 bg-slate-100 px-2.5 py-0.5 rounded-full tabular-nums animate-in fade-in duration-150">
                {hoveredSegment.label}: <strong className="text-slate-900">{hoveredSegment.percent}%</strong>
              </span>
            )}
          </div>

          {/* Responsive Legend (stacked full-amount rows on small mobile, 3 columns from sm+) */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 sm:gap-3">
            {activeSegments.map((seg) => {
              const isDimmed = hoveredSegmentId !== null && hoveredSegmentId !== seg.id;
              return (
                <button
                  key={seg.id}
                  type="button"
                  onMouseEnter={() => setHoveredSegmentId(seg.id)}
                  onFocus={() => setHoveredSegmentId(seg.id)}
                  onBlur={() => setHoveredSegmentId(null)}
                  className={cn(
                    'flex items-center justify-between sm:block text-left sm:space-y-1 rounded-lg p-1 -m-1 transition-opacity focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600',
                    isDimmed && 'opacity-55'
                  )}
                >
                  <div className="flex items-center gap-1.5 min-w-0">
                    <span className={cn('h-2.5 w-2.5 rounded-full shrink-0', seg.swatchClass)} />
                    <span className="text-xs sm:text-[13px] font-medium text-[#64748B] truncate">
                      {seg.label}
                    </span>
                  </div>
                  <div className="text-sm sm:text-base font-semibold text-slate-900 sm:pl-4 tabular-nums whitespace-nowrap sm:truncate">
                    {seg.amount}
                  </div>
                </button>
              );
            })}
          </div>

          {/* Thick Proportional Bar */}
          <div
            className="h-12 sm:h-14 w-full rounded-xl overflow-hidden flex shadow-2xs border border-slate-100"
            role="img"
            aria-label={`Cost breakdown: ${activeSegments.map((s) => `${s.label} ${s.percent}%`).join(', ')}`}
          >
            {activeSegments.map((seg) => {
              const isHovered = hoveredSegmentId === seg.id;
              const isDimmed = hoveredSegmentId !== null && !isHovered;
              return (
                <div
                  key={seg.id}
                  onMouseEnter={() => setHoveredSegmentId(seg.id)}
                  className={cn(
                    'h-full transition-all duration-200 flex items-center justify-center cursor-default',
                    seg.color,
                    isDimmed && 'opacity-55',
                    isHovered && 'brightness-[0.98]'
                  )}
                  style={{ width: `${seg.percent}%` }}
                  aria-label={`${seg.label}: ${seg.amount} (${seg.percent}%)`}
                >
                  {seg.percent >= 15 && (
                    <span
                      className={cn(
                        'text-xs font-semibold tabular-nums select-none',
                        seg.labelTextClass || 'text-white'
                      )}
                    >
                      {seg.percent}%
                    </span>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
