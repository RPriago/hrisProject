import { Building2, Users, Wallet, Smartphone, ArrowRight } from 'lucide-react';

export interface AudienceSectionProps {
  onOpenDemoModal: () => void;
}

export function AudienceSection({ onOpenDemoModal }: AudienceSectionProps) {
  return (
    <section className="w-full bg-white py-12 sm:py-20">
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Rounded Light Blue Container Box */}
        <div className="bg-blue-50/70 border border-blue-100 rounded-3xl p-6 sm:p-12">
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-12 items-center">
            {/* Left Column: Heading, Subtitle & Action Button */}
            <div className="lg:col-span-5 space-y-5">
              <span className="text-xs font-bold tracking-wider text-blue-700 uppercase bg-blue-100/60 px-3 py-1 rounded-full">
                TARGET AUDIENCE
              </span>
              <h2 className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight leading-tight">
                Who Is Quorum Designed For?
              </h2>
              <p className="text-slate-600 text-sm sm:text-base leading-relaxed">
                Purpose-built to adapt to various business scales, from fast-growing scaleups to multi-branch corporate groups.
              </p>
              <div className="pt-2">
                <button
                  type="button"
                  onClick={onOpenDemoModal}
                  className="inline-flex items-center gap-2 px-6 py-3 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-sm shadow-sm transition-all"
                >
                  <span>Start Free Trial</span>
                  <ArrowRight className="h-4 w-4" />
                </button>
              </div>
            </div>

            {/* Right Column: 2x2 Grid of 4 Persona Cards */}
            <div className="lg:col-span-7">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {/* Persona 1 */}
                <div className="bg-white p-5 rounded-2xl border border-blue-100/80 shadow-xs hover:shadow-sm transition-all">
                  <div className="h-10 w-10 rounded-xl bg-blue-50 text-blue-600 flex items-center justify-center mb-3">
                    <Building2 className="h-5 w-5" />
                  </div>
                  <h3 className="text-sm font-bold text-slate-900 mb-1">
                    Founders & Executives
                  </h3>
                  <p className="text-xs text-slate-600 leading-relaxed">
                    Complete visibility into workforce operations, cost-efficient payroll, and worry-free regulatory compliance.
                  </p>
                </div>

                {/* Persona 2 */}
                <div className="bg-white p-5 rounded-2xl border border-blue-100/80 shadow-xs hover:shadow-sm transition-all">
                  <div className="h-10 w-10 rounded-xl bg-violet-50 text-violet-600 flex items-center justify-center mb-3">
                    <Users className="h-5 w-5" />
                  </div>
                  <h3 className="text-sm font-bold text-slate-900 mb-1">
                    HR & People Teams
                  </h3>
                  <p className="text-xs text-slate-600 leading-relaxed">
                    Save up to 80% of time spent on leave approvals, attendance reconciliation, and contract document filing.
                  </p>
                </div>

                {/* Persona 3 */}
                <div className="bg-white p-5 rounded-2xl border border-blue-100/80 shadow-xs hover:shadow-sm transition-all">
                  <div className="h-10 w-10 rounded-xl bg-emerald-50 text-emerald-600 flex items-center justify-center mb-3">
                    <Wallet className="h-5 w-5" />
                  </div>
                  <h3 className="text-sm font-bold text-slate-900 mb-1">
                    Finance & Payroll Managers
                  </h3>
                  <p className="text-xs text-slate-600 leading-relaxed">
                    Close monthly payroll faster, automate tax and statutory deductions, and streamline bank batch payouts.
                  </p>
                </div>

                {/* Persona 4 */}
                <div className="bg-white p-5 rounded-2xl border border-blue-100/80 shadow-xs hover:shadow-sm transition-all">
                  <div className="h-10 w-10 rounded-xl bg-amber-50 text-amber-600 flex items-center justify-center mb-3">
                    <Smartphone className="h-5 w-5" />
                  </div>
                  <h3 className="text-sm font-bold text-slate-900 mb-1">
                    Employees & Line Managers
                  </h3>
                  <p className="text-xs text-slate-600 leading-relaxed">
                    Self-service mobile portal (ESS) for instant clock-in, flexible leave submissions, digital payslips, and fast approvals.
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
