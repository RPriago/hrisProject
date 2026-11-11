import { useState } from 'react';
import { SlidersHorizontal, MoreHorizontal, ListFilter, CheckCircle2, Clock } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import { DropdownSelect } from '@/components/ui/DropdownMenu';

export interface ActionTableRow {
  id: string;
  name?: string;
  primaryText?: string;
  logo?: string;
  logoBg?: string;
  date?: string;
  secondaryText?: string;
  amount?: string;
  status?: 'Success' | 'Pending' | 'Review' | string;
  badgeLabel?: string;
  badgeVariant?: 'success' | 'warning' | 'danger' | 'neutral' | 'info';
  actionLabel?: string;
  onAction?: () => void;
}

export type RecentActivityItem = ActionTableRow;

export interface CompactActionTableCardProps {
  title?: string;
  rows?: ActionTableRow[];
  className?: string;
}

function resolveBadgeVariant(item: ActionTableRow): BadgeVariant {
  if (item.badgeVariant) {
    return item.badgeVariant;
  }
  const normalized = (item.status || item.badgeLabel || 'Success').toLowerCase();
  if (['success', 'completed', 'paid', 'approved', 'tersanitasi'].includes(normalized)) {
    return 'success';
  }
  if (['pending', 'review', 'processing', 'warning', 'delayed'].includes(normalized)) {
    return 'warning';
  }
  if (['failed', 'error', 'rejected', 'declined', 'danger'].includes(normalized)) {
    return 'danger';
  }
  if (['info'].includes(normalized)) {
    return 'info';
  }
  return 'neutral';
}

