import { useState, useRef, useCallback, useEffect } from 'react';
import { useAuth } from '@/context/AuthContext';
import { useTenant } from '@/context/TenantContext';
import {
  HelpCircle,
  Mail,
  Bell,
  ChevronsUpDown,
  Building2,
  ShieldCheck,
  LogOut,
  Menu,
} from 'lucide-react';
import {
  DropdownPanel,
  DropdownSectionLabel,
  DropdownDivider,
  DropdownMenuItem,
  useDropdownDismiss,
} from '@/components/ui/DropdownMenu';
import { cn, sanitizeSafeAssetUrl } from '@/lib/utils';
import { registerMemoryWipeListener } from '@/lib/queryClient';

export interface TopbarProps {
  onOpenMobileMenu?: () => void;
  className?: string;
}

export function Topbar({ onOpenMobileMenu, className }: TopbarProps) {
  const { user, logout } = useAuth();
  const { activeTenant, availableTenants, switchTenant } = useTenant();
  const [isProfileOpen, setIsProfileOpen] = useState(false);
  const [activeNotification, setActiveNotification] = useState<string | null>(null);
  const [avatarLoadError, setAvatarLoadError] = useState(false);

  const profileRef = useRef<HTMLDivElement>(null);
  const closeProfile = useCallback(() => setIsProfileOpen(false), []);
  useDropdownDismiss(profileRef, isProfileOpen, closeProfile);

  useEffect(() => {
    return registerMemoryWipeListener(() => {
      setActiveNotification(null);
      setIsProfileOpen(false);
    });
  }, []);

  useEffect(() => {
    setAvatarLoadError(false);
  }, [user?.avatarUrl]);

  const firstName = user?.name ? user.name.split(' ')[0] : 'Pengguna';
  const safeAvatarUrl = sanitizeSafeAssetUrl(user?.avatarUrl);
  const showPhotoAvatar = Boolean(safeAvatarUrl) && !avatarLoadError;

  return (
    <header
      className={cn(
        'w-full bg-white px-4 sm:px-6 py-4 flex items-center justify-between gap-4 z-20 min-h-[68px]',
        className
      )}
    >
      {/* Left: Mobile Menu Trigger + Welcome Message */}
      <div className="flex items-center gap-2.5 min-w-0">
        {onOpenMobileMenu && (
          <button
            type="button"
            onClick={onOpenMobileMenu}
            className="md:hidden h-9 w-9 rounded-lg border border-[#ECEEF2] hover:bg-slate-50 flex items-center justify-center text-slate-600 transition-colors shrink-0"
            aria-label="Open navigation menu"
          >
            <Menu className="h-5 w-5" />
          </button>
        )}

        <div className="flex items-center gap-2 min-w-0">
          <h1 className="text-xl sm:text-2xl font-semibold text-[#111827] tracking-tight truncate">
            Welcome back, {firstName}
          </h1>
        </div>
      </div>

      {/* Right: Quick Action Icons + User Profile Dropdown */}
      <div className="flex items-center gap-2 sm:gap-3 shrink-0">
        {/* Help Button */}
        <button
          type="button"
          onClick={() => setActiveNotification('Documentation & Help Desk opened.')}
          className="hidden sm:flex h-10 w-10 rounded-xl border border-[#E2E8F0] bg-transparent hover:border-slate-300 hover:bg-slate-50/60 text-slate-600 hover:text-slate-900 items-center justify-center transition-colors shadow-2xs"
          aria-label="Help desk"
        >
          <HelpCircle className="h-5 w-5" />
        </button>

        {/* Messages Button */}
        <button
          type="button"
          onClick={() => setActiveNotification('Messages inbox opened.')}
          className="hidden sm:flex h-10 w-10 rounded-xl border border-[#E2E8F0] bg-transparent hover:border-slate-300 hover:bg-slate-50/60 text-slate-600 hover:text-slate-900 items-center justify-center transition-colors shadow-2xs"
          aria-label="Messages"
        >
          <Mail className="h-5 w-5" />
        </button>

        {/* Notifications Button */}
        <div className="relative">
          <button
            type="button"
            onClick={() => setActiveNotification('Payroll reminders and alert notifications cleared.')}
            className="h-10 w-10 rounded-xl border border-[#E2E8F0] bg-transparent hover:border-slate-300 hover:bg-slate-50/60 text-slate-600 hover:text-slate-900 flex items-center justify-center transition-colors shadow-2xs relative"
            aria-label="Notifications"
          >
            <Bell className="h-5 w-5" />
            <span className="absolute top-2 right-2 h-2.5 w-2.5 rounded-full bg-red-500 ring-2 ring-white" />
          </button>
        </div>

        {/* Notification Toast Message */}
        {activeNotification && (
          <div className="hidden xl:flex items-center gap-2 px-3 py-1 rounded-md bg-blue-50 border border-blue-200 text-blue-700 text-xs font-medium animate-in fade-in duration-150">
            <span>{activeNotification}</span>
            <button
              type="button"
              onClick={() => setActiveNotification(null)}
              className="text-blue-500 hover:text-blue-800 font-bold ml-1"
            >
              ×
            </button>
          </div>
        )}

        {/* User Profile Pill / Dropdown Trigger */}
        <div ref={profileRef} className="relative">
          <button
            type="button"
            onClick={() => setIsProfileOpen(!isProfileOpen)}
            className="min-h-[44px] flex items-center gap-3 p-1 sm:pl-1.5 sm:pr-3 rounded-full border border-transparent hover:border-[#ECEEF2] hover:bg-slate-50 transition-all text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
            aria-label="User account menu"
            aria-expanded={isProfileOpen}
            aria-haspopup="menu"
          >
            <div className="h-10 w-10 rounded-full overflow-hidden ring-1 ring-slate-200/90 bg-[#1E232D] text-white font-semibold text-sm flex items-center justify-center shrink-0">
              {showPhotoAvatar ? (
                <img
                  src={safeAvatarUrl!}
                  alt={user?.name || 'User profile'}
                  width={40}
                  height={40}
                  onError={() => setAvatarLoadError(true)}
                  className="h-full w-full object-cover object-center"
                />
              ) : (
                <span>{user?.avatarInitial || 'US'}</span>
              )}
            </div>

            <div className="hidden sm:flex flex-col min-w-0">
              <span className="text-sm font-semibold text-slate-900 truncate">
                {user?.name || 'Pengguna Terautentikasi'}
              </span>
              <span className="text-xs text-slate-500 truncate">
                {user?.email || '-'}
              </span>
            </div>

            <ChevronsUpDown className="h-4 w-4 text-slate-400 shrink-0 ml-0.5" />
          </button>

          {/* Profile Dropdown Menu */}
          {isProfileOpen && (
            <DropdownPanel className="w-72 sm:w-80">
              {/* Active Tenant / Company Switcher */}
              <div className="space-y-0.5">
                <DropdownSectionLabel>Active Company</DropdownSectionLabel>
                {availableTenants.map((t) => {
                  const isActive = activeTenant?.id === t.id;
                  return (
                    <DropdownMenuItem
                      key={t.id}
                      icon={<Building2 className="h-[18px] w-[18px]" strokeWidth={1.75} />}
                      active={isActive}
                      showCheck
                      onClick={() => {
                        switchTenant(t.id);
                        setIsProfileOpen(false);
                      }}
                    >
                      {t.name}
                    </DropdownMenuItem>
                  );
                })}
              </div>

              <DropdownDivider />

              {/* Role & Data Scope */}
              <div className="px-3.5 py-2 space-y-1.5 text-[13px]">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-slate-500 font-medium">Role:</span>
                  <span className="font-semibold text-[#111827] flex items-center gap-1.5">
                    <ShieldCheck className="h-4 w-4 text-slate-500 shrink-0" strokeWidth={1.75} aria-hidden="true" />
                    <span>{user?.role}</span>
                  </span>
                </div>
                <div className="flex items-center justify-between gap-2">
                  <span className="text-slate-500 font-medium">Scope:</span>
                  <span className="text-slate-800 font-medium">{user?.dataScope}</span>
                </div>
              </div>

              <DropdownDivider />

              {/* Help & Log out */}
              <div className="space-y-0.5">
                <DropdownMenuItem
                  icon={<HelpCircle className="h-[18px] w-[18px]" strokeWidth={1.75} />}
                  onClick={() => {
                    setIsProfileOpen(false);
                    setActiveNotification('Help & Support desk opened.');
                  }}
                >
                  Help & Support
                </DropdownMenuItem>
                <DropdownMenuItem
                  icon={<LogOut className="h-[18px] w-[18px]" strokeWidth={1.75} />}
                  onClick={() => {
                    setIsProfileOpen(false);
                    logout();
                  }}
                >
                  Log out
                </DropdownMenuItem>
              </div>
            </DropdownPanel>
          )}
        </div>
      </div>
    </header>
  );
}
