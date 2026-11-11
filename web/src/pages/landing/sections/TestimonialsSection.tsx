export interface TestimonialsSectionProps {
  onOpenDemoModal: () => void;
}

interface Testimonial {
  name: string;
  role: string;
  company: string;
  initials: string;
  color: string;
  quote: string;
}

const TESTIMONIALS: Testimonial[] = [
  {
    name: 'Ryan Cooper',
    role: 'HR Director',
    company: 'Apex Logistics',
    initials: 'RC',
    color: 'bg-blue-600',
    quote: 'Our monthly payroll close used to take 4 business days. With Quorum, it is done in under 2 hours with 100% accuracy and zero discrepancies.',
  },
  {
    name: 'Sarah Jenkins',
    role: 'Head of People',
    company: 'FinTech Global',
    initials: 'SJ',
    color: 'bg-violet-600',
    quote: 'Automated tax withholding calculations and statutory filing saved our team hundreds of hours previously lost in complicated spreadsheets.',
  },
  {
    name: 'David Miller',
    role: 'Chief Operating Officer',
    company: 'Mega Retail Group',
    initials: 'DM',
    color: 'bg-emerald-600',
    quote: 'Scheduling 800+ employees across 14 locations is now monitored in real time. Shift clashes and untracked overtime are completely gone.',
  },
  {
    name: 'Maya Anderson',
    role: 'Finance Manager',
    company: 'CareHealth Group',
    initials: 'MA',
    color: 'bg-amber-600',
    quote: 'Exporting batch payouts directly to corporate banking simplified our entire reconciliation process into just a few clicks.',
  },
  {
    name: 'Marcus Lee',
    role: 'Founder & CEO',
    company: 'Nexus Creative',
    initials: 'ML',
    color: 'bg-indigo-600',
    quote: 'The user interface is remarkably clean, modern, and intuitive. New team members pick up the employee self-service portal immediately.',
  },
  {
    name: 'Natalie Brooks',
    role: 'People Operations Lead',
    company: 'Agro Industries',
    initials: 'NB',
    color: 'bg-teal-600',
    quote: 'Our data migration from legacy spreadsheets was completed in just 2 days. The customer support team was exceptionally responsive.',
  },
  {
    name: 'Henry Vance',
    role: 'VP Human Capital',
    company: 'Sentosa Group',
    initials: 'HV',
    color: 'bg-rose-600',
    quote: 'Multi-tenant data isolation is robust and gives our executive board complete confidence in confidentiality across all business units.',
  },
  {
    name: 'Laura Wright',
    role: 'Payroll Specialist',
    company: 'Prime Construction',
    initials: 'LW',
    color: 'bg-blue-700',
    quote: 'Digital payslips are automatically and securely delivered to every employee mobile device right on payday.',
  },
  {
    name: 'Arthur Scott',
    role: 'General Manager',
    company: 'Grand Hospitality',
    initials: 'AS',
    color: 'bg-purple-600',
    quote: 'Managing overnight shifts and accurate GPS geofencing solved our workforce punctuality challenges once and for all.',
  },
];

export function TestimonialsSection({ onOpenDemoModal: _onOpenDemoModal }: TestimonialsSectionProps) {
  return (
    <section className="w-full bg-white py-16 sm:py-24">
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="text-center max-w-2xl mx-auto mb-16">
          <span className="text-xs font-bold tracking-wider text-blue-600 uppercase">
            CUSTOMER TESTIMONIALS
          </span>
          <h2 className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight mt-2">
            What Business Leaders Say About Quorum
          </h2>
          <p className="text-slate-600 text-sm sm:text-base mt-3 leading-relaxed">
            Real feedback from HR leaders, operations directors, and finance executives who trust Quorum.
          </p>
        </div>

        {/* 3x3 Testimonial Grid (9 Cards) */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {TESTIMONIALS.map((t, idx) => (
            <div
              key={idx}
              className="bg-white p-6 rounded-2xl border border-slate-200/80 shadow-xs hover:shadow-md transition-all flex flex-col justify-between"
            >
              <div className="space-y-4">
                {/* User Header */}
                <div className="flex items-center gap-3">
                  <div
                    className={`h-10 w-10 rounded-full ${t.color} text-white font-bold text-xs flex items-center justify-center shrink-0`}
                  >
                    {t.initials}
                  </div>
                  <div className="min-w-0">
                    <h3 className="text-sm font-bold text-slate-900 truncate">{t.name}</h3>
                    <p className="text-[11px] text-slate-500 truncate">
                      {t.role} • {t.company}
                    </p>
                  </div>
                </div>

                {/* Quote Text */}
                <p className="text-xs sm:text-sm text-slate-600 leading-relaxed italic">
                  "{t.quote}"
                </p>
              </div>

              {/* 5 Gold Stars Rating */}
              <div className="mt-5 pt-3 border-t border-slate-100 flex items-center gap-1 text-amber-500 text-xs font-bold">
                ★★★★★
                <span className="text-[11px] text-slate-400 font-normal ml-1">5.0 / 5.0</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
