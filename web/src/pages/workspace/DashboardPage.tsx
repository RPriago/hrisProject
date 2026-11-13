import { useState, useEffect } from 'react';
import { useAuth } from '@/context/AuthContext';
import { useTenant } from '@/context/TenantContext';
import { registerMemoryWipeListener } from '@/lib/queryClient';
import { SparklineKpiCard } from '@/components/dashboard/SparklineKpiCard';
import { ProportionalBarCard } from '@/components/dashboard/ProportionalBarCard';
import { SingleFocusBarCard } from '@/components/dashboard/SingleFocusBarCard';
import { CompactActionTableCard } from '@/components/dashboard/CompactActionTableCard';
import { StackedQueueRail } from '@/components/dashboard/StackedQueueRail';
import { CheckCircle2, ShieldAlert, Clock, FileText } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface DashboardPageProps {
  className?: string;
}

export function DashboardPage({ className }: DashboardPageProps) {
  const { user } = useAuth();
  const { activeTenant, isSupportAccess } = useTenant();
  const [selectedActionMessage, setSelectedActionMessage] = useState<string | null>(null);

  useEffect(() => {
    return registerMemoryWipeListener(() => {
      setSelectedActionMessage(null);
    });
  }, []);

  if (!user) {
    return (
      <div className={cn('p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs', className)}>
        <h2 className="text-base font-semibold text-slate-900">Sesi Tidak Aktif</h2>
        <p className="mt-1 text-sm text-slate-600">
          Silakan masuk terlebih dahulu untuk mengakses ruang kerja tenant.
        </p>
      </div>
    );
  }

  const role = user.role;
  const isPayrollOfficer = role === 'PayrollOfficer';

  if (role === 'PlatformAdmin' && !isSupportAccess) {
    return (
      <div className={cn('space-y-5', className)} data-testid="platform-ops-overview">
        <div className="p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs space-y-2">
          <div className="inline-flex items-center gap-2 text-xs font-semibold text-slate-700">
            <ShieldAlert className="h-4 w-4 text-amber-text" />
            <span>Platform Operations Overview</span>
          </div>
          <h2 className="text-lg font-semibold text-slate-900">
            Vendor Tenant Isolation Active
          </h2>
          <p className="text-sm text-slate-600">
            Data finansial dan payroll tenant disembunyikan hingga Support Access Grant resmi diaktifkan untuk sesi investigasi teknis.
          </p>
        </div>
      </div>
    );
  }

  if (role === 'Employee' || user.dataScope === 'Self') {
    return (
      <div className={cn('grid grid-cols-1 md:grid-cols-2 gap-5', className)} data-testid="employee-self-service">
        <div className="p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs space-y-2">
          <div className="inline-flex items-center gap-2 text-xs font-semibold text-slate-600">
            <Clock className="h-4 w-4 text-blue-600" />
            <span>My Workspace (Self)</span>
          </div>
          <h2 className="text-lg font-semibold text-slate-900">Ringkasan Kehadiran Saya</h2>
          <p className="text-sm text-slate-600">
            Menampilkan catatan absensi, saldo cuti, dan pengajuan lembur pribadi Anda pada entitas aktif.
          </p>
        </div>
        <div className="p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs space-y-2">
          <div className="inline-flex items-center gap-2 text-xs font-semibold text-slate-600">
            <FileText className="h-4 w-4 text-emerald-600" />
            <span>Slip Gaji Digital (Self)</span>
          </div>
          <h2 className="text-lg font-semibold text-slate-900">Slip Gaji & Pajak Pribadi</h2>
          <p className="text-sm text-slate-600">
            Akses slip gaji dan bukti potong PPh 21 pribadi secara terenkripsi.
          </p>
        </div>
      </div>
    );
  }

  if (role === 'Manager' || user.dataScope === 'DirectReports' || user.dataScope === 'Team') {
    return (
      <div className={cn('space-y-5', className)} data-testid="manager-team-scope">
        <div className="p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs space-y-2">
          <h2 className="text-lg font-semibold text-slate-900">
            Persetujuan Tim & Lembur ({user.dataScope})
          </h2>
          <p className="text-sm text-slate-600">
            Ringkasan kehadiran, cuti, dan persetujuan lembur khusus bawahan langsung pada ruang lingkup tim Anda.
          </p>
        </div>
      </div>
    );
  }

  const canMutateSchedules = role !== 'Auditor' && activeTenant?.subscriptionStatus !== 'PastDue';

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
            onAddNew={
              canMutateSchedules
                ? () => setSelectedActionMessage('New scheduled payroll reminder added.')
                : undefined
            }
            onManage={() => setSelectedActionMessage('Opening Bills & Payments module (4 active scheduled obligations)...')}
            className="h-full"
          />
        </div>
      </div>
    </div>
  );
}
