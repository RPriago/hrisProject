import { useState } from 'react';
import { useAuth } from '@/context/AuthContext';
import { useTenant } from '@/context/TenantContext';
import { SparklineKpiCard } from '@/components/dashboard/SparklineKpiCard';
import { ProportionalBarCard } from '@/components/dashboard/ProportionalBarCard';
import { SingleFocusBarCard } from '@/components/dashboard/SingleFocusBarCard';
import { CompactActionTableCard } from '@/components/dashboard/CompactActionTableCard';
import { StackedQueueRail } from '@/components/dashboard/StackedQueueRail';
import { CheckCircle2 } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface DashboardPageProps {
  className?: string;
}

export function DashboardPage({ className }: DashboardPageProps) {
  const { user } = useAuth();
  const { activeTenant } = useTenant();
  const [selectedActionMessage, setSelectedActionMessage] = useState<string | null>(null);

  const role = user?.role || 'HrAdmin';
  const isPayrollOfficer = role === 'PayrollOfficer';

  return (
    <div className={cn('space-y-5', className)}>
      {/* Action Toast Feedback if triggered */}
      {selectedActionMessage && (
        <div className="flex items-center justify-between gap-2 p-3 rounded-xl bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs font-semibold animate-in fade-in duration-150">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4 text-emerald-600 shrink-0" />
            <span>{selectedActionMessage}</span>
          </div>
          <button
            type="button"
            onClick={() => setSelectedActionMessage(null)}
            className="text-emerald-700 hover:text-emerald-950 font-bold px-1"
          >
            ×
          </button>
        </div>
      )}

      {/* Row 1: 2 Equal Cards (Overview Area Chart & Cost Breakdown) */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-5 items-stretch">
        <div className="lg:col-span-6 flex flex-col">
          <SparklineKpiCard
            title={isPayrollOfficer ? 'Total Payroll Overview' : 'Payroll Overview'}
            value={isPayrollOfficer ? 'Rp 1,205,850,000' : 'Rp 485,200,000'}
            changePercent={1.5}
            formula="Accumulated net salary, allowances, and approved overtime in the current active cut-off cycle."
            dataScope={activeTenant?.name || 'Active Legal Entity'}
            className="h-full"
          />
        </div>

        <div className="lg:col-span-6 flex flex-col">
          <ProportionalBarCard
            title="Spending Overview"
            value={isPayrollOfficer ? 'Rp 1,205,850,000' : 'Rp 485,200,000'}
            changePercent={-1.5}
            formula="Distribution of base salary, allowances, and statutory withholding deductions."
            dataScope={activeTenant?.name || 'Active Legal Entity'}
            className="h-full"
          />
        </div>
      </div>

      {/* Row 2: Bottom Asymmetric Split (8 Cols Left / 4 Cols Right Tall Rail) */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-5 items-stretch">
        {/* Left Column (8 Cols): Cash Flow Chart + Recent Activity Table */}
        <div className="lg:col-span-8 flex flex-col gap-5">
          <SingleFocusBarCard title="Cash Flow" />

          <CompactActionTableCard title="Recent Transaction" />
        </div>

        {/* Right Column (4 Cols): Tall Upcoming Bill & Payment Rail */}
        <div className="lg:col-span-4 flex flex-col">
          <StackedQueueRail
            title="Upcoming Bill & Payment"
            onAddNew={() => setSelectedActionMessage('New scheduled payroll reminder added.')}
            onManage={() => setSelectedActionMessage('Opening Bills & Payments module (4 active scheduled obligations)...')}
            className="h-full"
          />
        </div>
      </div>
    </div>
  );
}
