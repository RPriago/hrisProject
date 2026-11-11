import { Clock, CalendarRange, Calendar } from 'lucide-react';
import type { DropdownSelectOption } from '@/components/ui/DropdownMenu';

export type DashboardPeriodOption = 'This Month' | 'Last 6 Months' | 'Year 2026';

export const DASHBOARD_PERIOD_OPTIONS: readonly DropdownSelectOption<DashboardPeriodOption>[] = [
  {
    value: 'This Month',
    label: 'This Month',
    icon: <Clock className="h-[17px] w-[17px]" strokeWidth={1.75} />,
  },
  {
    value: 'Last 6 Months',
    label: 'Last 6 Months',
    icon: <CalendarRange className="h-[17px] w-[17px]" strokeWidth={1.75} />,
  },
  {
    value: 'Year 2026',
    label: 'Year 2026',
    icon: <Calendar className="h-[17px] w-[17px]" strokeWidth={1.75} />,
  },
];
