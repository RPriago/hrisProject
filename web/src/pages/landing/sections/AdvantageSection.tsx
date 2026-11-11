import { CheckCircle2, TrendingUp, ShieldCheck } from 'lucide-react';

export function AdvantageSection() {
  return (
    <section id="advantages" className="w-full bg-white py-16 sm:py-24 scroll-mt-20">
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="text-center max-w-2xl mx-auto mb-12 sm:mb-16">
          <span className="text-xs font-bold tracking-wider text-blue-600 uppercase">
            THE QUORUM ADVANTAGE
          </span>
          <h2 className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight mt-2">
            Superior Architecture for Maximum Operational Reliability
          </h2>
          <p className="text-slate-600 text-sm sm:text-base mt-3 leading-relaxed">
            Eliminate repetitive tasks, accelerate monthly payroll close, and safeguard your company's workforce data.
          </p>
        </div>

        {/* 3 Advantage Cards Grid */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 sm:gap-8 items-stretch">
          {/* Card 1 */}
          <div className="bg-white p-6 sm:p-8 rounded-2xl sm:rounded-3xl border border-slate-200/80 shadow-xs hover:shadow-md transition-all">
            <div className="h-12 w-12 rounded-xl bg-blue-50 text-blue-600 flex items-center justify-center mb-6">
              <CheckCircle2 className="h-6 w-6" />
            </div>
            <h3 className="text-lg sm:text-xl font-bold text-slate-900 mb-2.5">
              Automated Daily Administration
            </h3>
            <p className="text-sm text-slate-600 leading-relaxed">
              Attendance records, overtime calculations, and leave requests connected seamlessly in one system without manual paperwork.
            </p>
          </div>

          {/* Card 2 */}
          <div className="bg-white p-6 sm:p-8 rounded-2xl sm:rounded-3xl border border-slate-200/80 shadow-xs hover:shadow-md transition-all">
            <div className="h-12 w-12 rounded-xl bg-blue-50 text-blue-600 flex items-center justify-center mb-6">
              <TrendingUp className="h-6 w-6" />
            </div>
            <h3 className="text-lg sm:text-xl font-bold text-slate-900 mb-2.5">
              Instant Payroll & Tax Calculation
            </h3>
            <p className="text-sm text-slate-600 leading-relaxed">
              Automated calculation of salary components, allowances, tax deductions, and statutory contributions down to the exact penny.
            </p>
          </div>

          {/* Card 3 */}
          <div className="bg-white p-6 sm:p-8 rounded-2xl sm:rounded-3xl border border-slate-200/80 shadow-xs hover:shadow-md transition-all">
            <div className="h-12 w-12 rounded-xl bg-blue-600 text-white flex items-center justify-center mb-6 shadow-md shadow-blue-500/25">
              <ShieldCheck className="h-6 w-6" />
            </div>
            <h3 className="text-lg sm:text-xl font-bold text-slate-900 mb-2.5">
              Enterprise-Grade Data Security
            </h3>
            <p className="text-sm text-slate-600 leading-relaxed">
              Multi-tenant data isolation with modern encryption standards, granular role-based access, and transparent audit trails.
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}
