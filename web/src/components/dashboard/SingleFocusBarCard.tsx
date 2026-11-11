import { useMemo, useState, type KeyboardEvent } from 'react';
import { AlertCircle, BarChart3, Calendar, CalendarDays, Info } from 'lucide-react';
import { DropdownSelect } from '@/components/ui/DropdownMenu';
import {
  computeNiceScale,
  formatCompactRupiahTick,
  formatRupiahDisplay,
} from '@/lib/chartMath';
import { cn } from '@/lib/utils';

export interface CashFlowBarItem {
  shortLabel: string;
  fullLabel: string;
  amount: number; // stored in actual rupiah (e.g. 462_000_000)
}

export interface SingleFocusBarCardProps {
  title?: string;
  isLoading?: boolean;
  error?: string | null;
  className?: string;
}

type CashFlowTab = 'net' | 'tax' | 'claims';
type CashFlowPeriod = 'Yearly' | 'Monthly';

const YEARLY_DATA: Record<CashFlowTab, { total: string; items: CashFlowBarItem[] }> = {
  net: {
    total: 'Rp 5,420,500,000',
    items: [
      { shortLabel: 'Jan', fullLabel: 'January 2026', amount: 220_000_000 },
      { shortLabel: 'Feb', fullLabel: 'February 2026', amount: 280_000_000 },
      { shortLabel: 'Mar', fullLabel: 'March 2026', amount: 240_000_000 },
      { shortLabel: 'Apr', fullLabel: 'April 2026', amount: 310_000_000 },
      { shortLabel: 'May', fullLabel: 'May 2026', amount: 340_000_000 },
      { shortLabel: 'Jun', fullLabel: 'June 2026', amount: 300_000_000 },
      { shortLabel: 'Jul', fullLabel: 'July 2026', amount: 360_000_000 },
      { shortLabel: 'Aug', fullLabel: 'August 2026', amount: 462_000_000 },
      { shortLabel: 'Sep', fullLabel: 'September 2026', amount: 380_000_000 },
      { shortLabel: 'Oct', fullLabel: 'October 2026', amount: 410_000_000 },
      { shortLabel: 'Nov', fullLabel: 'November 2026', amount: 450_000_000 },
      { shortLabel: 'Dec', fullLabel: 'December 2026', amount: 490_000_000 },
    ],
  },
  tax: {
    total: 'Rp 584,200,000',
    items: [
      { shortLabel: 'Jan', fullLabel: 'January 2026', amount: 28_000_000 },
      { shortLabel: 'Feb', fullLabel: 'February 2026', amount: 35_000_000 },
      { shortLabel: 'Mar', fullLabel: 'March 2026', amount: 30_000_000 },
      { shortLabel: 'Apr', fullLabel: 'April 2026', amount: 42_000_000 },
      { shortLabel: 'May', fullLabel: 'May 2026', amount: 45_000_000 },
      { shortLabel: 'Jun', fullLabel: 'June 2026', amount: 40_000_000 },
      { shortLabel: 'Jul', fullLabel: 'July 2026', amount: 48_000_000 },
      { shortLabel: 'Aug', fullLabel: 'August 2026', amount: 58_000_000 },
      { shortLabel: 'Sep', fullLabel: 'September 2026', amount: 50_000_000 },
      { shortLabel: 'Oct', fullLabel: 'October 2026', amount: 52_000_000 },
      { shortLabel: 'Nov', fullLabel: 'November 2026', amount: 56_000_000 },
      { shortLabel: 'Dec', fullLabel: 'December 2026', amount: 62_000_000 },
    ],
  },
  claims: {
    total: 'Rp 215,800,000',
    items: [
      { shortLabel: 'Jan', fullLabel: 'January 2026', amount: 12_000_000 },
      { shortLabel: 'Feb', fullLabel: 'February 2026', amount: 18_000_000 },
      { shortLabel: 'Mar', fullLabel: 'March 2026', amount: 15_000_000 },
      { shortLabel: 'Apr', fullLabel: 'April 2026', amount: 20_000_000 },
      { shortLabel: 'May', fullLabel: 'May 2026', amount: 22_000_000 },
      { shortLabel: 'Jun', fullLabel: 'June 2026', amount: 18_000_000 },
      { shortLabel: 'Jul', fullLabel: 'July 2026', amount: 25_000_000 },
      { shortLabel: 'Aug', fullLabel: 'August 2026', amount: 32_000_000 },
      { shortLabel: 'Sep', fullLabel: 'September 2026', amount: 24_000_000 },
      { shortLabel: 'Oct', fullLabel: 'October 2026', amount: 26_000_000 },
      { shortLabel: 'Nov', fullLabel: 'November 2026', amount: 28_000_000 },
      { shortLabel: 'Dec', fullLabel: 'December 2026', amount: 30_000_000 },
    ],
  },
};

