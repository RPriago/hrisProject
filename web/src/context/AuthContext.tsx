import React, { createContext, useContext, useState, useEffect } from 'react';
import { wipeTenantCache } from '@/lib/queryClient';
import { IS_DEMO_MODE } from '@/lib/env';
import { apiClient } from '@/lib/api/apiClient';

export type DemoRole =
  | 'PlatformAdmin'
  | 'CompanyOwner'
  | 'HrAdmin'
  | 'PayrollOfficer'
  | 'Manager'
  | 'Employee'
  | 'Auditor';

export interface UserProfile {
  id: string;
  name: string;
  email: string;
  role: DemoRole;
  dataScope: 'Self' | 'DirectReports' | 'Team' | 'Branch' | 'LegalEntity' | 'Tenant';
  avatarInitial: string;
  avatarUrl?: string;
  badgeColor: string;
  requireMfa?: boolean;
}

const STATIC_DEMO_ENABLED =
  import.meta.env.DEV ||
  import.meta.env.MODE === 'test' ||
  import.meta.env.VITE_ENABLE_DEMO_MODE === 'true';

export const DEMO_USERS: Partial<Record<DemoRole, UserProfile>> = STATIC_DEMO_ENABLED
  ? {
      PlatformAdmin: {
    id: 'usr-plat-01',
    name: 'Platform Operator',
    email: 'ops@quorum.internal',
    role: 'PlatformAdmin',
    dataScope: 'Tenant',
    avatarInitial: 'OP',
    badgeColor: '#1F2227',
    requireMfa: true,
  },
  CompanyOwner: {
    id: 'usr-own-02',
    name: 'Arya Pratama',
    email: 'owner@nusantara.id',
    role: 'CompanyOwner',
    dataScope: 'Tenant',
    avatarInitial: 'AP',
    badgeColor: '#1F2227',
    requireMfa: true,
  },
  HrAdmin: {
    id: 'usr-hr-03',
    name: 'Sari Wijaya',
    email: 'sari.wijaya@nusantara.id',
    role: 'HrAdmin',
    dataScope: 'LegalEntity',
    avatarInitial: 'SW',
    avatarUrl: '/images/avatars/sari-wijaya.jpg',
    badgeColor: '#0D1B39',
    requireMfa: true,
  },
  PayrollOfficer: {
    id: 'usr-py-04',
    name: 'Bambang Prakoso',
    email: 'bambang.p@nusantara.id',
    role: 'PayrollOfficer',
    dataScope: 'LegalEntity',
    avatarInitial: 'BP',
    badgeColor: '#9A4A06',
    requireMfa: true,
  },
  Manager: {
    id: 'usr-mgr-05',
    name: 'Hendro Kusuma',
    email: 'hendro.k@nusantara.id',
    role: 'Manager',
    dataScope: 'DirectReports',
    avatarInitial: 'HK',
    badgeColor: '#1F2227',
    requireMfa: false,
  },
  Employee: {
    id: 'usr-emp-06',
    name: 'Dewi Lestari',
    email: 'dewi.lestari@nusantara.id',
    role: 'Employee',
    dataScope: 'Self',
    avatarInitial: 'DL',
    badgeColor: '#52565E',
    requireMfa: false,
  },
  Auditor: {
    id: 'usr-aud-07',
    name: 'Nurul Hidayat',
    email: 'auditor.external@pwc-audit.test',
    role: 'Auditor',
    dataScope: 'Tenant',
    avatarInitial: 'NH',
    badgeColor: '#374151',
    requireMfa: true,
  },
} : {};

interface AuthContextValue {
  user: UserProfile | null;
  isAuthenticated: boolean;
  login: (role: DemoRole) => void;
  logout: () => void;
  availableDemoRoles: Partial<Record<DemoRole, UserProfile>>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({
  children,
  defaultRole = 'HrAdmin',
  demoMode = IS_DEMO_MODE,
  initialUser = null,
}: {
  children: React.ReactNode;
  defaultRole?: DemoRole;
  demoMode?: boolean;
  initialUser?: UserProfile | null;
}) {
  const [user, setUser] = useState<UserProfile | null>(() => {
    if (initialUser) return initialUser;
    return demoMode ? DEMO_USERS[defaultRole] || null : null;
  });

  useEffect(() => {
    if (demoMode && !initialUser) {
      setUser(DEMO_USERS[defaultRole] || null);
    } else if (!demoMode && !initialUser) {
      setUser(null);
    }
  }, [demoMode, defaultRole, initialUser]);

  const login = (role: DemoRole) => {
    if (!demoMode) {
      throw new Error('Demo login is disabled when demo mode is off.');
    }
    wipeTenantCache();
    const targetUser = DEMO_USERS[role];
    if (targetUser) {
      setUser(targetUser);
    }
  };

  const logout = () => {
    wipeTenantCache();
    setUser(null);
    if (!demoMode) {
      void apiClient.post('/api/v1/auth/logout', {}).catch(() => {});
    }
  };

  const isAuthenticated = user !== null;

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated,
        login,
        logout,
        availableDemoRoles: demoMode ? DEMO_USERS : {},
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
