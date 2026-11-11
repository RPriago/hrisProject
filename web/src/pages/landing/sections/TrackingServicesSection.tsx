import {
  ArrowRight,
  CheckCircle2,
} from 'lucide-react';

export interface TrackingServicesSectionProps {
  onOpenDemoModal: () => void;
}

function ServicePreviewImage({ name, alt }: { name: string; alt: string }) {
  return (
    <div className="aspect-[1320/576] rounded-xl overflow-hidden bg-white border border-slate-100">
      <picture className="block w-full h-full">
        <source srcSet={`/images/services/${name}.avif`} type="image/avif" />
        <img
          src={`/images/services/${name}.webp`}
          alt={alt}
          width={1320}
          height={576}
          className="w-full h-auto block"
          loading="lazy"
          decoding="async"
        />
      </picture>
    </div>
  );
}

export function TrackingServicesSection({ onOpenDemoModal }: TrackingServicesSectionProps) {
  return (
    <section id="services" className="w-full bg-white py-16 sm:py-24 scroll-mt-20">
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="text-center max-w-2xl mx-auto mb-16">
          <span className="text-xs font-bold tracking-wider text-blue-600 uppercase">
            INTEGRATED TRACKING SERVICES
          </span>
          <h2 className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight mt-2">
            A Unified System for Every Operational Need
          </h2>
          <p className="text-slate-600 text-sm sm:text-base mt-3 leading-relaxed">
            Monitor attendance, track payroll lifecycles, and measure workforce productivity in one interconnected platform.
          </p>
        </div>

        {/* 3 Bento Cards Grid */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 sm:gap-8">
          {/* Card 1 */}
          <div className="bg-white p-6 rounded-2xl border border-slate-200/80 shadow-sm hover:shadow-md transition-all flex flex-col justify-between">
            <div className="space-y-4">
              {/* Graphic visual header */}
              <ServicePreviewImage
                name="service-attendance"
                alt="Accurate Attendance Tracking Preview"
              />

              <h3 className="text-lg font-bold text-slate-900">
                Accurate Attendance Tracking
              </h3>
              <p className="text-xs sm:text-sm text-slate-600 leading-relaxed">
                GPS geofenced attendance and facial verification with anti-fraud protection and instant hours calculation.
              </p>
            </div>

            <div className="mt-6 pt-4 border-t border-slate-100 flex items-center justify-between text-xs font-semibold text-blue-600">
              <span>GPS Coordinates Validated</span>
              <CheckCircle2 className="h-4 w-4" />
            </div>
          </div>

          {/* Card 2 */}
          <div className="bg-white p-6 rounded-2xl border border-slate-200/80 shadow-sm hover:shadow-md transition-all flex flex-col justify-between">
            <div className="space-y-4">
              {/* Graphic visual header */}
              <ServicePreviewImage
                name="service-payroll"
                alt="Zero-Discrepancy Payroll Calculation Preview"
              />

              <h3 className="text-lg font-bold text-slate-900">
                Zero-Discrepancy Payroll Calculation
              </h3>
              <p className="text-xs sm:text-sm text-slate-600 leading-relaxed">
                Accurate calculation of base salary, allowances, tax withholdings, and statutory deductions down to the single cent.
              </p>
            </div>

            <div className="mt-6 pt-4 border-t border-slate-100 flex items-center justify-between text-xs font-semibold text-blue-600">
              <span>Accurate Tax & Deductions</span>
              <CheckCircle2 className="h-4 w-4" />
            </div>
          </div>

          {/* Card 3 */}
          <div className="bg-white p-6 rounded-2xl border border-slate-200/80 shadow-sm hover:shadow-md transition-all flex flex-col justify-between">
            <div className="space-y-4">
              {/* Graphic visual header */}
              <ServicePreviewImage
                name="service-evaluation"
                alt="Team Evaluation & Productivity Preview"
              />

              <h3 className="text-lg font-bold text-slate-900">
                Team Evaluation & Productivity
              </h3>
              <p className="text-xs sm:text-sm text-slate-600 leading-relaxed">
                Objective productivity metrics, performance reviews, and department goal tracking with transparent indicators.
              </p>
            </div>

            <div className="mt-6 pt-4 border-t border-slate-100 flex items-center justify-between text-xs font-semibold text-blue-600">
              <span>Centralized KPI Reporting</span>
              <CheckCircle2 className="h-4 w-4" />
            </div>
          </div>
        </div>

        {/* Center Blue Action Button */}
        <div className="mt-12 text-center">
          <button
            type="button"
            onClick={onOpenDemoModal}
            className="inline-flex items-center gap-2 px-6 py-3 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-sm shadow-sm transition-all"
          >
            <span>Explore All Services</span>
            <ArrowRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </section>
  );
}
