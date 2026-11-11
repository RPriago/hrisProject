import React, { useEffect, useRef, useState, useCallback, type RefObject } from 'react';
import { Check, ChevronDown } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * Closes an open dropdown when clicking outside its container or pressing Escape.
 */
export function useDropdownDismiss(
  containerRef: RefObject<HTMLElement | null>,
  isOpen: boolean,
  onClose: () => void
) {
  useEffect(() => {
    if (!isOpen) return;

    function handleMouseDown(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        onClose();
      }
    }

    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        onClose();
      }
    }

    document.addEventListener('mousedown', handleMouseDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handleMouseDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [containerRef, isOpen, onClose]);
}

export interface DropdownPanelProps extends React.HTMLAttributes<HTMLDivElement> {
  position?: 'bottom' | 'top';
  align?: 'right' | 'left';
}

export function DropdownPanel({
  position = 'bottom',
  align = 'right',
  className,
  children,
  ...props
}: DropdownPanelProps) {
  return (
    <div
      role="menu"
      className={cn(
        'absolute z-50 rounded-3xl bg-white border border-[#ECEEF2] p-2 shadow-[0_14px_34px_-10px_rgba(15,23,42,0.10)] text-left space-y-0.5 animate-in fade-in zoom-in-95 duration-150',
        position === 'bottom' ? 'top-full mt-2' : 'bottom-full mb-2',
        align === 'right' ? 'right-0' : 'left-0',
        className
      )}
      {...props}
    >
      {children}
    </div>
  );
}

export function DropdownSectionLabel({
  className,
  children,
  ...props
}: React.HTMLAttributes<HTMLSpanElement>) {
  return (
    <span
      className={cn(
        'px-3.5 pt-1.5 pb-1 text-[11px] font-semibold tracking-wider text-slate-500 uppercase block',
        className
      )}
      {...props}
    >
      {children}
    </span>
  );
}

export function DropdownDivider({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      role="separator"
      className={cn('my-1.5 -mx-2 border-t border-[#ECEEF2]', className)}
      {...props}
    />
  );
}

export interface DropdownMenuItemProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  icon?: React.ReactNode;
  active?: boolean;
  showCheck?: boolean;
}

export function DropdownMenuItem({
  icon,
  active = false,
  showCheck = false,
  className,
  children,
  ...props
}: DropdownMenuItemProps) {
  return (
    <button
      type="button"
      role="menuitem"
      className={cn(
        'w-full flex items-center justify-between gap-3 px-3.5 py-2.5 rounded-full text-sm transition-colors whitespace-nowrap focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-inset',
        active
          ? 'bg-[#F4F5F7] text-[#111827] font-semibold'
          : 'text-[#1E232D] font-medium hover:bg-[#F4F5F7]',
        className
      )}
      {...props}
    >
      <span className="flex items-center gap-3 min-w-0 truncate">
        {icon && (
          <span
            className={cn(
              'shrink-0 flex items-center justify-center',
              active ? 'text-[#111827]' : 'text-slate-500'
            )}
            aria-hidden="true"
          >
            {icon}
          </span>
        )}
        <span className="truncate">{children}</span>
      </span>
      {active && showCheck && (
        <Check className="h-4 w-4 text-slate-700 shrink-0" aria-hidden="true" />
      )}
    </button>
  );
}

export interface DropdownSelectOption<T extends string | number> {
  value: T;
  label: string;
  icon?: React.ReactNode;
  dividerBefore?: boolean;
}

export interface DropdownSelectProps<T extends string | number> {
  value: T;
  options: readonly DropdownSelectOption<T>[];
  onChange: (value: T) => void;
  buttonLabel?: React.ReactNode;
  trailingIcon?: React.ReactNode;
  ariaLabel?: string;
  buttonClassName?: string;
  panelClassName?: string;
  position?: 'bottom' | 'top';
  align?: 'right' | 'left';
}

export function DropdownSelect<T extends string | number>({
  value,
  options,
  onChange,
  buttonLabel,
  trailingIcon,
  ariaLabel,
  buttonClassName,
  panelClassName,
  position = 'bottom',
  align = 'right',
}: DropdownSelectProps<T>) {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const handleClose = useCallback(() => {
    setIsOpen(false);
  }, []);

  useDropdownDismiss(containerRef, isOpen, handleClose);

  const selectedOption = options.find((opt) => opt.value === value);

  return (
    <div ref={containerRef} className="relative shrink-0">
      <button
        type="button"
        onClick={() => setIsOpen((prev) => !prev)}
        aria-expanded={isOpen}
        aria-haspopup="menu"
        aria-label={ariaLabel}
        className={cn(
          'px-3.5 py-2 rounded-lg border border-[#E2E8F0] bg-transparent hover:border-slate-300 hover:bg-slate-50/60 text-[13px] sm:text-sm font-medium text-slate-800 whitespace-nowrap flex items-center gap-2 transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600',
          buttonClassName
        )}
      >
        <span>{buttonLabel ?? selectedOption?.label ?? String(value)}</span>
        {trailingIcon ?? (
          <ChevronDown
            className={cn('h-4 w-4 text-slate-500 transition-transform duration-150', isOpen && 'rotate-180')}
            aria-hidden="true"
          />
        )}
      </button>

      {isOpen && (
        <DropdownPanel position={position} align={align} className={panelClassName}>
          {options.map((opt) => (
            <React.Fragment key={String(opt.value)}>
              {opt.dividerBefore && <DropdownDivider />}
              <DropdownMenuItem
                icon={opt.icon}
                active={value === opt.value}
                showCheck
                onClick={() => {
                  onChange(opt.value);
                  setIsOpen(false);
                }}
              >
                {opt.label}
              </DropdownMenuItem>
            </React.Fragment>
          ))}
        </DropdownPanel>
      )}
    </div>
  );
}