const MONTHLY_DATA: Record<CashFlowTab, { total: string; items: CashFlowBarItem[] }> = {
  net: {
    total: 'Rp 462,000,000',
    items: [
      { shortLabel: 'Week 1', fullLabel: 'Aug Week 1', amount: 64_000_000 },
      { shortLabel: 'Week 2', fullLabel: 'Aug Week 2', amount: 88_000_000 },
      { shortLabel: 'Week 3', fullLabel: 'Aug Week 3', amount: 96_000_000 },
      { shortLabel: 'Week 4', fullLabel: 'Aug Week 4 (Payroll Cut-off)', amount: 214_000_000 },
    ],
  },
  tax: {
    total: 'Rp 58,000,000',
    items: [
      { shortLabel: 'Week 1', fullLabel: 'Aug Week 1', amount: 8_000_000 },
      { shortLabel: 'Week 2', fullLabel: 'Aug Week 2', amount: 12_000_000 },
      { shortLabel: 'Week 3', fullLabel: 'Aug Week 3', amount: 14_000_000 },
      { shortLabel: 'Week 4', fullLabel: 'Aug Week 4 (PPh 21 & BPJS)', amount: 24_000_000 },
    ],
  },
  claims: {
    total: 'Rp 32,000,000',
    items: [
      { shortLabel: 'Week 1', fullLabel: 'Aug Week 1', amount: 5_500_000 },
      { shortLabel: 'Week 2', fullLabel: 'Aug Week 2', amount: 9_000_000 },
      { shortLabel: 'Week 3', fullLabel: 'Aug Week 3', amount: 7_500_000 },
      { shortLabel: 'Week 4', fullLabel: 'Aug Week 4', amount: 10_000_000 },
    ],
  },
};

function formatCompactCurrencyM(amount: number): string {
  const millions = amount / 1_000_000;
  if (Number.isInteger(millions)) {
    return `Rp ${millions.toLocaleString('en-US')}M`;
  }
  return formatRupiahDisplay(amount);
}

