import React, { createContext, useContext, useState } from 'react';
import { wipeTenantCache } from '@/lib/queryClient';

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

export const DEMO_USERS: Record<DemoRole, UserProfile> = {
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
};

interface AuthContextValue {
  user: UserProfile | null;
  isAuthenticated: boolean;
  login: (role: DemoRole) => void;
  logout: () => void;
  availableDemoRoles: typeof DEMO_USERS;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({
  children,
  defaultRole = 'HrAdmin',
}: {
  children: React.ReactNode;
  defaultRole?: DemoRole;
}) {
  const [user, setUser] = useState<UserProfile | null>(() => DEMO_USERS[defaultRole] || null);

  const login = (role: DemoRole) => {
    wipeTenantCache();
    const targetUser = DEMO_USERS[role];
    if (targetUser) {
      setUser(targetUser);
    }
  };

  const logout = () => {
    wipeTenantCache();
    setUser(null);
  };

  const isAuthenticated = user !== null;

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated,
        login,
        logout,
        availableDemoRoles: DEMO_USERS,
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
