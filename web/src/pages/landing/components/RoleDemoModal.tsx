import { useState, useEffect, useRef } from 'react';
import { useAuth, type UserProfile, type DemoRole } from '@/context/AuthContext';
import { X, Copy, Check, ArrowRight, ShieldCheck } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface RoleDemoModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSelectRole?: (roleId: string) => void;
  initialRole?: string;
}

const ROLE_DESCRIPTIONS: Record<string, string> = {
  PlatformAdmin: 'Platform-level operations, multi-tenant monitoring, and system health.',
  CompanyOwner: 'Enterprise group owner with complete organizational control and authorization access.',
  HrAdmin: 'Management of employee records, organizational structures, and workforce compliance.',
  PayrollOfficer: 'Automated salary calculations, tax withholdings, statutory contributions, and payout approvals.',
  Manager: 'Overtime approvals, team leave requests, and daily attendance summaries.',
  Employee: 'Self-service portal (ESS), daily clock-in, leave requests, and digital payslips.',
  Auditor: 'Operational compliance reviews and activity logs in read-only mode.',
};

export function RoleDemoModal({
  isOpen,
  onClose,
  onSelectRole,
  initialRole,
}: RoleDemoModalProps) {
  const { availableDemoRoles, login } = useAuth();
  const demoUsers = Object.values(availableDemoRoles);
  const [copiedRole, setCopiedRole] = useState<string | null>(null);
  const closeBtnRef = useRef<HTMLButtonElement>(null);
  const modalRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (isOpen) {
      closeBtnRef.current?.focus();
    }
  }, [isOpen]);

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape' && isOpen) {
        onClose();
      }
    }
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const handleCopy = async (roleId: string, email: string) => {
    try {
      await navigator.clipboard.writeText(email);
      setCopiedRole(roleId);
      setTimeout(() => setCopiedRole(null), 3000);
    } catch {
      setCopiedRole(roleId);
      setTimeout(() => setCopiedRole(null), 3000);
    }
  };

  const handleSelect = (user: UserProfile) => {
    login(user.role as DemoRole);
    onClose();
    onSelectRole?.(user.role);
  };

  return (
    <div
      className="fixed inset-0 z-50 overflow-y-auto bg-slate-900/60 backdrop-blur-sm flex items-center justify-center p-4 sm:p-6 animate-in fade-in duration-200"
      role="dialog"
      aria-modal="true"
      aria-label="Select a ready-to-use demo role"
      onClick={onClose}
    >
      <div
        ref={modalRef}
        className="w-full max-w-2xl bg-white rounded-3xl shadow-2xl border border-slate-100 p-6 sm:p-8 flex flex-col space-y-6 animate-in zoom-in-95 duration-200"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-4 border-b border-slate-100 pb-5">
          <div>
            <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-blue-50 text-blue-700 text-xs font-semibold mb-2 border border-blue-100">
              <ShieldCheck className="h-3.5 w-3.5" />
              <span>7 Verified Role Accounts</span>
            </div>
            <h3 className="text-xl sm:text-2xl font-bold text-slate-900 tracking-tight">
              Select a Ready-to-Use Demo Role
            </h3>
            <p className="text-xs sm:text-sm text-slate-600 mt-1">
              Each role features customized permissions, data scope, and dashboard views tailored to distinct organizational responsibilities.
            </p>
          </div>

          <button
            ref={closeBtnRef}
            type="button"
            onClick={onClose}
            className="h-9 w-9 rounded-full bg-slate-100 hover:bg-slate-200 flex items-center justify-center text-slate-500 hover:text-slate-800 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
            aria-label="Close demo role modal"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Roles List */}
        <div className="space-y-3 max-h-[60vh] overflow-y-auto pr-1">
          {demoUsers.map((user) => {
            const isCopied = copiedRole === user.role;
            const isInitial = initialRole === user.role;

            return (
              <div
                key={user.id}
                className={cn(
                  'p-4 rounded-2xl border transition-all flex flex-col sm:flex-row sm:items-center justify-between gap-4',
                  isInitial
                    ? 'border-blue-300 bg-blue-50/50 shadow-sm'
                    : 'border-slate-200/80 bg-white hover:border-blue-200 hover:bg-slate-50/60'
                )}
              >
                {/* Role Details */}
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="inline-flex items-center text-xs font-semibold px-3 py-1 rounded-full leading-none bg-slate-900 text-white">
                      {user.role}
                    </span>
                    <h4 className="text-sm font-bold text-slate-900">{user.name}</h4>
                  </div>
                  <p className="text-xs text-slate-600 leading-relaxed">
                    {ROLE_DESCRIPTIONS[user.role] || `Data Scope: ${user.dataScope}`}
                  </p>
                  <div className="flex items-center gap-2 pt-1">
                    <span className="text-[11px] font-mono text-slate-600 bg-slate-100 px-2 py-0.5 rounded">
                      {user.email}
                    </span>
                    <button
                      type="button"
                      onClick={() => handleCopy(user.role, user.email)}
                      className="text-[11px] text-blue-600 hover:text-blue-700 font-medium hover:underline inline-flex items-center gap-1 focus-visible:outline-none"
                      aria-label={`Copy ${user.role} email`}
                    >
                      {isCopied ? (
                        <>
                          <Check className="h-3 w-3 text-emerald-600" />
                          <span className="text-emerald-600 font-semibold">Email copied</span>
                        </>
                      ) : (
                        <>
                          <Copy className="h-3 w-3" />
                          <span>Copy Credentials</span>
                        </>
                      )}
                    </button>
                  </div>
                </div>

                {/* Enter Button */}
                <button
                  type="button"
                  onClick={() => handleSelect(user)}
                  className={cn(
                    'inline-flex items-center justify-center px-4 py-2 text-xs font-semibold rounded-xl transition-all shadow-sm shrink-0 self-end sm:self-center focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2',
                    isInitial
                      ? 'bg-blue-600 hover:bg-blue-700 text-white focus-visible:ring-blue-600'
                      : 'bg-white hover:bg-slate-50 text-slate-700 border border-slate-200 hover:border-slate-300 focus-visible:ring-slate-400'
                  )}
                  aria-label={`Enter ${user.role} workspace`}
                >
                  <span>Enter</span>
                  <ArrowRight className="h-3.5 w-3.5 ml-1.5" />
                </button>
              </div>
            );
          })}
        </div>

        {/* Footer */}
        <div className="pt-4 border-t border-slate-100 flex justify-between items-center text-xs text-slate-500">
          <span>Test data is isolated and automatically reset when switching accounts.</span>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold rounded-xl border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}