export function SingleFocusBarCard({
  title = 'Payroll Cash Flow',
  isLoading = false,
  error = null,
  className,
}: SingleFocusBarCardProps) {
  const [activeTab, setActiveTab] = useState<CashFlowTab>('net');
  const [selectedPeriod, setSelectedPeriod] = useState<CashFlowPeriod>('Yearly');
  const [activeBarIndex, setActiveBarIndex] = useState<number>(7); // August (idx 7) in Yearly
  const [hoveredBarIndex, setHoveredBarIndex] = useState<number | null>(null);
  const [showInfo, setShowInfo] = useState(false);

  const currentSeries = useMemo(() => {
    return selectedPeriod === 'Monthly' ? MONTHLY_DATA[activeTab] : YEARLY_DATA[activeTab];
  }, [activeTab, selectedPeriod]);

  const items = currentSeries.items;
  const safeActiveIndex = Math.min(Math.max(activeBarIndex, 0), Math.max(0, items.length - 1));
  const focusedIndex =
    hoveredBarIndex !== null && hoveredBarIndex >= 0 && hoveredBarIndex < items.length
      ? hoveredBarIndex
      : safeActiveIndex;

  const yScale = useMemo(() => {
    if (items.length === 0) {
      return computeNiceScale(0, 100, { desiredIntervals: 3, startAtZero: true });
    }
    const maxAmount = Math.max(...items.map((item) => item.amount));
    return computeNiceScale(0, maxAmount, {
      desiredIntervals: 3,
      startAtZero: true,
      upperPaddingRatio: 0.12,
    });
  }, [items]);

  const domainMax = Math.max(1, yScale.domainMax);

  const handlePeriodChange = (nextPeriod: CashFlowPeriod) => {
    setSelectedPeriod(nextPeriod);
    setHoveredBarIndex(null);
    setActiveBarIndex(nextPeriod === 'Monthly' ? 3 : 7);
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLButtonElement>, currentIndex: number) => {
    if (e.key === 'ArrowRight') {
      e.preventDefault();
      setActiveBarIndex(Math.min(items.length - 1, currentIndex + 1));
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault();
      setActiveBarIndex(Math.max(0, currentIndex - 1));
    } else if (e.key === 'Home') {
      e.preventDefault();
      setActiveBarIndex(0);
    } else if (e.key === 'End') {
      e.preventDefault();
      setActiveBarIndex(items.length - 1);
    }
  };

  const getTooltipHorizontalClass = (idx: number): string => {
    if (idx === 0 || (items.length > 6 && idx === 1)) {
      return 'left-0 translate-x-0';
    }
    if (idx === items.length - 1 || (items.length > 6 && idx === items.length - 2)) {
      return 'right-0 translate-x-0';
    }
    return 'left-1/2 -translate-x-1/2';
  };

  return (
    <div
      className={cn(
        'relative p-5 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs flex flex-col gap-3 overflow-visible',
        className
      )}
    >
      {/* Top Header */}
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <span className="text-base font-medium text-[#1E232D] tracking-tight">
            {title}
          </span>

          <div className="relative">
            <button
              type="button"
              onClick={() => setShowInfo((prev) => !prev)}
              aria-expanded={showInfo}
              className="h-7 w-7 rounded-lg hover:bg-slate-100 flex items-center justify-center text-slate-400 hover:text-slate-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
              aria-label={`Metric information for ${title}`}
            >
              <Info className="h-4 w-4" />
            </button>

            {showInfo && (
              <div
                className="absolute left-0 top-full mt-1.5 w-64 p-3.5 rounded-xl bg-slate-900 text-white shadow-xl z-30 text-xs space-y-2 animate-in fade-in zoom-in-95 duration-150"
                role="tooltip"
              >
                <div className="border-b border-slate-700 pb-1 font-semibold text-blue-400">
                  {selectedPeriod === 'Yearly' ? 'Annual Cash Flow' : 'Monthly Weekly Cycle'}
                </div>
                <p className="text-slate-300 text-[11px] leading-relaxed">
                  Total disbursement trend across all banking networks and statutory payroll periods.
                </p>
                <div className="text-[10px] text-slate-400 pt-1 border-t border-slate-800">
                  Data Scope: <span className="text-slate-200 font-semibold">Active Legal Entity</span>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Period Selector */}
        <DropdownSelect<CashFlowPeriod>
          value={selectedPeriod}
          onChange={handlePeriodChange}
          panelClassName="min-w-[176px]"
          options={[
            {
              value: 'Yearly',
              label: 'Yearly',
              icon: <Calendar className="h-[17px] w-[17px]" strokeWidth={1.75} />,
            },
            {
              value: 'Monthly',
              label: 'Monthly',
              icon: <CalendarDays className="h-[17px] w-[17px]" strokeWidth={1.75} />,
            },
          ]}
        />
      </div>

      {/* Metric Value */}
      <div>
        <span className="text-3xl sm:text-4xl font-semibold text-[#111827] tracking-tight tabular-nums">
          {currentSeries.total}
        </span>
      </div>

      {/* Category Filter Option Pills */}
      <div
        className="flex flex-wrap items-center gap-2 sm:gap-3 pt-0.5"
        role="group"
        aria-label="Cash flow categories"
      >
        {(
          [
            { id: 'net', label: 'Net Payroll' },
            { id: 'tax', label: 'Statutory & Tax' },
            { id: 'claims', label: 'Claims & Bonus' },
          ] as const
        ).map((tab) => {
          const isActive = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActiveTab(tab.id)}
              aria-pressed={isActive}
              className={cn(
                'rounded-full text-[13px] sm:text-sm transition-all duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-2',
                isActive
                  ? 'px-5 py-1.5 sm:py-2 font-medium text-white border border-[#15171A] bg-[radial-gradient(ellipse_at_50%_45%,#474A50_0%,#24272B_65%,#111316_100%)] shadow-[inset_0_1px_1px_rgba(255,255,255,0.16),inset_0_-2px_5px_rgba(0,0,0,0.65),0_2px_6px_rgba(15,23,42,0.18)]'
                  : 'px-3.5 py-1.5 sm:py-2 font-normal text-slate-600 hover:text-slate-900 hover:bg-slate-100/80'
              )}
            >
              {tab.label}
            </button>
          );
        })}
      </div>

      {/* States: Loading / Error / Empty / Bar Chart */}
      {isLoading ? (
        <div
          className="mt-2 h-52 w-full rounded-xl bg-slate-50 border border-slate-100 p-4 flex items-end justify-between gap-2 animate-pulse"
          aria-busy="true"
          aria-label={`Loading ${title} bar chart`}
        >
          {Array.from({ length: 12 }).map((_, i) => (
            <div
              key={i}
              className="flex-1 rounded-t-md bg-slate-200/80"
              style={{ height: `${35 + ((i * 19) % 55)}%` }}
            />
          ))}
        </div>
      ) : error ? (
        <div
          role="alert"
          className="mt-2 h-52 w-full rounded-xl bg-rose-50/60 border border-rose-200 p-5 flex flex-col items-center justify-center text-center gap-2"
        >
          <AlertCircle className="h-5 w-5 text-rose-600" />
          <p className="text-sm font-medium text-rose-900">Unable to load cash flow series</p>
          <p className="text-xs text-rose-700 max-w-xs">{error}</p>
        </div>
      ) : items.length === 0 ? (
        <div className="mt-2 h-52 w-full rounded-xl bg-slate-50/70 border border-dashed border-slate-200 p-5 flex flex-col items-center justify-center text-center gap-2">
          <BarChart3 className="h-5 w-5 text-slate-400" />
          <p className="text-sm font-medium text-slate-700">No disbursement records available</p>
          <p className="text-xs text-slate-500">Cash flow columns will populate once disbursements are posted.</p>
        </div>
      ) : (
        <div
          className="pt-12 relative"
          onMouseLeave={() => setHoveredBarIndex(null)}
        >
          {/* Plot Row: Synchronized Y-Axis + Bars Grid */}
          <div className="flex gap-2.5 sm:gap-3.5 items-stretch">
            {/* Y-Axis Ticks anchored cleanly by percentage */}
            <div
              className="w-10 sm:w-12 shrink-0 h-40 sm:h-44 relative select-none"
              aria-hidden="true"
            >
              {yScale.ticks.map((tick) => {
                const bottomPct = (tick / domainMax) * 100;
                return (
                  <span
                    key={tick}
                    className="absolute right-0 translate-y-1/2 text-[11px] font-medium text-[#64748B] tabular-nums leading-none"
                    style={{ bottom: `${bottomPct}%` }}
                  >
                    {formatCompactRupiahTick(tick)}
                  </span>
                );
              })}
            </div>

            {/* Chart Area */}
            <div className="flex-1 min-w-0">
              <div className="relative h-40 sm:h-44">
                {/* Synchronized Horizontal Gridlines */}
                <div className="absolute inset-0 pointer-events-none" aria-hidden="true">
                  {yScale.ticks.map((tick, idx) => {
                    const bottomPct = (tick / domainMax) * 100;
                    const isBaseline = idx === 0;
                    return (
                      <div
                        key={tick}
                        className={cn(
                          'absolute inset-x-0 w-full',
                          isBaseline ? 'border-b border-slate-200' : 'border-b border-dashed border-slate-200/70'
                        )}
                        style={{ bottom: `${bottomPct}%` }}
                      />
                    );
                  })}
                </div>

                {/* Bars Container */}
                <div
                  className={cn(
                    'relative z-10 h-full flex items-end justify-between px-0.5 sm:px-1',
                    items.length <= 6 ? 'gap-4 sm:gap-6' : 'gap-1 sm:gap-2.5'
                  )}
                >
                  {items.map((item, idx) => {
                    const isFocused = idx === focusedIndex;
                    const isPinned = idx === safeActiveIndex;
                    const heightPct = item.amount > 0 ? Math.max(3, (item.amount / domainMax) * 100) : 0;

                    return (
                      <div
                        key={item.shortLabel}
                        className="flex-1 h-full flex items-end justify-center relative"
                      >
                        <button
                          type="button"
                          onClick={() => setActiveBarIndex(idx)}
                          onMouseEnter={() => setHoveredBarIndex(idx)}
                          onFocus={() => setHoveredBarIndex(idx)}
                          onBlur={() => setHoveredBarIndex(null)}
                          onKeyDown={(e) => handleKeyDown(e, idx)}
                          aria-pressed={isPinned}
                          aria-label={`${item.fullLabel}: ${formatRupiahDisplay(item.amount)}`}
                          className="w-full h-full flex flex-col items-center justify-end focus-visible:outline-none group"
                        >
                          <div
                            className={cn(
                              'w-full rounded-t-md sm:rounded-t-lg transition-all duration-200 relative group-focus-visible:ring-2 group-focus-visible:ring-blue-600 group-focus-visible:ring-offset-1',
                              items.length <= 6 ? 'max-w-[68px]' : 'max-w-[34px] sm:max-w-[44px]',
                              isFocused
                                ? 'bg-gradient-to-b from-[#1859E9] via-[#2B6FF5] to-[#93C5FD]/65 shadow-xs'
                                : 'bg-gradient-to-b from-[#CBD5E1] to-[#E2E8F0] group-hover:from-[#94A3B8] group-hover:to-[#CBD5E1]'
                            )}
                            style={{ height: `${heightPct}%` }}
                          >
                            {/* Floating Tooltip anchored directly above focused bar */}
                            {isFocused && (
                              <div
                                className={cn(
                                  'absolute -top-12 z-20 whitespace-nowrap px-3 py-1.5 rounded-lg bg-white border border-slate-200 shadow-md text-center animate-in fade-in duration-150 pointer-events-none',
                                  getTooltipHorizontalClass(idx)
                                )}
                              >
                                <div className="text-[11px] text-slate-500 font-medium leading-tight">
                                  {item.fullLabel}:
                                </div>
                                <div className="text-xs sm:text-[13px] font-semibold text-slate-900 tabular-nums leading-tight mt-0.5">
                                  {formatCompactCurrencyM(item.amount)}
                                </div>
                              </div>
                            )}

                            {/* High-contrast Circular Indicator Ring inside dark top cap when bar fits cleanly */}
                            {isFocused && heightPct >= 15 && (
                              <div className="absolute top-2.5 left-1/2 -translate-x-1/2 h-3 w-3 rounded-full border-2 border-white bg-[#1859E9]/30 shadow-2xs" />
                            )}
                          </div>
                        </button>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* X-Axis Labels aligned strictly beneath each column */}
              <div
                className={cn(
                  'flex items-center justify-between pt-2.5 px-0.5 sm:px-1',
                  items.length <= 6 ? 'gap-4 sm:gap-6' : 'gap-1 sm:gap-2.5'
                )}
              >
                {items.map((item, idx) => {
                  const isFocused = idx === focusedIndex;
                  return (
                    <span
                      key={item.shortLabel}
                      onClick={() => setActiveBarIndex(idx)}
                      onMouseEnter={() => setHoveredBarIndex(idx)}
                      className={cn(
                        'flex-1 text-center text-[10px] sm:text-xs tabular-nums transition-colors cursor-pointer select-none',
                        isFocused ? 'text-slate-900 font-bold' : 'text-[#64748B] font-medium hover:text-slate-900'
                      )}
                    >
                      {item.shortLabel}
                    </span>
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
