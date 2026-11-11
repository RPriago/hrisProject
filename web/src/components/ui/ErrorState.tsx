import { useState } from 'react';
import { cn } from '@/lib/utils';
import { Button } from './Button';
import { type ProblemDetails } from '@/lib/api/problemDetails';
import { AlertCircle, RefreshCw, Copy, Check } from 'lucide-react';

export interface ErrorStateProps {
  problem?: ProblemDetails | null;
  title?: string;
  detail?: string;
  onRetry?: () => void;
  className?: string;
}

export function ErrorState({
  problem,
  title,
  detail,
  onRetry,
  className,
}: ErrorStateProps) {
  const [copied, setCopied] = useState(false);

  const displayTitle = title || problem?.title || 'Terjadi Kendala Teknis';
  const displayDetail =
    detail || problem?.detail || 'Permintaan Anda belum dapat diselesaikan saat ini. Silakan coba kembali.';
  const correlationId = problem?.correlationId;

  const handleCopy = async () => {
    if (!correlationId) return;
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard) {
        await navigator.clipboard.writeText(correlationId);
      }
      setCopied(true);
      setTimeout(() => setCopied(false), 2500);
    } catch {
      // Fallback
    }
  };

  return (
    <div
      role="alert"
      className={cn(
        'flex flex-col items-center justify-center p-8 text-center rounded-2xl border border-[#FCA5A5] bg-[#FEF2F2]/60',
        className
      )}
    >
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-[#FEE2E2] mb-4">
        <AlertCircle className="h-8 w-8 text-[#991B1B]" aria-hidden="true" />
      </div>

      <h3 className="text-base font-bold text-[#991B1B] mb-1">{displayTitle}</h3>
      <p className="max-w-md text-sm text-[#7F1D1D] mb-4">{displayDetail}</p>

      {correlationId && (
        <div className="flex items-center gap-2 mb-6 p-2 px-3 rounded-lg bg-white/80 border border-[#FCA5A5] text-xs font-mono text-[#7F1D1D]">
          <span>Kode Laporan: {correlationId}</span>
          <button
            type="button"
            onClick={handleCopy}
            className="p-1 rounded hover:bg-[#FEE2E2] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#991B1B] inline-flex items-center gap-1 text-xs font-sans font-semibold text-[#991B1B] min-h-[36px] min-w-[36px] justify-center"
            aria-label="Salin kode laporan"
          >
            {copied ? (
              <>
                <Check className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
                <span className="text-emerald-700">Kode laporan disalin</span>
              </>
            ) : (
              <>
                <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Salin</span>
              </>
            )}
          </button>
        </div>
      )}

      {onRetry && (
        <Button variant="primary" onClick={onRetry} className="gap-2">
          <RefreshCw className="h-4 w-4" aria-hidden="true" />
          <span>Coba Muat Ulang</span>
        </Button>
      )}
    </div>
  );
}
