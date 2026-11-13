import { useState } from 'react';
import { useAuth, type DemoRole } from '@/context/AuthContext';
import { IS_DEMO_MODE } from '@/lib/env';
import { LandingPage } from '@/pages/landing/LandingPage';
import { AppShell } from '@/components/layout/AppShell';
import { DashboardPage } from '@/pages/workspace/DashboardPage';

export function App() {
  const { user, login } = useAuth();
  const [view, setView] = useState<'landing' | 'workspace'>('landing');

  const handleNavigateToWorkspace = (role: string) => {
    if (!IS_DEMO_MODE) return;
    login(role as DemoRole);
    setView('workspace');
  };

  const handleNavigateToLanding = () => {
    setView('landing');
  };

  if (view === 'workspace' && user) {
    return (
      <AppShell
        activePath="/dashboard"
        onNavigate={(path) => {
          if (path === '/') {
            handleNavigateToLanding();
          }
        }}
      >
        <DashboardPage />
      </AppShell>
    );
  }

  return <LandingPage onNavigateToWorkspace={handleNavigateToWorkspace} />;
}
