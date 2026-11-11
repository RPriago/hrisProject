import React, { useRef } from 'react';
import { cn } from '@/lib/utils';

export interface TabItem {
  key: string;
  label: string;
  content?: React.ReactNode;
  icon?: React.ReactNode;
}

export interface TabsProps {
  items: TabItem[];
  selectedKey: string;
  onSelect: (key: string) => void;
  className?: string;
  tabListClassName?: string;
  variant?: 'pill' | 'underline';
}

export function Tabs({
  items,
  selectedKey,
  onSelect,
  className,
  tabListClassName,
  variant = 'pill',
}: TabsProps) {
  const tabListRef = useRef<HTMLDivElement>(null);

  const handleKeyDown = (e: React.KeyboardEvent, index: number) => {
    let targetIndex = index;

    if (e.key === 'ArrowRight') {
      e.preventDefault();
      targetIndex = (index + 1) % items.length;
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault();
      targetIndex = (index - 1 + items.length) % items.length;
    } else if (e.key === 'Home') {
      e.preventDefault();
      targetIndex = 0;
    } else if (e.key === 'End') {
      e.preventDefault();
      targetIndex = items.length - 1;
    } else {
      return;
    }

    const targetItem = items[targetIndex];
    if (targetItem) {
      onSelect(targetItem.key);
      const buttons = tabListRef.current?.querySelectorAll<HTMLButtonElement>('[role="tab"]');
      buttons?.[targetIndex]?.focus();
    }
  };

  const activeItem = items.find((item) => item.key === selectedKey) || items[0];

  return (
    <div className={cn('w-full', className)}>
      <div
        ref={tabListRef}
        role="tablist"
        className={cn(
          variant === 'pill'
            ? 'inline-flex items-center p-1.5 rounded-full bg-[#EEEEEE] gap-1'
            : 'flex items-center border-b border-[#ECEEF2] gap-6',
          tabListClassName
        )}
      >
        {items.map((item, idx) => {
          const isSelected = item.key === selectedKey;

          return (
            <button
              key={item.key}
              role="tab"
              id={`tab-${item.key}`}
              aria-controls={`panel-${item.key}`}
              aria-selected={isSelected}
              tabIndex={isSelected ? 0 : -1}
              onClick={() => onSelect(item.key)}
              onKeyDown={(e) => handleKeyDown(e, idx)}
              className={cn(
                'min-h-[44px] px-5 py-2 text-sm font-semibold transition-all inline-flex items-center justify-center gap-2 select-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-navy-action',
                variant === 'pill'
                  ? isSelected
                    ? 'bg-white text-text-primary rounded-full shadow-sm'
                    : 'text-text-secondary hover:text-text-primary rounded-full'
                  : isSelected
                    ? 'border-b-2 border-amber-text text-amber-text'
                    : 'text-text-secondary hover:text-text-primary'
              )}
            >
              {item.icon}
              <span>{item.label}</span>
            </button>
          );
        })}
      </div>

      {activeItem && activeItem.content && (
        <div
          role="tabpanel"
          id={`panel-${activeItem.key}`}
          aria-labelledby={`tab-${activeItem.key}`}
          className="mt-4 focus:outline-none"
          tabIndex={0}
        >
          {activeItem.content}
        </div>
      )}
    </div>
  );
}
