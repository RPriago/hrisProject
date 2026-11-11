import { ArrowRight } from 'lucide-react';

export interface HeroSectionProps {
  onOpenDemoModal: () => void;
}

export function HeroSection({ onOpenDemoModal }: HeroSectionProps) {
  return (
    <section className="relative w-full bg-white overflow-hidden pt-16 sm:pt-20 lg:pt-28 pb-16 sm:pb-24">
      {/* Hero Headline & Value Proposition */}
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8 flex flex-col items-center justify-center text-center">
        {/* Hero Title */}
        <h1 className="text-4xl sm:text-5xl lg:text-6xl font-extrabold text-slate-900 tracking-tight leading-[1.18] max-w-6xl mx-auto text-center">
          Streamline HR & Payroll Operations with a{' '}
          <span className="block mt-1 sm:mt-2 text-blue-600">
            Smart Dashboard
          </span>
        </h1>

        {/* Hero Subtitle */}
        <p className="mt-6 text-base sm:text-lg text-slate-600 max-w-2xl mx-auto leading-relaxed text-center">
          One unified dashboard for GPS attendance, dynamic shifts, automated tax and statutory calculations, and self-service employee requests without administrative complexity.
        </p>

        {/* CTA Buttons Row */}
        <div className="mt-8 flex flex-col sm:flex-row items-center justify-center gap-4">
          <button
            type="button"
            onClick={onOpenDemoModal}
            className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-7 py-3.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-base font-semibold shadow-md shadow-blue-500/25 transition-all hover:shadow-lg"
          >
            <span>Start Interactive Demo</span>
            <ArrowRight className="h-4 w-4" />
          </button>
          <a
            href="#features"
            className="w-full sm:w-auto inline-flex items-center justify-center px-7 py-3.5 rounded-xl bg-white hover:bg-slate-50 text-slate-700 text-base font-semibold border border-slate-200 transition-colors"
          >
            Explore Features
          </a>
        </div>

        {/* Social Proof Strip */}
        <div className="mt-6 flex items-center justify-center gap-3 text-xs sm:text-sm text-slate-500">
          <div className="flex -space-x-2 overflow-hidden">
            <span className="inline-block h-7 w-7 rounded-full ring-2 ring-white bg-blue-500 text-white font-bold text-[10px] flex items-center justify-center">
              AD
            </span>
            <span className="inline-block h-7 w-7 rounded-full ring-2 ring-white bg-violet-500 text-white font-bold text-[10px] flex items-center justify-center">
              BW
            </span>
            <span className="inline-block h-7 w-7 rounded-full ring-2 ring-white bg-emerald-500 text-white font-bold text-[10px] flex items-center justify-center">
              SW
            </span>
            <span className="inline-block h-7 w-7 rounded-full ring-2 ring-white bg-amber-500 text-white font-bold text-[10px] flex items-center justify-center">
              HK
            </span>
          </div>
          <div className="flex items-center gap-1 text-amber-500 font-semibold">
            ★★★★★
          </div>
          <span>Trusted by 500+ modern companies</span>
        </div>
      </div>

      {/* Hero Floating Dashboard Showcase (Authentic Live Footage in Layered Browser Deck) */}
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8 mt-12 sm:mt-16">
        {/* Layered Card Deck Effect Behind Showcase */}
        <div className="relative max-w-6xl mx-auto">
          {/* Back Layer 1 */}
          <div
            aria-hidden="true"
            className="absolute -top-6 left-10 right-10 h-10 bg-white/60 border border-slate-200/60 rounded-2xl sm:rounded-3xl shadow-xs hidden sm:flex items-center px-6 gap-3 pointer-events-none opacity-60"
          >
            <div className="h-3.5 w-3.5 rounded-full bg-slate-300" />
            <div className="h-2 w-24 rounded-full bg-slate-200" />
            <div className="h-2 w-16 rounded-full bg-slate-100 ml-auto" />
          </div>

          {/* Back Layer 2 */}
          <div
            aria-hidden="true"
            className="absolute -top-3 left-5 right-5 h-8 bg-white/80 border border-slate-200/70 rounded-2xl sm:rounded-3xl shadow-xs hidden sm:flex items-center px-6 gap-3 pointer-events-none opacity-80"
          >
            <div className="h-3.5 w-3.5 rounded-full bg-slate-300" />
            <div className="h-2 w-28 rounded-full bg-slate-200" />
            <div className="h-2 w-20 rounded-full bg-slate-100 ml-auto" />
          </div>

          {/* Main Browser Frame + Authentic Quorum Dashboard Footage */}
          <div className="relative rounded-2xl sm:rounded-3xl shadow-2xl border border-slate-200/80 bg-white overflow-hidden [mask-image:linear-gradient(to_bottom,black_88%,transparent_100%)] [-webkit-mask-image:linear-gradient(to_bottom,black_88%,transparent_100%)]">
            {/* Minimal Browser Top Chrome */}
            <div className="px-3.5 sm:px-6 py-2.5 sm:py-3 border-b border-slate-100 bg-white flex items-center justify-between gap-3">
              <div className="flex items-center gap-1.5 sm:gap-2" aria-hidden="true">
                <span className="h-2.5 w-2.5 sm:h-3 sm:w-3 rounded-full bg-rose-400" />
                <span className="h-2.5 w-2.5 sm:h-3 sm:w-3 rounded-full bg-amber-400" />
                <span className="h-2.5 w-2.5 sm:h-3 sm:w-3 rounded-full bg-emerald-400" />
              </div>
              <div className="px-3 sm:px-4 py-0.5 sm:py-1 rounded-full bg-slate-100 border border-slate-200/70 text-[10px] sm:text-xs font-medium text-slate-600 tracking-tight truncate max-w-[210px] sm:max-w-xs">
                https://app.quorum.com/dashboard
              </div>
              <div className="w-10 sm:w-12 hidden sm:block" aria-hidden="true" />
            </div>

            {/* Real Quorum Dashboard Footage (3K Retina HD AVIF + WebP Fallback) */}
            <div className="relative bg-white">
              <picture className="block w-full">
                <source srcSet="/images/hero/hero-dashboard.avif" type="image/avif" />
                <img
                  src="/images/hero/hero-dashboard.webp"
                  alt="Quorum Smart HR & Payroll Dashboard"
                  width={2940}
                  height={1725}
                  className="w-full h-auto aspect-[2940/1725] block"
                  loading="eager"
                  decoding="sync"
                />
              </picture>
              {/* Subtle Bottom Edge Blend Overlay */}
              <div className="absolute inset-x-0 bottom-0 h-14 sm:h-20 bg-gradient-to-t from-white via-white/75 to-transparent pointer-events-none" />
            </div>
          </div>
        </div>

        {/* Four Key Metrics Bar Under Showcase (Floating naturally without dividing line) */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-6 pt-6 sm:pt-8 pb-10 text-center">
          <div>
            <div className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight">
              +30%
            </div>
            <p className="text-xs sm:text-sm text-slate-500 mt-1">Operational Efficiency</p>
          </div>
          <div>
            <div className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight">
              98%
            </div>
            <p className="text-xs sm:text-sm text-slate-500 mt-1">Customer Satisfaction</p>
          </div>
          <div>
            <div className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight">
              10k+
            </div>
            <p className="text-xs sm:text-sm text-slate-500 mt-1">Active Employees Managed</p>
          </div>
          <div>
            <div className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight">
              100%
            </div>
            <p className="text-xs sm:text-sm text-slate-500 mt-1">Statutory & Tax Compliance</p>
          </div>
        </div>
      </div>
    </section>
  );
}
