import { useState, useEffect, useRef } from 'react';
import { cn } from '@/lib/utils';
import { registerMemoryWipeListener } from '@/lib/queryClient';
import { Eye, EyeOff } from 'lucide-react';

export interface MaskedFieldProps {
  value?: string;
  maskedValue?: string;
  onReveal?: () => Promise<string>;
  label?: string;
  canUnmask?: boolean;
  className?: string;
}

export function MaskedField({
  value,
  maskedValue,
  onReveal,
  label = 'Data Sensitif',
  canUnmask = false,
  className,
}: MaskedFieldProps) {
  const [isRevealed, setIsRevealed] = useState(false);
  const [revealedValue, setRevealedValue] = useState<string | null>(null);
  const [isRevealing, setIsRevealing] = useState(false);
  const [revealError, setRevealError] = useState<string | null>(null);
  const isMountedRef = useRef(true);

  useEffect(() => {
    isMountedRef.current = true;
    const unregister = registerMemoryWipeListener(() => {
      setIsRevealed(false);
      setRevealedValue(null);
      setRevealError(null);
    });
    return () => {
      isMountedRef.current = false;
      setIsRevealed(false);
      setRevealedValue(null);
      unregister();
    };
  }, []);

  const fallbackMasked =
    maskedValue || '••••••••' + (value && value.length > 4 ? value.slice(-4) : '');
  const displayValue = isRevealed ? (revealedValue ?? value ?? fallbackMasked) : fallbackMasked;

  const handleToggleReveal = async () => {
    if (isRevealed) {
      setIsRevealed(false);
      setRevealedValue(null);
      setRevealError(null);
      return;
    }

    if (onReveal) {
      setIsRevealing(true);
      setRevealError(null);
      try {
        const fetched = await onReveal();
        if (!isMountedRef.current) return;
        setRevealedValue(fetched);
        setIsRevealed(true);
      } catch {
        if (!isMountedRef.current) return;
        setRevealError(`Gagal membuka ${label}`);
      } finally {
        if (isMountedRef.current) {
          setIsRevealing(false);
        }
      }
      return;
    }

    if (value !== undefined) {
      setRevealedValue(value);
      setIsRevealed(true);
    }
  };

  return (
    <div className={cn('inline-flex flex-col gap-1', className)}>
      <div className="inline-flex items-center gap-2 tabular-nums">
        <span className="font-mono text-sm tracking-wide text-text-primary">
          {displayValue}
        </span>

        {canUnmask && (
          <button
            type="button"
            disabled={isRevealing}
            onClick={handleToggleReveal}
            className="min-h-[44px] min-w-[44px] p-2 inline-flex items-center justify-center rounded-lg text-text-secondary hover:text-text-primary hover:bg-surface-soft focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-navy-action transition-colors disabled:opacity-50"
            aria-label={isRevealed ? `Sembunyikan ${label}` : `Lihat ${label}`}
            title={isRevealed ? `Sembunyikan ${label}` : `Lihat ${label}`}
          >
            {isRevealed ? (
              <EyeOff className="h-4 w-4" aria-hidden="true" />
            ) : (
              <Eye className="h-4 w-4" aria-hidden="true" />
            )}
          </button>
        )}
      </div>
      {revealError && (
        <span role="alert" className="text-xs text-red-600">
          {revealError}
        </span>
      )}
    </div>
  );
}
