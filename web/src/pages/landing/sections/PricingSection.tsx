import { useState, useRef, useEffect } from 'react';
import { Check, Receipt, Sliders, Building2 } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface PricingSectionProps {
  onOpenDemoModal: () => void;
}

export type PricingTier = 'basic' | 'pro' | 'enterprise';

export function PricingSection({ onOpenDemoModal }: PricingSectionProps) {
  const [billingPeriod, setBillingPeriod] = useState<'monthly' | 'annually'>('monthly');
  const [activeCard, setActiveCard] = useState<PricingTier>('pro');
  const sectionRef = useRef<HTMLElement>(null);

  const isMonthly = billingPeriod === 'monthly';

  useEffect(() => {
    const element = sectionRef.current;
    if (!element || typeof IntersectionObserver === 'undefined') return;

    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          // When the pricing section completely leaves the viewport (scrolled out of view in either direction)
          if (!entry.isIntersecting) {
            setActiveCard('pro');
          }
        });
      },
      { threshold: 0 }
    );

    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return (
    <section
      ref={sectionRef}
      id="pricing"
      className="w-full bg-white py-16 sm:py-24 border-t border-slate-150 scroll-mt-20"
    >
      <div className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="text-center max-w-2xl mx-auto mb-12 sm:mb-16">
          <h2 className="text-3xl sm:text-4xl lg:text-5xl font-extrabold text-slate-900 tracking-tight">
            Flexible Pricing Plans
          </h2>
          <p className="text-slate-600 text-sm sm:text-base mt-3 leading-relaxed">
            Choose the perfect plan for your business—whether you're just starting or managing multiple branches.
          </p>

          {/* Toggle Switch Pill (Selaras Biru Modern) */}
          <div className="mt-8 inline-flex items-center p-1 rounded-full bg-slate-100 border border-slate-200 text-xs sm:text-sm font-semibold shadow-xs">
            <button
              type="button"
              onClick={() => setBillingPeriod('monthly')}
              className={`px-5 py-2 rounded-full transition-all ${isMonthly
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-slate-600 hover:text-slate-900'
                }`}
            >
              Monthly Plan
            </button>
            <button
              type="button"
              onClick={() => setBillingPeriod('annually')}
              className={`px-5 py-2 rounded-full transition-all ${!isMonthly
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-slate-600 hover:text-slate-900'
                }`}
            >
              Yearly plan
            </button>
          </div>
        </div>

        {/* 3 Pricing Cards Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 sm:gap-8 items-stretch">
          {/* Card 1: Basic Plan */}
          <div
            onClick={() => setActiveCard('basic')}
            onMouseEnter={() => setActiveCard('basic')}
            className={cn(
              'bg-white p-6 sm:p-8 rounded-2xl sm:rounded-3xl transition-all duration-300 flex flex-col justify-between cursor-pointer',
              activeCard === 'basic'
                ? 'border-2 border-blue-600 shadow-lg shadow-blue-500/10'
                : 'border-2 border-slate-200/80 shadow-xs hover:border-slate-300'
            )}
          >
            <div>
              {/* Icon Top Left */}
              <div className="h-10 w-10 rounded-xl bg-slate-50 border border-slate-200 text-slate-600 flex items-center justify-center mb-5">
                <Receipt className="h-5 w-5" />
              </div>

              <h3 className="text-xl font-bold text-slate-900">Basic Plan</h3>
              <p className="text-xs sm:text-sm text-slate-500 mt-1">
                Essential tools for small and growing teams.
              </p>

              {/* Price */}
              <div className="mt-6 mb-6">
                <div className="flex items-baseline gap-1">
                  <span className="text-3xl sm:text-4xl font-extrabold text-slate-900">
                    {isMonthly ? 'Rp 20.000' : 'Rp 16.000'}
                  </span>
                  <span className="text-xs text-slate-500 font-medium">/employee/mo</span>
                </div>
              </div>

              <div className="border-t border-slate-200/80 my-6" />

              {/* Checklist */}
              <div className="space-y-3.5 text-xs sm:text-sm text-slate-700">
                {[
                  'GPS online attendance & shift scheduling',
                  'Self-service leave & overtime requests (ESS)',
                  'Digital employee record management',
                  'Automated digital payslips',
                  'Standard email & chat technical support',
                ].map((feat, idx) => (
                  <div key={idx} className="flex items-center gap-3">
                    <Check className="h-4 w-4 text-blue-500 shrink-0" />
                    <span>{feat}</span>
                  </div>
                ))}
              </div>
            </div>

            {/* Bottom Button (Soft Light Blue like reference) */}
            <div className="mt-8 pt-4">
              <button
                type="button"
                onClick={onOpenDemoModal}
                className="w-full py-3 px-4 rounded-xl bg-blue-50 hover:bg-blue-100 text-blue-600 font-semibold text-xs sm:text-sm transition-colors"
              >
                Get Started
              </button>
            </div>
          </div>

          {/* Card 2: Pro Plan (Featured with Persistent Corner Best Seller Tag & Default Active Border) */}
          <div
            onClick={() => setActiveCard('pro')}
            onMouseEnter={() => setActiveCard('pro')}
            className={cn(
              'bg-white p-6 sm:p-8 rounded-2xl sm:rounded-3xl transition-all duration-300 flex flex-col justify-between cursor-pointer relative',
              activeCard === 'pro'
                ? 'border-2 border-blue-600 shadow-lg shadow-blue-500/10'
                : 'border-2 border-slate-200/80 shadow-xs hover:border-slate-300'
            )}
          >
            {/* Top-Right Attached Tag: flush against inside edge of border without overlapping */}
            <div className="absolute top-0 right-0 bg-blue-600 text-white text-[11px] sm:text-xs font-bold px-3.5 sm:px-4 py-1 sm:py-1.5 rounded-tr-2xl sm:rounded-tr-[21.3px] rounded-bl-2xl tracking-wide shadow-xs">
              Best Seller
            </div>

            <div>
              {/* Icon Top Left */}
              <div className="h-10 w-10 rounded-xl bg-blue-600 text-white flex items-center justify-center mb-5 shadow-sm shadow-blue-500/25">
                <Sliders className="h-5 w-5" />
              </div>

              <h3 className="text-xl font-bold text-slate-900">Pro Plan</h3>
              <p className="text-xs sm:text-sm text-slate-500 mt-1">
                Full automation for growing teams and companies.
              </p>

              {/* Price */}
              <div className="mt-6 mb-6">
                <div className="flex items-baseline gap-1">
                  <span className="text-3xl sm:text-4xl font-extrabold text-slate-900">
                    {isMonthly ? 'Rp 39.000' : 'Rp 31.000'}
                  </span>
                  <span className="text-xs text-slate-500 font-medium">/employee/mo</span>
                </div>
              </div>

              <div className="border-t border-slate-200/80 my-6" />

              {/* Checklist */}
              <div className="space-y-3.5 text-xs sm:text-sm text-slate-700">
                {[
                  'All capabilities included in Basic',
                  'Automated tax & withholding calculations',
                  'Statutory social security & health deductions',
                  'Multi-branch & org structure hierarchy',
                  '24/7 priority technical support',
                ].map((feat, idx) => (
                  <div key={idx} className="flex items-center gap-3">
                    <Check className="h-4 w-4 text-blue-500 shrink-0" />
                    <span>{feat}</span>
                  </div>
                ))}
              </div>
            </div>

            {/* Bottom Button (Solid Blue like reference) */}
            <div className="mt-8 pt-4">
              <button
                type="button"
                onClick={onOpenDemoModal}
                className="w-full py-3 px-4 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs sm:text-sm shadow-md shadow-blue-500/25 transition-all"
              >
                Contact us
              </button>
            </div>
          </div>

          {/* Card 3: Enterprise Plan */}
          <div
            onMouseEnter={() => setActiveCard('enterprise')}
            className={cn(
              'bg-white p-6 sm:p-8 rounded-2xl sm:rounded-3xl transition-all duration-300 flex flex-col justify-between cursor-pointer',
              activeCard === 'enterprise'
                ? 'border-2 border-blue-600 shadow-lg shadow-blue-500/10'
                : 'border-2 border-slate-200/80 shadow-xs hover:border-slate-300'
            )}
          >
            <div>
              {/* Icon Top Left */}
              <div className="h-10 w-10 rounded-xl bg-slate-50 border border-slate-200 text-slate-600 flex items-center justify-center mb-5">
                <Building2 className="h-5 w-5" />
              </div>

              <h3 className="text-xl font-bold text-slate-900">Enterprise Plan</h3>
              <p className="text-xs sm:text-sm text-slate-500 mt-1">
                Full control for large enterprises & multi-entity groups.
              </p>

              {/* Price */}
              <div className="mt-6 mb-6">
                <div className="flex items-baseline gap-1">
                  <span className="text-3xl sm:text-4xl font-extrabold text-slate-900">
                    {isMonthly ? 'Rp 65.000' : 'Rp 52.000'}
                  </span>
                  <span className="text-xs text-slate-500 font-medium">/employee/mo</span>
                </div>
              </div>

              <div className="border-t border-slate-200/80 my-6" />

              {/* Checklist */}
              <div className="space-y-3.5 text-xs sm:text-sm text-slate-700">
                {[
                  'All capabilities included in Pro',
                  'Isolated multi-company data architecture',
                  'Custom payroll formulas & allowance rules',
                  'Integration API & compliance audit trail',
                  'Dedicated account manager & 99.9% SLA',
                ].map((feat, idx) => (
                  <div key={idx} className="flex items-center gap-3">
                    <Check className="h-4 w-4 text-blue-500 shrink-0" />
                    <span>{feat}</span>
                  </div>
                ))}
              </div>
            </div>

            {/* Bottom Button (Soft Light Blue like reference) */}
            <div className="mt-8 pt-4">
              <button
                type="button"
                onClick={onOpenDemoModal}
                className="w-full py-3 px-4 rounded-xl bg-blue-50 hover:bg-blue-100 text-blue-600 font-semibold text-xs sm:text-sm transition-colors"
              >
                Get Started
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
