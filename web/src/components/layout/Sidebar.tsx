import { useState } from 'react';
import { useAuth } from '@/context/AuthContext';
import {
  LayoutDashboard,
  Users,
  BarChart3,
  Banknote,
  FileText,
  Clock,
  ShieldCheck,
  MessageSquare,
  Settings,
  HelpCircle,
  LogOut,
  Search,
  Command,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react';
import { QuorumLogo } from '@/components/brand/QuorumLogo';
import { cn } from '@/lib/utils';

export interface SidebarProps {
  activePath?: string;
  onNavigate?: (path: string) => void;
  className?: string;
}

export interface SidebarItem {
  id: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  path?: string;
  badge?: string;
  isLogout?: boolean;
  action?: () => void;
}

export interface SidebarGroup {
  title: string;
  items: SidebarItem[];
}

export function Sidebar({ activePath = '/dashboard', onNavigate, className }: SidebarProps) {
  const { logout } = useAuth();
  const [isCollapsed, setIsCollapsed] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');

  const menuGroups: SidebarGroup[] = [
    {
      title: 'MAIN MENU',
      items: [
        { id: 'dash', label: 'Home', icon: LayoutDashboard, path: '/dashboard' },
        { id: 'emp', label: 'Employees', icon: Users, path: '/employees' },
        { id: 'analytics', label: 'Analytics', icon: BarChart3, path: '/analytics', badge: '20' },
        { id: 'payroll', label: 'Payroll Runs', icon: Banknote, path: '/payroll' },
        { id: 'invoices', label: 'Invoices & Slips', icon: FileText, path: '/invoices' },
      ],
    },
    {
      title: 'FEATURES',
      items: [
        { id: 'shifts', label: 'Recurring Shifts', icon: Clock, path: '/shifts' },
        { id: 'statutory', label: 'Statutory & BPJS', icon: ShieldCheck, path: '/statutory' },
        { id: 'feedback', label: 'Feedback & KPI', icon: MessageSquare, path: '/feedback' },
      ],
    },
    {
      title: 'GENERAL',
      items: [
        { id: 'settings', label: 'Settings', icon: Settings, path: '/settings' },
        { id: 'help', label: 'Help Desk', icon: HelpCircle, path: '/help' },
        {
          id: 'logout',
          label: 'Log out',
          icon: LogOut,
          isLogout: true,
          action: () => logout(),
        },
      ],
    },
  ];

  return (
    <aside
      className={cn(
        'relative bg-white text-slate-700 h-full flex flex-col justify-between transition-all duration-300 z-30 select-none shrink-0',
        isCollapsed ? 'w-[76px]' : 'w-[280px]',
        className
      )}
      aria-label="Workspace sidebar navigation"
    >
      {/* Brand Header (Seamless white, no border-b) */}
      <div className="px-5 py-4 flex items-center justify-between min-h-[68px]">
        {!isCollapsed && (
          <div className="flex items-center">
            <QuorumLogo className="h-9 sm:h-10 text-[#0F172A]" />
          </div>
        )}

        <button
          type="button"
          onClick={() => setIsCollapsed(!isCollapsed)}
          className="h-9 w-9 rounded-lg hover:bg-slate-100 text-slate-400 hover:text-slate-700 flex items-center justify-center transition-colors mx-auto md:mx-0"
          aria-label={isCollapsed ? 'Expand sidebar menu' : 'Collapse sidebar menu'}
        >
          {isCollapsed ? <ChevronRight className="h-5 w-5" /> : <ChevronLeft className="h-5 w-5" />}
        </button>
      </div>

      {/* Search Input Box */}
      {!isCollapsed && (
        <div className="px-4 pt-1 pb-2">
          <div className="relative flex items-center">
            <Search className="absolute left-3.5 h-4 w-4 text-slate-400" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search"
              className="w-full bg-transparent border border-[#E2E8F0] text-sm text-slate-800 placeholder-slate-400 pl-10 pr-12 py-2.5 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
            />
            <div className="absolute right-2.5 flex items-center gap-0.5 px-1.5 py-0.5 rounded-md bg-transparent border border-[#E2E8F0] text-[11px] text-slate-500 font-mono">
              <Command className="h-2.5 w-2.5" />
              <span>K</span>
            </div>
          </div>
        </div>
      )}

      {/* Navigation Menu List */}
      <div className="flex-1 overflow-y-auto py-2 px-3 space-y-4">
        {menuGroups.map((group, gIdx) => (
          <div key={gIdx} className="space-y-1">
            {!isCollapsed && (
              <span className="px-3 text-xs font-semibold tracking-wider text-slate-400 uppercase block mb-1.5 pt-2">
                {group.title}
              </span>
            )}

            <div className="space-y-1.5">
              {group.items.map((item) => {
                const Icon = item.icon;
                const isActive = item.path ? activePath === item.path : false;

                if (item.isLogout) {
                  return (
                    <button
                      key={item.id}
                      type="button"
                      onClick={item.action}
                      title={isCollapsed ? item.label : undefined}
                      className={cn(
                        'w-full flex items-center gap-3.5 px-3.5 py-2.5 rounded-xl text-sm font-medium text-[#EF4444] hover:bg-red-50/80 transition-all min-h-[46px]',
                        isCollapsed && 'justify-center px-0'
                      )}
                      aria-label="Log out"
                    >
                      <Icon className="h-5 w-5 shrink-0 text-[#EF4444]" />
                      {!isCollapsed && <span>{item.label}</span>}
                    </button>
                  );
                }

                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => onNavigate?.(item.path!)}
                    title={isCollapsed ? item.label : undefined}
                    className={cn(
                      'w-full flex items-center gap-3.5 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-all min-h-[46px]',
                      isActive
                        ? 'bg-gradient-to-r from-[#11141D] via-[#1B2234] to-[#1E335C] text-white font-semibold shadow-xs'
                        : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50/80',
                      isCollapsed && 'justify-center px-0'
                    )}
                    aria-label={item.label}
                  >
                    <Icon
                      className={cn(
                        'h-5 w-5 shrink-0',
                        isActive ? 'text-white' : 'text-slate-400'
                      )}
                    />
                    {!isCollapsed && (
                      <div className="flex items-center justify-between w-full">
                        <span className="truncate">{item.label}</span>
                        {item.badge && (
                          <span className="inline-flex items-center justify-center min-w-[30px] px-3 py-1.5 rounded-full bg-[#2563EB] text-white text-xs font-semibold leading-none tabular-nums shadow-2xs shrink-0">
                            {item.badge}
                          </span>
                        )}
                      </div>
                    )}
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </div>

      {/* Bottom Area: Starter Plan Card (Transparent bg with crisp thin gray border) */}
      {!isCollapsed && (
        <div className="p-3.5 mt-auto">
          <div className="p-4 sm:p-5 rounded-2xl bg-transparent border border-[#E2E8F0] space-y-3">
            <div>
              <h4 className="text-sm font-semibold text-slate-900">Starter Plan</h4>
              <p className="text-xs text-slate-500 leading-relaxed mt-0.5">
                Upgrade to the enterprise plan & get attractive discounts
              </p>
            </div>
            <button
              type="button"
              onClick={() => onNavigate?.('/pricing')}
              className="w-full py-2.5 px-3 rounded-xl bg-gradient-to-r from-[#3B82F6] via-[#2563EB] to-[#1D4ED8] hover:brightness-105 text-white font-semibold text-xs sm:text-[13px] shadow-xs transition-all active:scale-[0.98]"
            >
              Upgrade Plan
            </button>
          </div>
        </div>
      )}
    </aside>
  );
}
