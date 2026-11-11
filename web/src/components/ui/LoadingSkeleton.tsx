import React from 'react';
import { cn } from '@/lib/utils';

export interface LoadingSkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  height?: string | number;
  width?: string | number;
  borderRadius?: string;
  ariaLabel?: string;
}

export function LoadingSkeleton({
  height = '24px',
  width = '100%',
  borderRadius = '12px',
  ariaLabel = 'Memuat data...',
  className,
  style,
  ...props
}: LoadingSkeletonProps) {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-label={ariaLabel}
      className={cn(
        'animate-pulse bg-[#E4E6EB] dark:bg-[#20242C] transition-opacity duration-300',
        className
      )}
      style={{
        height,
        width,
        borderRadius,
        ...style,
      }}
      {...props}
    >
      <span className="sr-only">{ariaLabel}</span>
    </div>
  );
}
