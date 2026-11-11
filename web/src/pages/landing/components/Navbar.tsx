import { useState } from 'react';
import { Menu, X } from 'lucide-react';
import { QuorumLogo } from '@/components/brand/QuorumLogo';

export interface NavbarProps {
  onOpenDemoModal: () => void;
}

export function Navbar({ onOpenDemoModal }: NavbarProps) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  return (
    <header className="sticky top-0 z-50 w-full bg-white/95 backdrop-blur-md border-b border-slate-150 shadow-xs transition-all">
      <nav
        className="max-w-screen-2xl mx-auto px-4 sm:px-6 lg:px-8 flex items-center justify-between h-16 sm:h-20"
        aria-label="Quorum Main Navigation"
      >
        {/* Logo Brand */}
        <div className="flex items-center">
          <QuorumLogo className="h-9 sm:h-11 text-[#0F172A]" />
        </div>

        {/* Desktop Navigation Links */}
        <div className="hidden md:flex items-center gap-8 text-sm font-medium text-slate-600">
          <a href="#features" className="hover:text-blue-600 transition-colors">
            Features
          </a>
          <a href="#advantages" className="hover:text-blue-600 transition-colors">
            Advantages
          </a>
          <a href="#services" className="hover:text-blue-600 transition-colors">
            Services
          </a>
          <a href="#pricing" className="hover:text-blue-600 transition-colors">
            Pricing
          </a>
          <a href="#faq" className="hover:text-blue-600 transition-colors">
            FAQ
          </a>
        </div>

        {/* Right Action Buttons */}
        <div className="hidden sm:flex items-center gap-3">
          <button
            type="button"
            onClick={onOpenDemoModal}
            className="px-4 py-2 text-sm font-semibold text-slate-700 hover:text-blue-600 transition-colors"
          >
            Sign In
          </button>
          <button
            type="button"
            onClick={onOpenDemoModal}
            className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold rounded-xl bg-blue-600 hover:bg-blue-700 text-white shadow-md shadow-blue-500/25 transition-all hover:shadow-lg"
          >
            <span>Start a Demo</span>
          </button>
        </div>

        {/* Mobile Menu Toggle */}
        <div className="flex sm:hidden">
          <button
            type="button"
            onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
            className="p-2 rounded-lg text-slate-600 hover:text-slate-900 hover:bg-slate-100"
            aria-label="Toggle navigation menu"
          >
            {mobileMenuOpen ? <X className="h-6 w-6" /> : <Menu className="h-6 w-6" />}
          </button>
        </div>
      </nav>

      {/* Mobile Dropdown */}
      {mobileMenuOpen && (
        <div className="sm:hidden px-4 pt-2 pb-4 bg-white border-b border-slate-100 shadow-xl flex flex-col gap-3 animate-in fade-in slide-in-from-top-2">
          <a
            href="#features"
            onClick={() => setMobileMenuOpen(false)}
            className="text-sm font-medium text-slate-700 p-2 rounded-lg hover:bg-slate-50"
          >
            Features
          </a>
          <a
            href="#advantages"
            onClick={() => setMobileMenuOpen(false)}
            className="text-sm font-medium text-slate-700 p-2 rounded-lg hover:bg-slate-50"
          >
            Advantages
          </a>
          <a
            href="#services"
            onClick={() => setMobileMenuOpen(false)}
            className="text-sm font-medium text-slate-700 p-2 rounded-lg hover:bg-slate-50"
          >
            Services
          </a>
          <a
            href="#pricing"
            onClick={() => setMobileMenuOpen(false)}
            className="text-sm font-medium text-slate-700 p-2 rounded-lg hover:bg-slate-50"
          >
            Pricing
          </a>
          <a
            href="#faq"
            onClick={() => setMobileMenuOpen(false)}
            className="text-sm font-medium text-slate-700 p-2 rounded-lg hover:bg-slate-50"
          >
            FAQ
          </a>
          <div className="pt-3 border-t border-slate-100 flex flex-col gap-2">
            <button
              type="button"
              onClick={() => {
                setMobileMenuOpen(false);
                onOpenDemoModal();
              }}
              className="w-full py-2.5 text-center text-sm font-semibold rounded-xl bg-blue-600 text-white"
            >
              Book a Demo
            </button>
          </div>
        </div>
      )}
    </header>
  );
}
