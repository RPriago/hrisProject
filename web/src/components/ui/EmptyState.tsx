import React from 'react';
import { cn } from '@/lib/utils';
import { Button } from './Button';
import { Inbox, SearchX, PlusCircle } from 'lucide-react';

export interface EmptyStateProps {
  variant?: 'tenant-empty' | 'filter-empty';
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
  className?: string;
  icon?: React.ReactNode;
}

export function EmptyState({
  variant = 'tenant-empty',
  title,
  description,
  actionLabel,
  onAction,
  className,
  icon,
}: EmptyStateProps) {
  const defaultIcon =
    variant === 'tenant-empty' ? (
      <Inbox className="h-10 w-10 text-amber-text" aria-hidden="true" />
    ) : (
      <SearchX className="h-10 w-10 text-text-secondary" aria-hidden="true" />
    );

  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center p-8 text-center rounded-2xl border border-dashed border-[#ECEEF2] bg-white/60',
        className
      )}
    >
      <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-surface-soft mb-4">
        {icon || defaultIcon}
      </div>

      <h3 className="text-base font-bold text-text-primary mb-1">{title}</h3>
      <p className="max-w-md text-sm text-text-secondary mb-6">{description}</p>

      {actionLabel && onAction && (
        <Button variant="primary" onClick={onAction} className="gap-2">
          <PlusCircle className="h-4 w-4" aria-hidden="true" />
          <span>{actionLabel}</span>
        </Button>
      )}
    </div>
  );
}
