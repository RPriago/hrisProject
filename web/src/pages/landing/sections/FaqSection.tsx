import { useState } from 'react';
import { ChevronDown, ArrowRight } from 'lucide-react';

export interface FaqSectionProps {
  onOpenDemoModal: () => void;
}

interface FaqItem {
  question: string;
  answer: string;
}

const FAQS: FaqItem[] = [
  {
    question: 'How does Quorum handle automated tax and statutory calculations?',
    answer:
      'Quorum automatically computes official tax withholding brackets and statutory contributions with zero rounding discrepancies and seamless year-end reconciliations without manual spreadsheet errors.',
  },
  {
    question: 'Is employee and payroll data strictly isolated between subsidiary companies?',
    answer:
      'Yes. Every company entity operates within strict data isolation boundaries. Subsidiary administrators cannot view parent company or peer entity records without explicit multi-tenant authorization from group ownership.',
  },
  {
    question: 'How long does implementation and data migration from Excel take?',
    answer:
      'For mid-sized companies (50–300 employees), migrating existing spreadsheet records typically takes 1 to 3 business days using our guided templates and dedicated onboarding specialists.',
  },
  {
    question: 'Does Quorum support direct batch payment exports to banks?',
    answer:
      'Yes, Quorum exports standard corporate banking formats for major financial institutions, allowing finance teams to execute bulk salary payouts in seconds without manual entry.',
  },
];

export function FaqSection({ onOpenDemoModal }: FaqSectionProps) {
  const [openIndex, setOpenIndex] = useState<number | null>(0);

  const toggleAccordion = (index: number) => {
    setOpenIndex(openIndex === index ? null : index);
  };

  return (
    <section id="faq" className="w-full bg-slate-50/50 py-16 sm:py-24 border-t border-slate-150 scroll-mt-20">
      <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="text-center max-w-2xl mx-auto mb-12 sm:mb-16">
          <h2 className="text-3xl sm:text-4xl font-extrabold text-slate-900 tracking-tight">
            Frequently Asked Questions
          </h2>
          <p className="text-slate-600 text-sm sm:text-base mt-3 leading-relaxed">
            Clear answers to the most common questions asked by HR leaders and management teams.
          </p>
        </div>

        {/* Interactive Accordion List */}
        <div className="space-y-3.5">
          {FAQS.map((faq, index) => {
            const isOpen = openIndex === index;

            return (
              <div
                key={index}
                className="bg-white rounded-2xl border border-slate-200/80 shadow-xs overflow-hidden transition-all"
              >
                <button
                  type="button"
                  onClick={() => toggleAccordion(index)}
                  className="w-full p-5 sm:p-6 text-left flex items-center justify-between gap-4 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
                  aria-expanded={isOpen}
                >
                  <span className="text-sm sm:text-base font-bold text-slate-900">
                    {faq.question}
                  </span>
                  <ChevronDown
                    className={`h-5 w-5 text-slate-400 shrink-0 transition-transform duration-200 ${
                      isOpen ? 'rotate-180 text-blue-600' : ''
                    }`}
                  />
                </button>

                {isOpen && (
                  <div className="px-5 sm:px-6 pb-5 sm:pb-6 text-xs sm:text-sm text-slate-600 leading-relaxed border-t border-slate-100 pt-3 animate-in fade-in duration-150">
                    {faq.answer}
                  </div>
                )}
              </div>
            );
          })}
        </div>

        {/* Center Blue Action Button */}
        <div className="mt-12 text-center">
          <button
            type="button"
            onClick={onOpenDemoModal}
            className="inline-flex items-center gap-2 px-6 py-3 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-sm shadow-sm transition-all"
          >
            <span>Contact Support Team</span>
            <ArrowRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </section>
  );
}
