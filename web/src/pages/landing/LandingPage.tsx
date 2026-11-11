import { useState } from 'react';
import { SkipLink } from '@/components/ui/SkipLink';
import { Navbar } from '@/pages/landing/components/Navbar';
import { HeroSection } from '@/pages/landing/sections/HeroSection';
import { AdvantageSection } from '@/pages/landing/sections/AdvantageSection';
import { ZigZagFeaturesSection } from '@/pages/landing/sections/ZigZagFeaturesSection';
import { TrackingServicesSection } from '@/pages/landing/sections/TrackingServicesSection';
import { AudienceSection } from '@/pages/landing/sections/AudienceSection';
import { PricingSection } from '@/pages/landing/sections/PricingSection';
import { IntegrationsSection } from '@/pages/landing/sections/IntegrationsSection';
import { FaqSection } from '@/pages/landing/sections/FaqSection';
import { TestimonialsSection } from '@/pages/landing/sections/TestimonialsSection';
import { CtaBannerSection } from '@/pages/landing/sections/CtaBannerSection';
import { FooterSection } from '@/pages/landing/sections/FooterSection';
import { RoleDemoModal } from '@/pages/landing/components/RoleDemoModal';

export interface LandingPageProps {
  onNavigateToWorkspace?: (role: string) => void;
}

export function LandingPage({ onNavigateToWorkspace }: LandingPageProps) {
  const [isDemoModalOpen, setIsDemoModalOpen] = useState(false);
  const [selectedDemoRole, setSelectedDemoRole] = useState<string | undefined>(undefined);

  const handleOpenDemoModal = (role?: string) => {
    setSelectedDemoRole(role);
    setIsDemoModalOpen(true);
  };

  const handleSelectRole = (role: string) => {
    setIsDemoModalOpen(false);
    onNavigateToWorkspace?.(role);
  };

  return (
    <div className="min-h-screen bg-white text-slate-900 flex flex-col antialiased selection:bg-blue-100 selection:text-blue-900 font-sans">
      {/* 0. Accessible Skip Link */}
      <SkipLink targetId="main-content" label="Skip to main content" />

      {/* 1. Sticky Navbar Across the Entire Page */}
      <Navbar onOpenDemoModal={() => handleOpenDemoModal()} />

      {/* Main Content Landmark */}
      <main id="main-content" className="flex-1 w-full">
        {/* Section 1: Hero Section (Headline, Showcase Mockup & 4 Stats) */}
        <HeroSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 2: Core Advantages (3 Cards) */}
        <AdvantageSection />

        {/* Section 3: Feature Highlights (5 Alternating Rows + UI Mockups) */}
        <ZigZagFeaturesSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 4: Integrated Tracking Services (3 Bento Cards) */}
        <TrackingServicesSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 5: Target Audience (Light Blue Container + 2x2 Grid) */}
        <AudienceSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 6: Flexible Pricing Plans (Monthly/Yearly Toggle + 3 Tier Cards) */}
        <PricingSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 7: Ecosystem Integrations (3x3 App Tiles Mosaic) */}
        <IntegrationsSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 8: Frequently Asked Questions (Interactive Accordion) */}
        <FaqSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 9: Customer Testimonials (3x3 Grid of 9 Verified Reviews) */}
        <TestimonialsSection onOpenDemoModal={() => handleOpenDemoModal()} />

        {/* Section 10: Curved Pre-Footer Banner (High Impact CTA) */}
        <CtaBannerSection onOpenDemoModal={() => handleOpenDemoModal()} />
      </main>

      {/* Section 11: 4-Column SaaS Footer */}
      <FooterSection onOpenDemoModal={() => handleOpenDemoModal()} />

      {/* Interactive Ready-to-Use Demo Role Modal (7 Roles) */}
      <RoleDemoModal
        isOpen={isDemoModalOpen}
        onClose={() => setIsDemoModalOpen(false)}
        onSelectRole={handleSelectRole}
        initialRole={selectedDemoRole}
      />
    </div>
  );
}
