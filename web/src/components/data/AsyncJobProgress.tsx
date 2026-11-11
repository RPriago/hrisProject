import { useState, useEffect, useRef } from 'react';
import { apiClient } from '@/lib/api/apiClient';
import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { cn } from '@/lib/utils';
import { Download, AlertCircle, CheckCircle2, Clock } from 'lucide-react';

export interface JobProgressDto {
  jobId: string;
  jobCategory: string;
  status:
    | 'Queued'
    | 'Validating'
    | 'Processing'
    | 'Completed'
    | 'CompletedWithErrors'
    | 'Failed'
    | 'Cancelled'
    | string;
  totalItems: number;
  processedItems: number;
  succeededItems: number;
  failedItems: number;
  progressPercentage: number;
  errorReportFileId?: string | null;
  summaryMessage?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
}

export interface AsyncJobProgressProps {
  jobId: string;
  pollIntervalMs?: number;
  onDownloadErrorReport?: (fileId: string) => void;
  onCompleted?: (job: JobProgressDto) => void;
  className?: string;
}

const TERMINAL_STATUSES = ['Completed', 'CompletedWithErrors', 'Failed', 'Cancelled'];

export function AsyncJobProgress({
  jobId,
  pollIntervalMs = 1000,
  onDownloadErrorReport,
  onCompleted,
  className,
}: AsyncJobProgressProps) {
  const [job, setJob] = useState<JobProgressDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const onCompletedRef = useRef(onCompleted);
  onCompletedRef.current = onCompleted;

  useEffect(() => {
    let isMounted = true;
    let timerId: ReturnType<typeof setTimeout> | null = null;

    async function poll() {
      try {
        const data = await apiClient.get<JobProgressDto>(`/api/v1/jobs/${jobId}`);
        if (!isMounted) return;

        setJob(data);
        setError(null);

        if (TERMINAL_STATUSES.includes(data.status)) {
          if (data.status === 'Completed' || data.status === 'CompletedWithErrors') {
            onCompletedRef.current?.(data);
          }
          return; // Stop polling
        }

        // Schedule next poll
        timerId = setTimeout(poll, pollIntervalMs);
      } catch (err: unknown) {
        if (!isMounted) return;
        const msg = err instanceof Error ? err.message : 'Gagal memuat status tugas latar belakang';
        setError(msg);
        // Retry polling even on error unless unmounted
        timerId = setTimeout(poll, pollIntervalMs * 2);
      }
    }

    poll();

    return () => {
      isMounted = false;
      if (timerId) clearTimeout(timerId);
    };
  }, [jobId, pollIntervalMs]);

  if (error && !job) {
    return (
      <div className={cn('p-4 rounded-xl border border-red-200 bg-red-50 text-xs text-red-700', className)}>
        <p className="font-semibold">Terjadi Gangguan Pemantauan</p>
        <p className="mt-1">{error}</p>
      </div>
    );
  }

  if (!job) {
    return (
      <div className={cn('p-4 rounded-xl border border-[#ECEEF2] bg-white shadow-card-soft', className)}>
        <div className="flex items-center gap-2 text-xs text-text-secondary animate-pulse">
          <Clock className="h-4 w-4 text-amber-text animate-spin" />
          <span>Menghubungi layanan tugas latar belakang...</span>
        </div>
      </div>
    );
  }

  const getStatusLabel = (status: string) => {
    switch (status) {
      case 'Queued':
        return 'Menunggu Antrean';
      case 'Validating':
        return 'Memvalidasi Data';
      case 'Processing':
        return 'Memproses';
      case 'Completed':
        return 'Selesai';
      case 'CompletedWithErrors':
        return 'Selesai dengan Catatan';
      case 'Failed':
        return 'Gagal';
      case 'Cancelled':
        return 'Dibatalkan';
      default:
        return status;
    }
  };

  const getStatusBadgeVariant = (status: string): 'success' | 'warning' | 'danger' | 'neutral' | 'info' | 'locked' => {
    switch (status) {
      case 'Completed':
        return 'success';
      case 'CompletedWithErrors':
        return 'warning';
      case 'Failed':
      case 'Cancelled':
        return 'danger';
      case 'Processing':
      case 'Validating':
        return 'info';
      default:
        return 'neutral';
    }
  };

  const percentage = Math.min(100, Math.max(0, job.progressPercentage ?? 0));
  const hasErrors = job.failedItems > 0 || job.status === 'CompletedWithErrors' || Boolean(job.errorReportFileId);

  return (
    <div
      className={cn(
        'p-5 rounded-2xl border border-[#ECEEF2] bg-white shadow-card-soft flex flex-col space-y-4',
        className
      )}
    >
      {/* Header with category and status */}
      <div className="flex items-center justify-between gap-3">
        <div>
          <span className="text-xs uppercase tracking-wider font-semibold text-text-secondary">
            Tugas Latar Belakang
          </span>
          <h4 className="text-sm font-bold text-text-primary mt-0.5">{job.jobCategory}</h4>
        </div>
        <Badge variant={getStatusBadgeVariant(job.status)}>
          {getStatusLabel(job.status)}
        </Badge>
      </div>

      {/* Progress Bar */}
      <div className="space-y-1.5">
        <div className="flex justify-between items-center text-xs font-semibold">
          <span className="text-text-secondary">Progres Pengerjaan</span>
          <span className="tabular-nums text-text-primary">{percentage}%</span>
        </div>
        <div
          role="progressbar"
          aria-valuenow={percentage}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label={`Progres ${job.jobCategory}`}
          className="w-full h-2.5 rounded-full bg-[#ECEEF2] overflow-hidden"
        >
          <div
            className={cn(
              'h-full transition-all duration-300 rounded-full',
              job.status === 'Completed'
                ? 'bg-[#107569]'
                : job.status === 'CompletedWithErrors'
                ? 'bg-[#E58411]'
                : job.status === 'Failed'
                ? 'bg-[#BC1A1A]'
                : 'bg-navy-action'
            )}
            style={{ width: `${percentage}%` }}
          />
        </div>
      </div>

      {/* Metric Counters */}
      <div className="grid grid-cols-3 gap-2 py-2 px-3 rounded-xl bg-[#F6F7F9] text-xs">
        <div>
          <span className="text-text-secondary block">Total Baris</span>
          <span className="font-bold tabular-nums text-text-primary">{job.totalItems}</span>
        </div>
        <div>
          <span className="text-text-secondary block">Berhasil</span>
          <span className="font-bold tabular-nums text-[#107569]">
            {job.succeededItems} berhasil
          </span>
        </div>
        <div>
          <span className="text-text-secondary block">Gagal</span>
          <span
            className={cn(
              'font-bold tabular-nums',
              job.failedItems > 0 ? 'text-[#BC1A1A]' : 'text-text-secondary'
            )}
          >
            {job.failedItems} gagal
          </span>
        </div>
      </div>

      {/* Summary Message or Error details */}
      {job.summaryMessage && (
        <p className="text-xs text-text-secondary italic">{job.summaryMessage}</p>
      )}

      {/* Download Error Report Button if errors occurred */}
      {hasErrors && (
        <div className="pt-2 border-t border-[#ECEEF2] flex items-center justify-between gap-3">
          <div className="flex items-center gap-1.5 text-xs text-[#BC1A1A]">
            <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
            <span>Terdapat data yang tidak dapat diproses.</span>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onDownloadErrorReport?.(job.errorReportFileId || job.jobId)}
            className="text-xs shrink-0"
          >
            <Download className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
            Unduh Laporan Kesalahan
          </Button>
        </div>
      )}

      {/* Success Notification Banner */}
      {job.status === 'Completed' && (
        <div className="flex items-center gap-2 text-xs text-[#107569] font-medium pt-1">
          <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span>Seluruh {job.totalItems} baris data berhasil diproses tanpa kesalahan.</span>
        </div>
      )}
    </div>
  );
}
