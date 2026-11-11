import React, { useState } from 'react';
import { Sidebar } from '@/components/layout/Sidebar';
import { Topbar } from '@/components/layout/Topbar';
import { SystemStatusBanner } from '@/components/layout/SystemStatusBanner';
import { cn } from '@/lib/utils';
import { X } from 'lucide-react';

export interface AppShellProps {
  children: React.ReactNode;
  activePath?: string;
  onNavigate?: (path: string) => void;
  className?: string;
}

export function AppShell({
  children,
  activePath = '/dashboard',
  onNavigate,
  className,
}: AppShellProps) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  return (
    <div className={cn('min-h-screen h-screen flex flex-col bg-white text-text-primary antialiased overflow-hidden', className)}>
      {/* Conditional Warning / Grant Banner */}
      <SystemStatusBanner />

      {/* Main Workspace Frame (Full screen height, seamless white container) */}
      <div className="flex-1 flex min-h-0 overflow-hidden bg-white">
        {/* Desktop Sidebar: Always extends 100% full height to the bottom */}
        <Sidebar
          activePath={activePath}
          onNavigate={onNavigate}
          className="hidden md:flex h-full shrink-0"
        />

        {/* Mobile Slide-Over Drawer */}
        {mobileMenuOpen && (
          <div className="fixed inset-0 z-50 md:hidden flex">
            <div
              className="fixed inset-0 bg-slate-900/40 backdrop-blur-xs transition-opacity"
              onClick={() => setMobileMenuOpen(false)}
              aria-hidden="true"
            />
            <div className="relative flex-1 flex flex-col max-w-[280px] w-full bg-white shadow-2xl z-10 animate-in slide-in-from-left duration-200">
              <div className="absolute top-3 right-3 z-20">
                <button
                  type="button"
                  onClick={() => setMobileMenuOpen(false)}
                  className="p-1.5 rounded-lg text-slate-500 hover:text-slate-800 hover:bg-slate-100"
                  aria-label="Close navigation menu"
                >
                  <X className="h-5 w-5" />
                </button>
              </div>
              <Sidebar
                activePath={activePath}
                onNavigate={(path) => {
                  setMobileMenuOpen(false);
                  onNavigate?.(path);
                }}
                className="w-full h-full border-r-0"
              />
            </div>
          </div>
        )}

        {/* Content Column */}
        <div className="flex-1 flex flex-col min-w-0 h-full overflow-hidden bg-white">
          {/* Topbar: Seamless white, no harsh dividing border */}
          <Topbar onOpenMobileMenu={() => setMobileMenuOpen(!mobileMenuOpen)} />

          {/* Main Scrollable Viewport: Inset Canvas with Curved Corner (rounded-tl-[32px]) */}
          <main className="flex-1 overflow-y-auto bg-[#F4F6F9] md:rounded-tl-[32px] p-4 sm:p-5 lg:p-6 border-t border-l border-[#ECEEF2]/70 shadow-2xs">
            <div className="w-full space-y-5 sm:space-y-6">
              {children}
            </div>
          </main>
        </div>
      </div>
    </div>
  );
}
