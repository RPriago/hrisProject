export interface ZigZagFeaturesSectionProps {
  onOpenDemoModal: () => void;
}

interface FeaturePreviewImageProps {
  name: string;
  alt: string;
  width: number;
  height: number;
}

/** Renders 3x HiDPI AVIF/WebP preview frames with explicit intrinsic bounds to avoid CLS and blur. */
function FeaturePreviewImage({ name, alt, width, height }: FeaturePreviewImageProps) {
  return (
    <div className="rounded-2xl border border-slate-200/80 shadow-md overflow-hidden bg-white">
      <picture className="block w-full">
        <source srcSet={`/images/features/${name}.avif`} type="image/avif" />
        <img
          src={`/images/features/${name}.webp`}
          alt={alt}
          width={width}
          height={height}
          className="w-full h-auto block"
          loading="lazy"
          decoding="async"
        />
      </picture>
    </div>
  );
}

export function ZigZagFeaturesSection({ onOpenDemoModal }: ZigZagFeaturesSectionProps) {
  return (
    <section id="features" className="w-full bg-slate-50/50 py-16 sm:py-24 border-y border-slate-150 scroll-mt-20">
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="text-center max-w-2xl mx-auto mb-16 sm:mb-20">
          <span className="text-xs font-bold tracking-wider text-blue-600 uppercase">
            EFFICIENT WORKFORCE FEATURES
          </span>
          <h2 className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight mt-2">
            Comprehensive HR & Payroll Capabilities
          </h2>
          <p className="text-slate-600 text-sm sm:text-base mt-3 leading-relaxed">
            Designed specifically to automate people operations, ensure regulatory compliance, and support multi-branch scaling.
          </p>
        </div>

        <div className="space-y-20 sm:space-y-28">
          {/* Row 1: Text Left ↔ Mockup Right */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-10 lg:gap-12 items-center">
            <div className="lg:col-span-5 space-y-4">
              <h3 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
                Track Real-Time Attendance & Team Performance
              </h3>
              <p className="text-slate-600 text-sm sm:text-base leading-relaxed">
                Gain complete visibility into employee discipline and workforce productivity across all branches instantly without manual roll calls.
              </p>
              <div className="pt-2">
                <button
                  type="button"
                  onClick={onOpenDemoModal}
                  className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold shadow-sm transition-all"
                >
                  <span>Learn More</span>
                </button>
              </div>
            </div>

            <div className="lg:col-span-7">
              <FeaturePreviewImage
                name="feature-attendance"
                alt="Today's Attendance Summary"
                width={2340}
                height={1128}
              />
            </div>
          </div>

          {/* Row 2: Mockup Left ↔ Text Right */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-10 lg:gap-12 items-center">
            <div className="lg:col-span-7 order-2 lg:order-1">
              <FeaturePreviewImage
                name="feature-shifts"
                alt="Weekly Shift Schedule"
                width={2340}
                height={1101}
              />
            </div>

            <div className="lg:col-span-5 space-y-4 order-1 lg:order-2">
              <h3 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
                Manage Shift Schedules, Leave & Overtime Without Clashes
              </h3>
              <p className="text-slate-600 text-sm sm:text-base leading-relaxed">
                Configure overnight rotating shifts, collective company leave, and streamlined overtime approvals with automatic compensation math.
              </p>
              <div className="pt-2">
                <button
                  type="button"
                  onClick={onOpenDemoModal}
                  className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold shadow-sm transition-all"
                >
                  <span>Learn More</span>
                </button>
              </div>
            </div>
          </div>

          {/* Row 3: Text Left ↔ Mockup Right */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-10 lg:gap-12 items-center">
            <div className="lg:col-span-5 space-y-4">
              <h3 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
                Centralized Employee Records & Organization Structure
              </h3>
              <p className="text-slate-600 text-sm sm:text-base leading-relaxed">
                Maintain employment contracts, digital documents, management reporting lines, and branch assignments securely.
              </p>
              <div className="pt-2">
                <button
                  type="button"
                  onClick={onOpenDemoModal}
                  className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold shadow-sm transition-all"
                >
                  <span>Learn More</span>
                </button>
              </div>
            </div>

            <div className="lg:col-span-7">
              <FeaturePreviewImage
                name="feature-directory"
                alt="Employee Directory & Organization Profiles"
                width={2340}
                height={906}
              />
            </div>
          </div>

          {/* Row 4: Mockup Left ↔ Text Right */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-10 lg:gap-12 items-center">
            <div className="lg:col-span-7 order-2 lg:order-1">
              <FeaturePreviewImage
                name="feature-payroll"
                alt="Digital Payslip Preview"
                width={2340}
                height={918}
              />
            </div>

            <div className="lg:col-span-5 space-y-4 order-1 lg:order-2">
              <h3 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
                Faster Payroll Processing, Digital Payslips & Claims
              </h3>
              <p className="text-slate-600 text-sm sm:text-base leading-relaxed">
                Process payroll for thousands of employees in minutes, automatically delivered to private digital payslips without risk of data leaks.
              </p>
              <div className="pt-2">
                <button
                  type="button"
                  onClick={onOpenDemoModal}
                  className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold shadow-sm transition-all"
                >
                  <span>Learn More</span>
                </button>
              </div>
            </div>
          </div>

          {/* Row 5: Text Left ↔ Mockup Right */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-10 lg:gap-12 items-center">
            <div className="lg:col-span-5 space-y-4">
              <h3 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
                Ready-to-Use Workforce Analytics & Expense Reports
              </h3>
              <p className="text-slate-600 text-sm sm:text-base leading-relaxed">
                Monitor labor cost efficiency, monthly attendance trends, and retention rates to support executive decision-making.
              </p>
              <div className="pt-2">
                <button
                  type="button"
                  onClick={onOpenDemoModal}
                  className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold shadow-sm transition-all"
                >
                  <span>Learn More</span>
                </button>
              </div>
            </div>

            <div className="lg:col-span-7">
              <FeaturePreviewImage
                name="feature-analytics"
                alt="Workforce Analytics & Cost Efficiency"
                width={2340}
                height={1095}
              />
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
