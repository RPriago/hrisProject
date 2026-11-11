import { useState } from 'react';
import { Plus, ChevronRight, ArrowRight } from 'lucide-react';
import { Badge } from '@/components/ui/Badge';
import { cn } from '@/lib/utils';

export interface ScheduleItem {
  id: string;
  title: string;
  date: string;
  amount: string;
  logo: string;
  logoBg: string;
}

export interface StackedQueueRailProps {
  title?: string;
  items?: ScheduleItem[];
  onAddNew?: () => void;
  onManage?: () => void;
  onViewAll?: () => void;
  className?: string;
}

export const DEFAULT_SCHEDULED_ITEMS: ScheduleItem[] = [
  {
    id: 'sc-1',
    title: 'BCA Auto-Disbursement',
    date: 'June 28, 2026',
    amount: 'Rp 388,966,000',
    logo: '/logos/bca.svg',
    logoBg: 'bg-blue-50 border-blue-100',
  },
  {
    id: 'sc-2',
    title: 'BPJS Kesehatan Premium',
    date: 'June 30, 2026',
    amount: 'Rp 48,234,000',
    logo: '/logos/bpjs-kes.svg',
    logoBg: 'bg-emerald-50 border-emerald-100',
  },
  {
    id: 'sc-3',
    title: 'PPh 21 TER Tax Remittance',
    date: 'July 4, 2026',
    amount: 'Rp 48,000,000',
    logo: '/logos/pajak.svg',
    logoBg: 'bg-amber-50 border-amber-100',
  },
  {
    id: 'sc-4',
    title: 'BPJS Ketenagakerjaan (JHT)',
    date: 'July 10, 2026',
    amount: 'Rp 62,450,000',
    logo: '/logos/bpjs-tk.svg',
    logoBg: 'bg-emerald-50 border-emerald-100',
  },
];

export function StackedQueueRail({
  title = 'Upcoming Bill & Payment',
  items = DEFAULT_SCHEDULED_ITEMS,
  onAddNew,
  onManage,
  onViewAll,
  className,
}: StackedQueueRailProps) {
  const [selectedItem, setSelectedItem] = useState<string | null>(null);

  const totalScheduledLabel = (() => {
    const sum = items.reduce((acc, item) => {
      const numeric = Number(item.amount.replace(/[^0-9]/g, ''));
      return acc + (Number.isFinite(numeric) ? numeric : 0);
    }, 0);
    return sum > 0 ? `Rp ${sum.toLocaleString('en-US')}` : 'Rp 0';
  })();

  const handleAction = () => {
    if (onManage) {
      onManage();
    } else if (onViewAll) {
      onViewAll();
    }
  };

  return (
    <div
      className={cn(
        'relative p-5 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs flex flex-col gap-3.5 h-full',
        className
      )}
    >
      {/* Header */}
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-base font-medium text-[#1E232D] tracking-tight">
          {title}
        </h3>

        <button
          type="button"
          onClick={onAddNew}
          className="h-9 w-9 rounded-lg border border-[#E2E8F0] bg-transparent hover:border-slate-300 hover:bg-slate-50/60 text-slate-700 flex items-center justify-center transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
          aria-label="Add new scheduled payment"
        >
          <Plus className="h-4 w-4" />
        </button>
      </div>

      {/* 4 Stacked Sub-Cards - tightly grouped with consistent gap, transparent bg & thin border */}
      <div className="space-y-3.5">
        {items.map((item) => (
          <div
            key={item.id}
            onClick={() => setSelectedItem(item.id)}
            className={cn(
              'p-4 rounded-xl border border-[#E2E8F0] bg-transparent hover:border-slate-300 hover:shadow-xs transition-all space-y-3 cursor-pointer group',
              selectedItem === item.id && 'border-blue-400 bg-blue-50/20'
            )}
          >
            {/* Top Row: Logo, Title, Date, Chevron */}
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-3 min-w-0">
                <div
                  className={cn(
                    'h-11 w-11 rounded-xl border p-2 flex items-center justify-center shrink-0 shadow-2xs',
                    item.logoBg
                  )}
                >
                  <img src={item.logo} alt="" className="max-h-7 max-w-full object-contain" />
                </div>
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-slate-900 truncate group-hover:text-blue-600 transition-colors">
                    {item.title}
                  </h4>
                  <p className="text-xs text-slate-500 font-normal">
                    {item.date}
                  </p>
                </div>
              </div>

              <ChevronRight className="h-4 w-4 text-slate-300 group-hover:text-slate-600 transition-colors shrink-0" />
            </div>

            {/* Bottom Row: Amount + Plump Full-Rounded Scheduled Badge */}
            <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-1.5 pt-1.5 border-t border-slate-100/80">
              <span className="text-[15px] sm:text-base font-semibold text-slate-900 tabular-nums">
                {item.amount}
              </span>
              <Badge variant="outline">
                Scheduled
              </Badge>
            </div>
          </div>
        ))}
      </div>

      {/* Footer Summary & Action Button: Docked cleanly at bottom of container */}
      <div className="mt-auto pt-3.5 border-t border-slate-100 flex flex-col gap-3">
        <div className="flex items-center justify-between text-xs sm:text-[13px] px-0.5">
          <span className="text-slate-500 font-medium">
            Total Scheduled ({items.length})
          </span>
          <span className="font-semibold text-slate-900 tabular-nums">
            {totalScheduledLabel}
          </span>
        </div>

        <button
          type="button"
          onClick={handleAction}
          className="w-full py-2.5 sm:py-3 px-4 rounded-xl border border-blue-600 bg-white hover:bg-blue-50/70 text-slate-900 font-semibold text-sm transition-all active:scale-[0.99] flex items-center justify-center gap-2 shadow-2xs group"
          aria-label="Manage Bills & Payments"
        >
          <span>Manage Bills & Payments</span>
          <ArrowRight className="h-4 w-4 text-blue-600 group-hover:translate-x-0.5 transition-transform" />
        </button>
      </div>
    </div>
  );
}
