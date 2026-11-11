import React from 'react';
import { cn } from '@/lib/utils';
import { Loader2 } from 'lucide-react';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'amber' | 'outline' | 'ghost' | 'danger';
  size?: 'sm' | 'md' | 'lg' | 'icon';
  isLoading?: boolean;
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  (
    {
      className,
      variant = 'primary',
      size = 'md',
      isLoading = false,
      disabled,
      children,
      type = 'button',
      ...props
    },
    ref
  ) => {
    const baseStyles =
      'inline-flex items-center justify-center font-medium transition-colors select-none focus-visible:outline-none focus-visible:ring-2 disabled:pointer-events-none disabled:opacity-50 min-h-[44px] min-w-[44px] rounded-xl';

    const variantStyles = {
      primary:
        'bg-navy-action text-white hover:bg-navy-action-hover focus-visible:ring-navy-action shadow-floating-control',
      amber:
        'bg-amber-fill text-amber-ink hover:bg-amber-fill-hover focus-visible:ring-amber-fill font-semibold',
      outline:
        'border border-interactive bg-transparent text-text-primary hover:bg-surface-soft focus-visible:ring-navy-action',
      ghost:
        'bg-transparent text-text-secondary hover:bg-surface-soft hover:text-text-primary focus-visible:ring-navy-action',
      danger:
        'bg-[#B42318] text-white hover:bg-[#991B1B] focus-visible:ring-[#B42318]',
    };

    const sizeStyles = {
      sm: 'px-3 py-2 text-xs',
      md: 'px-4 py-2.5 text-sm',
      lg: 'px-6 py-3.5 text-base',
      icon: 'p-2.5',
    };

    return (
      <button
        ref={ref}
        type={type}
        disabled={disabled || isLoading}
        className={cn(baseStyles, variantStyles[variant], sizeStyles[size], className)}
        {...props}
      >
        {isLoading ? (
          <>
            <Loader2 className="mr-2 h-4 w-4 animate-spin" role="status" aria-label="Memuat..." />
            <span>{children}</span>
          </>
        ) : (
          children
        )}
      </button>
    );
  }
);

Button.displayName = 'Button';
