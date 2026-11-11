import { ArrowRight } from 'lucide-react';

export interface IntegrationsSectionProps {
  onOpenDemoModal: () => void;
}

export function IntegrationsSection({ onOpenDemoModal }: IntegrationsSectionProps) {
  return (
    <section className="w-full bg-white py-16 sm:py-24 overflow-hidden">
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-12 lg:gap-16 items-center">
          {/* Left Column: Organic Floating Cards (No enclosing container) */}
          <div className="lg:col-span-7 flex justify-center">
            <div className="grid grid-cols-5 gap-2 sm:gap-3.5 items-start w-full max-w-[580px]">
              
              {/* Column 1 (Leftmost, shifted down) */}
              <div className="flex flex-col gap-2 sm:gap-3.5 pt-10 sm:pt-20">
                {/* White Card: Bank Mandiri */}
                <div className="bg-white rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_8px_30px_rgb(0,0,0,0.06)] border border-slate-100 flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/mandiri.svg" alt="Bank Mandiri Logo" className="max-h-5 sm:max-h-7 max-w-full object-contain" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-slate-800 leading-tight">Bank Mandiri</span>
                </div>
              </div>

              {/* Column 2 */}
              <div className="flex flex-col gap-2 sm:gap-3.5 pt-0 sm:pt-2">
                {/* Sage Green Accent Card: Social Security (BPJS Ketenagakerjaan) */}
                <div className="bg-[#2e7d32] rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_10px_25px_rgba(46,125,50,0.28)] flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/bpjs-tk.svg" alt="Social Security (BPJS Ketenagakerjaan) Logo" className="max-h-4 sm:max-h-6 max-w-full object-contain brightness-0 invert" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-white leading-tight">Social Security</span>
                </div>

                {/* White Card: Bank BNI */}
                <div className="bg-white rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_8px_30px_rgb(0,0,0,0.06)] border border-slate-100 flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/bni.svg" alt="Bank BNI Logo" className="max-h-5 sm:max-h-7 max-w-full object-contain" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-slate-800 leading-tight">Bank BNI</span>
                </div>

                {/* White Card: WhatsApp */}
                <div className="bg-white rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_8px_30px_rgb(0,0,0,0.06)] border border-slate-100 flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/whatsapp.svg" alt="WhatsApp Logo" className="max-h-6 sm:max-h-8 max-w-full object-contain" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-slate-800 leading-tight">WhatsApp</span>
                </div>
              </div>

              {/* Column 3 (Center) */}
              <div className="flex flex-col gap-2 sm:gap-3.5 pt-4 sm:pt-8">
                {/* White Card: Microsoft Excel */}
                <div className="bg-white rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_8px_30px_rgb(0,0,0,0.06)] border border-slate-100 flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/excel.svg" alt="Microsoft Excel Logo" className="max-h-6 sm:max-h-8 max-w-full object-contain" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-slate-800 leading-tight">Microsoft Excel</span>
                </div>

                {/* Featured Vibrant Blue Card: BCA Corporate (Center anchor) */}
                <div className="bg-blue-600 rounded-2xl sm:rounded-[26px] p-2.5 sm:p-4 shadow-[0_12px_32px_rgba(37,99,235,0.32)] flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-2xl transition-all duration-300 min-h-[88px] sm:min-h-[135px] group">
                  <div className="h-7 w-7 sm:h-12 sm:w-12 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/bca.svg" alt="BCA Corporate Logo" className="max-h-5 sm:max-h-7 max-w-full object-contain brightness-0 invert" />
                  </div>
                  <span className="text-[10px] sm:text-sm font-bold text-white tracking-wide leading-tight">BCA Corporate</span>
                </div>
              </div>

              {/* Column 4 */}
              <div className="flex flex-col gap-2 sm:gap-3.5 pt-0 sm:pt-2">
                {/* Vibrant Royal Blue Accent Card: Bank BRI */}
                <div className="bg-blue-700 rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_10px_25px_rgba(29,78,216,0.28)] flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/bri.svg" alt="Bank BRI Logo" className="max-h-4 sm:max-h-6 max-w-full object-contain brightness-0 invert" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-white leading-tight">Bank BRI</span>
                </div>

                {/* White Card: Slack */}
                <div className="bg-white rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_8px_30px_rgb(0,0,0,0.06)] border border-slate-100 flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/slack.svg" alt="Slack Logo" className="max-h-6 sm:max-h-8 max-w-full object-contain" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-slate-800 leading-tight">Slack</span>
                </div>

                {/* White Card: Tax Authority */}
                <div className="bg-white rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_8px_30px_rgb(0,0,0,0.06)] border border-slate-100 flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/pajak.svg" alt="Tax Authority Logo" className="max-h-6 sm:max-h-8 max-w-full object-contain" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-slate-800 leading-tight">Tax Authority</span>
                </div>
              </div>

              {/* Column 5 (Rightmost, shifted down) */}
              <div className="flex flex-col gap-2 sm:gap-3.5 pt-8 sm:pt-16">
                {/* White Card: Google Workspace */}
                <div className="bg-white rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_8px_30px_rgb(0,0,0,0.06)] border border-slate-100 flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/google.svg" alt="Google Workspace Logo" className="max-h-6 sm:max-h-8 max-w-full object-contain" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-slate-800 leading-tight">Google Workspace</span>
                </div>

                {/* Accent Green Card: Healthcare Fund (BPJS Kesehatan) */}
                <div className="bg-[#15803d] rounded-2xl sm:rounded-[24px] p-2 sm:p-4 shadow-[0_10px_25px_rgba(21,128,61,0.28)] flex flex-col items-center justify-center text-center hover:-translate-y-1.5 hover:shadow-xl transition-all duration-300 min-h-[82px] sm:min-h-[125px] group">
                  <div className="h-6 w-6 sm:h-11 sm:w-11 flex items-center justify-center mb-1 sm:mb-2 group-hover:scale-105 transition-transform">
                    <img src="/logos/bpjs-kes.svg" alt="Healthcare Fund (BPJS Kesehatan) Logo" className="max-h-4 sm:max-h-6 max-w-full object-contain brightness-0 invert" />
                  </div>
                  <span className="text-[9px] sm:text-xs font-semibold text-white leading-tight">Healthcare Fund</span>
                </div>
              </div>

            </div>
          </div>

          {/* Right Column: Copy & Action */}
          <div className="lg:col-span-5 space-y-5 text-center lg:text-left">
            <span className="text-xs font-bold tracking-wider text-blue-600 uppercase">
              INTEGRATION ECOSYSTEM
            </span>
            <h2 className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight leading-tight">
              Seamless Integration into a Smarter Workflow
            </h2>
            <p className="text-slate-600 text-sm sm:text-base leading-relaxed">
              Connect Quorum directly with major corporate banking networks, statutory compliance portals, and everyday workplace collaboration tools.
            </p>
            <div className="pt-2">
              <button
                type="button"
                onClick={onOpenDemoModal}
                className="inline-flex items-center gap-2 px-6 py-3 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-sm shadow-sm transition-all"
              >
                <span>Explore All Integrations</span>
                <ArrowRight className="h-4 w-4" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
