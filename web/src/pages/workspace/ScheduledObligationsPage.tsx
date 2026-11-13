import { useState, useEffect } from 'react';
import { useAuth } from '@/context/AuthContext';
import { useTenant } from '@/context/TenantContext';
import { registerMemoryWipeListener } from '@/lib/queryClient';
import { StackedQueueRail, DEFAULT_SCHEDULED_ITEMS } from '@/components/dashboard/StackedQueueRail';
import { Badge } from '@/components/ui/Badge';
import { ShieldAlert, CheckCircle2 } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface ScheduledObligationsPageProps {
  className?: string;
}

const ALLOWED_OBLIGATION_ROLES = new Set([
  'PayrollOfficer',
  'HrAdmin',
  'CompanyOwner',
  'Auditor',
]);

export function ScheduledObligationsPage({ className }: ScheduledObligationsPageProps) {
  const { user } = useAuth();
  const { activeTenant, isSupportAccess } = useTenant();
  const [feedback, setFeedback] = useState<string | null>(null);

  useEffect(() => {
    return registerMemoryWipeListener(() => {
      setFeedback(null);
    });
  }, []);

  if (!user) {
    return (
      <div className={cn('p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs', className)}>
        <h2 className="text-base font-semibold text-slate-900">Sesi Tidak Aktif</h2>
        <p className="mt-1 text-sm text-slate-600">
          Silakan masuk untuk melihat kewajiban jadwal payroll & pajak.
        </p>
      </div>
    );
  }

  const isAuthorized =
    ALLOWED_OBLIGATION_ROLES.has(user.role) ||
    (user.role === 'PlatformAdmin' && isSupportAccess);

  if (!isAuthorized) {
    return (
      <div className={cn('p-6 rounded-2xl bg-white border border-[#ECEEF2] shadow-xs space-y-2', className)}>
        <div className="inline-flex items-center gap-2 text-xs font-semibold text-amber-text">
          <ShieldAlert className="h-4 w-4" />
          <span>Akses Terbatas ({user.dataScope})</span>
        </div>
        <h2 className="text-lg font-semibold text-slate-900">Kewajiban Jadwal Terproteksi</h2>
        <p className="text-sm text-slate-600">
          Halaman kewajiban jadwal payroll, BPJS, dan pajak hanya dapat diakses oleh otoritas keuangan/HR entitas yang berwenang.
        </p>
      </div>
    );
  }

  const isReadOnly =
    user.role === 'Auditor' || activeTenant?.subscriptionStatus === 'PastDue';

  return (
    <div className={cn('space-y-5', className)}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-slate-900">
            Kewajiban Pembayaran Terjadwal
          </h2>
          <p className="text-sm text-slate-500">
            {activeTenant?.legalEntityName || activeTenant?.name || 'Entitas Aktif'}
          </p>
        </div>
        <Badge variant={isReadOnly ? 'warning' : 'info'}>
          {isReadOnly ? 'Mode Baca Saja' : 'Akses Terotorisasi'}
        </Badge>
      </div>

      {feedback && (
        <div className="flex items-center justify-between gap-2 p-3 rounded-xl bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs font-semibold">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4 text-emerald-600 shrink-0" />
            <span>{feedback}</span>
          </div>
          <button
            type="button"
            onClick={() => setFeedback(null)}
            className="text-emerald-700 hover:text-emerald-950 font-bold px-1"
          >
            ×
          </button>
        </div>
      )}

      <StackedQueueRail
        title="Daftar Kewajiban BPJS, Pajak & Payroll"
        items={DEFAULT_SCHEDULED_ITEMS}
        onAddNew={
          isReadOnly
            ? undefined
            : () => setFeedback('Pengingat kewajiban baru siap dikonfigurasi.')
        }
      />
    </div>
  );
}
