import React from 'react';
import { cn } from '@/lib/utils';
import { Lock } from 'lucide-react';

export type BadgeVariant = 'success' | 'warning' | 'danger' | 'neutral' | 'info' | 'locked' | 'outline';

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: BadgeVariant;
  customIcon?: React.ReactNode;
  showIcon?: boolean;
}

function SolidStatusIcon({
  circleColor,
  children,
}: {
  circleColor: string;
  children: React.ReactNode;
}) {
  return (
    <svg
      viewBox="0 0 16 16"
      fill="none"
      className="h-4 w-4 shrink-0"
      aria-hidden="true"
    >
      <circle cx="8" cy="8" r="8" fill={circleColor} />
      {children}
    </svg>
  );
}

export function Badge({
  className,
  variant = 'neutral',
  customIcon,
  showIcon = true,
  children,
  ...props
}: BadgeProps) {
  const variantConfig: Record<
    BadgeVariant,
    { style: string; defaultIcon: React.ReactNode }
  > = {
    success: {
      style: 'bg-[#E8F8EE] text-[#047857] font-semibold',
      defaultIcon: (
        <SolidStatusIcon circleColor="#16A34A">
          <path
            d="M4.8 8.2L7 10.4L11.4 5.8"
            stroke="white"
            strokeWidth="1.9"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </SolidStatusIcon>
      ),
    },
    warning: {
      style: 'bg-[#FEF3C7] text-[#92400E] font-semibold',
      defaultIcon: (
        <SolidStatusIcon circleColor="#D97706">
          <path
            d="M8 4.4V8.5"
            stroke="white"
            strokeWidth="1.9"
            strokeLinecap="round"
          />
          <circle cx="8" cy="11.2" r="1" fill="white" />
        </SolidStatusIcon>
      ),
    },
    danger: {
      style: 'bg-[#FEE2E2] text-[#991B1B] font-semibold',
      defaultIcon: (
        <SolidStatusIcon circleColor="#DC2626">
          <path
            d="M5.5 5.5L10.5 10.5M10.5 5.5L5.5 10.5"
            stroke="white"
            strokeWidth="1.85"
            strokeLinecap="round"
          />
        </SolidStatusIcon>
      ),
    },
    neutral: {
      style: 'bg-[#F1F5F9] text-[#334155] font-semibold',
      defaultIcon: (
        <SolidStatusIcon circleColor="#64748B">
          <path
            d="M5.4 8H10.6"
            stroke="white"
            strokeWidth="1.9"
            strokeLinecap="round"
          />
        </SolidStatusIcon>
      ),
    },
    info: {
      style: 'bg-[#EFF6FF] text-[#1E40AF] font-semibold',
      defaultIcon: (
        <SolidStatusIcon circleColor="#2563EB">
          <circle cx="8" cy="4.8" r="1" fill="white" />
          <path
            d="M8 7.2V11.4"
            stroke="white"
            strokeWidth="1.9"
            strokeLinecap="round"
          />
        </SolidStatusIcon>
      ),
    },
    locked: {
      style: 'bg-[#11141D] text-white font-semibold',
      defaultIcon: <Lock className="h-3.5 w-3.5 shrink-0 text-amber-400" aria-hidden="true" />,
    },
    outline: {
      style: 'bg-transparent text-[#475569] border border-[#E2E8F0] font-medium',
      defaultIcon: null,
    },
  };

  const { style, defaultIcon } = variantConfig[variant];

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-full text-xs sm:text-[13px] leading-none tabular-nums whitespace-nowrap select-none',
        style,
        className
      )}
      {...props}
    >
      {showIcon ? customIcon || defaultIcon : null}
      <span>{children}</span>
    </span>
  );
}
