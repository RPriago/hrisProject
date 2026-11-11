import { QuorumLogo } from '@/components/brand/QuorumLogo';

export interface FooterSectionProps {
  onOpenDemoModal: () => void;
}

export function FooterSection({ onOpenDemoModal }: FooterSectionProps) {
  return (
    <footer className="w-full bg-white border-t border-slate-200 py-12 sm:py-16 text-slate-600">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-2 md:grid-cols-5 gap-8 pb-12 border-b border-slate-100">
          {/* Brand Column */}
          <div className="col-span-2 space-y-4">
            <div className="flex items-center">
              <QuorumLogo className="h-10 text-[#0F172A]" />
            </div>
            <p className="text-xs sm:text-sm text-slate-500 max-w-sm leading-relaxed">
              Integrated cloud HRIS & Payroll platform built with enterprise-grade data isolation, automated statutory compliance, and mobile self-service.
            </p>
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-emerald-50 text-emerald-700 text-xs font-semibold border border-emerald-100">
              <span className="h-2 w-2 rounded-full bg-emerald-500" />
              <span>All Systems Operational</span>
            </div>
          </div>

          {/* Links Column 1: Product */}
          <div className="space-y-3">
            <h4 className="text-xs font-bold text-slate-900 uppercase tracking-wider">
              Product
            </h4>
            <ul className="space-y-2 text-xs sm:text-sm">
              <li>
                <a href="#features" className="hover:text-blue-600 transition-colors">
                  GPS & Attendance
                </a>
              </li>
              <li>
                <a href="#features" className="hover:text-blue-600 transition-colors">
                  Payroll & Taxes
                </a>
              </li>
              <li>
                <a href="#features" className="hover:text-blue-600 transition-colors">
                  Shifts & Leave
                </a>
              </li>
              <li>
                <a href="#features" className="hover:text-blue-600 transition-colors">
                  Employee ESS Portal
                </a>
              </li>
              <li>
                <a href="#features" className="hover:text-blue-600 transition-colors">
                  Org Structure
                </a>
              </li>
            </ul>
          </div>

          {/* Links Column 2: Company */}
          <div className="space-y-3">
            <h4 className="text-xs font-bold text-slate-900 uppercase tracking-wider">
              Company
            </h4>
            <ul className="space-y-2 text-xs sm:text-sm">
              <li>
                <a href="#advantages" className="hover:text-blue-600 transition-colors">
                  About Us
                </a>
              </li>
              <li>
                <a href="#services" className="hover:text-blue-600 transition-colors">
                  Services & Solutions
                </a>
              </li>
              <li>
                <a href="#pricing" className="hover:text-blue-600 transition-colors">
                  Pricing Plans
                </a>
              </li>
              <li>
                <button
                  type="button"
                  onClick={onOpenDemoModal}
                  className="hover:text-blue-600 transition-colors text-left"
                >
                  Demo Accounts (7 Roles)
                </button>
              </li>
            </ul>
          </div>

          {/* Links Column 3: Support & Legal */}
          <div className="space-y-3">
            <h4 className="text-xs font-bold text-slate-900 uppercase tracking-wider">
              Support & Legal
            </h4>
            <ul className="space-y-2 text-xs sm:text-sm">
              <li>
                <a href="#faq" className="hover:text-blue-600 transition-colors">
                  Help Center / FAQ
                </a>
              </li>
              <li>
                <span className="hover:text-blue-600 transition-colors cursor-pointer">
                  Privacy Policy
                </span>
              </li>
              <li>
                <span className="hover:text-blue-600 transition-colors cursor-pointer">
                  Terms of Service
                </span>
              </li>
              <li>
                <span className="hover:text-blue-600 transition-colors cursor-pointer">
                  Data Security
                </span>
              </li>
            </ul>
          </div>
        </div>

        {/* Bottom Copyright Strip */}
        <div className="pt-8 flex flex-col sm:flex-row items-center justify-between gap-4 text-xs text-slate-500">
          <div>© 2026 Quorum Inc. All rights reserved.</div>
          <div className="flex items-center gap-6">
            <span>Statutory Compliance Standard</span>
            <span>Bank-Grade Encryption</span>
          </div>
        </div>
      </div>
    </footer>
  );
}
