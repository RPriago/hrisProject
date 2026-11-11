import { ArrowRight, Sparkles } from 'lucide-react';

export interface CtaBannerSectionProps {
  onOpenDemoModal: () => void;
}

export function CtaBannerSection({ onOpenDemoModal }: CtaBannerSectionProps) {
  return (
    <section className="w-full bg-white py-12 sm:py-16">
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Curved Pre-Footer Banner Card */}
        <div className="bg-slate-900 text-white rounded-3xl p-8 sm:p-14 relative overflow-hidden text-center shadow-xl">
          {/* Subtle Accent Glows */}
          <div className="absolute top-0 right-0 w-80 h-80 bg-blue-600/20 rounded-full blur-3xl pointer-events-none" />
          <div className="absolute bottom-0 left-0 w-80 h-80 bg-violet-600/20 rounded-full blur-3xl pointer-events-none" />

          <div className="relative z-10 max-w-3xl mx-auto space-y-6">
            <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-white/10 text-blue-300 text-xs font-semibold backdrop-blur-sm">
              <Sparkles className="h-3.5 w-3.5" />
              <span>Start Your Digital HR Transformation</span>
            </div>

            <h2 className="text-3xl sm:text-4xl lg:text-5xl font-extrabold tracking-tight leading-tight">
              Transform the Way You Run HR Operations Today
            </h2>

            <p className="text-sm sm:text-base text-slate-300 max-w-2xl mx-auto leading-relaxed">
              Improve workforce discipline, accelerate monthly payroll close, and experience effortless people management in one unified platform.
            </p>

            <div className="pt-2 flex flex-col sm:flex-row items-center justify-center gap-4">
              <button
                type="button"
                onClick={onOpenDemoModal}
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-7 py-3.5 rounded-xl bg-blue-600 hover:bg-blue-500 text-white text-base font-semibold shadow-md shadow-blue-600/30 transition-all"
              >
                <span>Start Free Trial</span>
                <ArrowRight className="h-4 w-4" />
              </button>
              <button
                type="button"
                onClick={onOpenDemoModal}
                className="w-full sm:w-auto inline-flex items-center justify-center px-7 py-3.5 rounded-xl bg-white/10 hover:bg-white/15 text-white text-base font-semibold border border-white/20 transition-all backdrop-blur-sm"
              >
                Schedule a Live Demo
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
