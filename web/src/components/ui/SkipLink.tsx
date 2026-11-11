import { cn } from '@/lib/utils';

export interface SkipLinkProps {
  targetId?: string;
  label?: string;
  className?: string;
}

export function SkipLink({
  targetId = 'main-content',
  label = 'Loncat ke konten utama',
  className,
}: SkipLinkProps) {
  return (
    <a
      href={`#${targetId}`}
      className={cn(
        'sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-50 focus:px-5 focus:py-3 focus:rounded-xl focus:bg-navy-action focus:text-white focus:font-semibold focus:shadow-2xl focus:outline-none focus:ring-4 focus:ring-amber-fill transition-all',
        className
      )}
    >
      {label}
    </a>
  );
}
