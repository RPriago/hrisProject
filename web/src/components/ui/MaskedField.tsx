import { useState } from 'react';
import { cn } from '@/lib/utils';
import { Eye, EyeOff } from 'lucide-react';

export interface MaskedFieldProps {
  value: string;
  maskedValue?: string;
  label?: string;
  canUnmask?: boolean;
  className?: string;
}

export function MaskedField({
  value,
  maskedValue,
  label = 'Data Sensitif',
  canUnmask = false,
  className,
}: MaskedFieldProps) {
  const [isRevealed, setIsRevealed] = useState(false);

  const fallbackMasked = maskedValue || '••••••••' + (value.length > 4 ? value.slice(-4) : '');
  const displayValue = isRevealed ? value : fallbackMasked;

  return (
    <div className={cn('inline-flex items-center gap-2 tabular-nums', className)}>
      <span className="font-mono text-sm tracking-wide text-text-primary">
        {displayValue}
      </span>

      {canUnmask && (
        <button
          type="button"
          onClick={() => setIsRevealed(!isRevealed)}
          className="min-h-[44px] min-w-[44px] p-2 inline-flex items-center justify-center rounded-lg text-text-secondary hover:text-text-primary hover:bg-surface-soft focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-navy-action transition-colors"
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
  );
}