export function CompactActionTableCard({
  title = 'Recent Transaction',
  rows,
  className,
}: CompactActionTableCardProps) {
  const [filter, setFilter] = useState<'All' | 'Success' | 'Pending'>('All');
  const [activeActionId, setActiveActionId] = useState<string | null>(null);

  const defaultActivities: ActionTableRow[] = [
    {
      id: 'tx-1',
      name: 'BCA Payroll Salary Batch',
      logo: '/logos/bca.svg',
      logoBg: 'bg-blue-50 border-blue-100',
      date: 'Wed 10:29 AM',
      amount: '+Rp 388,966,000',
      status: 'Success',
    },
    {
      id: 'tx-2',
      name: 'BPJS Health & Labor Batch',
      logo: '/logos/bpjs-tk.svg',
      logoBg: 'bg-emerald-50 border-emerald-100',
      date: 'Wed 10:29 AM',
      amount: '+Rp 96,234,000',
      status: 'Success',
    },
    {
      id: 'tx-3',
      name: 'PPh 21 TER Tax Remittance',
      logo: '/logos/pajak.svg',
      logoBg: 'bg-amber-50 border-amber-100',
      date: 'Tue 04:15 PM',
      amount: '+Rp 48,234,000',
      status: 'Success',
    },
    {
      id: 'tx-4',
      name: 'Overtime & Bonus Payout',
      logo: '/logos/bca.svg',
      logoBg: 'bg-blue-50 border-blue-100',
      date: 'Mon 09:30 AM',
      amount: '+Rp 32,850,000',
      status: 'Success',
    },
  ];

  const sourceItems = rows && rows.length > 0 ? rows : defaultActivities;
  const filteredItems = filter === 'All' 
    ? sourceItems 
    : sourceItems.filter(a => (a.status || a.badgeLabel) === filter);

  return (
    <div
      className={cn(
        'relative p-5 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs flex flex-col gap-3 overflow-visible',
        className
      )}
    >
      {/* Header */}
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-base font-medium text-[#1E232D] tracking-tight">
          {title}
        </h3>

        {/* Filter Button */}
        <DropdownSelect<'All' | 'Success' | 'Pending'>
          value={filter}
          onChange={setFilter}
          buttonLabel="Filter"
          trailingIcon={<SlidersHorizontal className="h-4 w-4 text-slate-500" aria-hidden="true" />}
          ariaLabel="Filter transactions"
          panelClassName="min-w-[184px]"
          options={[
            {
              value: 'All',
              label: 'All',
              icon: <ListFilter className="h-[17px] w-[17px]" strokeWidth={1.75} />,
            },
            {
              value: 'Success',
              label: 'Success',
              icon: <CheckCircle2 className="h-[17px] w-[17px]" strokeWidth={1.75} />,
              dividerBefore: true,
            },
            {
              value: 'Pending',
              label: 'Pending',
              icon: <Clock className="h-[17px] w-[17px]" strokeWidth={1.75} />,
            },
          ]}
        />
      </div>

      {/* Table Section */}
      <div className="overflow-x-auto">
        <div className="min-w-[540px] space-y-1">
          {/* Table Header Row (Architectural rounded-xl, transparent + thin gray border) */}
          <div className="rounded-xl bg-transparent border border-[#E2E8F0] px-3.5 py-2 text-xs sm:text-[13px] font-medium text-slate-500 grid grid-cols-[minmax(0,1.6fr)_92px_136px_minmax(136px,auto)] gap-2.5 sm:gap-3 items-center">
            <span>Activity</span>
            <span>Date</span>
            <span>Total Amount</span>
            <span className="text-right pr-1">Status</span>
          </div>

          {/* Table Rows */}
          <div className="divide-y divide-slate-100">
            {filteredItems.map((item) => (
              <div
                key={item.id}
                className="px-3.5 py-2.5 rounded-xl hover:bg-slate-50/80 transition-colors grid grid-cols-[minmax(0,1.6fr)_92px_136px_minmax(136px,auto)] gap-2.5 sm:gap-3 items-center"
              >
                {/* Activity Name with Logo Box */}
                <div className="flex items-center gap-3 min-w-0">
                  <div
                    className={cn(
                      'h-9 w-9 rounded-xl border p-1.5 flex items-center justify-center shrink-0 shadow-2xs',
                      item.logoBg || 'bg-transparent border-[#E2E8F0]'
                    )}
                  >
                    {item.logo ? (
                      <img src={item.logo} alt="" className="max-h-5 max-w-full object-contain" />
                    ) : (
                      <span className="text-xs font-semibold text-slate-500">
                        {(item.primaryText || item.name || 'TX').slice(0, 2).toUpperCase()}
                      </span>
                    )}
                  </div>
                  <span className="text-sm font-semibold text-slate-900 truncate">
                    {item.primaryText || item.name}
                  </span>
                </div>

                {/* Date */}
                <div className="text-xs sm:text-[13px] font-normal text-slate-500 tabular-nums whitespace-nowrap">
                  {item.secondaryText || item.date || ''}
                </div>

                {/* Total Amount */}
                <div className="text-sm font-semibold text-slate-900 tabular-nums whitespace-nowrap">
                  {item.amount || 'Rp 0'}
                </div>

                {/* Status Pill & Menu */}
                <div className="flex items-center justify-end gap-2">
                  <Badge variant={resolveBadgeVariant(item)}>
                    {item.badgeLabel || item.status || 'Success'}
                  </Badge>

                  {item.actionLabel ? (
                    <button
                      type="button"
                      onClick={item.onAction}
                      className="px-2 py-1 rounded-md text-xs font-medium text-blue-600 hover:bg-blue-50 transition-colors"
                    >
                      {item.actionLabel}
                    </button>
                  ) : (
                    <button
                      type="button"
                      onClick={() => setActiveActionId(activeActionId === item.id ? null : item.id)}
                      className="h-6 w-6 rounded-md hover:bg-slate-100 text-slate-400 hover:text-slate-700 flex items-center justify-center transition-colors shrink-0"
                      aria-label="More options"
                    >
                      <MoreHorizontal className="h-3.5 w-3.5" />
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
