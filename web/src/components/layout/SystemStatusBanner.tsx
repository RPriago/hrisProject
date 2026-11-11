import { useTenant } from '@/context/TenantContext';
import { AlertTriangle, Clock, ShieldAlert } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface SystemStatusBannerProps {
  className?: string;
}

export function SystemStatusBanner({ className }: SystemStatusBannerProps) {
  const { activeTenant } = useTenant();

  if (!activeTenant) return null;

  const isPastDue = activeTenant.subscriptionStatus === 'PastDue';
  const isSupportActive = activeTenant.isSupportAccess;

  if (!isPastDue && !isSupportActive) return null;

  return (
    <div className={cn('w-full flex flex-col', className)} role="region" aria-label="Pita status sistem">
      {/* Support Access Banner */}
      {isSupportActive && (
        <div className="w-full bg-[#1F2227] text-white border-b border-[#353D43] px-4 py-2.5 flex items-center justify-between text-xs font-medium">
          <div className="flex items-center gap-2">
            <Clock className="h-4 w-4 text-amber-fill animate-pulse shrink-0" />
            <span>
              <strong>Sesi Dukungan Vendor Aktif:</strong> Tiket investigasi teknis berbatas waktu aktif untuk entitas {activeTenant.name}.
            </span>
          </div>
          <div className="flex items-center gap-2 text-amber-fill font-mono font-bold text-[11px] shrink-0">
            <span>Berakhir dalam: 03:45:12</span>
            <span className="text-[10px] text-[#8D95A5] hidden sm:inline">(Audit Trail Locked)</span>
          </div>
        </div>
      )}

      {/* Subscription Past Due Read-Only Banner */}
      {isPastDue && (
        <div className="w-full bg-[#FFEDD5] text-[#9A3412] border-b border-[#FDBA74] px-4 py-2.5 flex items-center justify-between text-xs font-semibold">
          <div className="flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 text-[#C2410C] shrink-0" />
            <span>
              Langganan Perusahaan Lewat Jatuh Tempo: Ruang kerja berjalan dalam mode baca sahaja (Read-Only).
            </span>
          </div>
          <div className="flex items-center gap-2">
            <ShieldAlert className="h-4 w-4 shrink-0" />
            <span className="underline cursor-pointer">Perbarui Tagihan</span>
          </div>
        </div>
      )}
    </div>
  );
}
