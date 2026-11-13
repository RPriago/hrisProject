import React, { createContext, useContext, useState } from 'react';
import { wipeTenantCache } from '@/lib/queryClient';
import { apiClient } from '@/lib/api/apiClient';
import { IS_DEMO_MODE } from '@/lib/env';

export interface TenantInfo {
  id: string;
  name: string;
  legalEntityName: string;
  subscriptionStatus: 'Active' | 'Trial' | 'PastDue' | 'Suspended';
  isSupportAccess?: boolean;
  supportExpiresInMinutes?: number;
  planName?: string;
  activeEmployeeCount?: number;
  maxEmployeeQuota?: number;
  storageUsageMb?: number;
}

const STATIC_DEMO_ENABLED =
  import.meta.env.DEV ||
  import.meta.env.MODE === 'test' ||
  import.meta.env.VITE_ENABLE_DEMO_MODE === 'true';

export const INITIAL_DEMO_TENANTS: TenantInfo[] = STATIC_DEMO_ENABLED
  ? [
  {
    id: 'tenant-alpha-id',
    name: 'PT Nusantara Teknologi (Tenant Alpha)',
    legalEntityName: 'PT Nusantara Teknologi Utama',
    subscriptionStatus: 'Active',
    planName: 'Paket Growth',
    activeEmployeeCount: 84,
    maxEmployeeQuota: 100,
    storageUsageMb: 1240,
  },
  {
    id: 'tenant-beta-id',
    name: 'PT Maju Mandiri Bersama (Tenant Beta)',
    legalEntityName: 'PT Maju Mandiri Bersama',
    subscriptionStatus: 'PastDue',
    planName: 'Paket Bisnis',
    activeEmployeeCount: 142,
    maxEmployeeQuota: 200,
    storageUsageMb: 2850,
  },
  {
    id: 'tenant-gamma-id',
    name: 'PT Samudera Logistik Nusantara',
    legalEntityName: 'PT Samudera Logistik Nusantara',
    subscriptionStatus: 'Trial',
    planName: 'Paket Starter',
    activeEmployeeCount: 18,
    maxEmployeeQuota: 25,
    storageUsageMb: 320,
  },
]
  : [];

interface TenantContextValue {
  activeTenant: TenantInfo | null;
  availableTenants: TenantInfo[];
  subscriptionStatus: 'Active' | 'Trial' | 'PastDue' | 'Suspended' | 'Unknown';
  isSupportAccess: boolean;
  switchTenant: (tenantId: string) => Promise<void>;
  enableSupportAccess: (minutes?: number) => void;
  disableSupportAccess: () => void;
}

const TenantContext = createContext<TenantContextValue | undefined>(undefined);

export function TenantProvider({
  children,
  initialTenantId = STATIC_DEMO_ENABLED ? 'tenant-alpha-id' : undefined,
  demoMode = IS_DEMO_MODE,
  initialTenants,
  initialActiveTenant,
}: {
  children: React.ReactNode;
  initialTenantId?: string;
  demoMode?: boolean;
  initialTenants?: TenantInfo[];
  initialActiveTenant?: TenantInfo | null;
}) {
  const [availableTenants] = useState<TenantInfo[]>(
    () => initialTenants ?? (demoMode ? INITIAL_DEMO_TENANTS : [])
  );
  const [activeTenant, setActiveTenant] = useState<TenantInfo | null>(() => {
    if (initialActiveTenant !== undefined) return initialActiveTenant;
    if (!demoMode) return null;
    return availableTenants.find((t) => t.id === initialTenantId) || availableTenants[0] || null;
  });

  const switchTenant = async (tenantId: string) => {
    // 1. Enforce strict immediate query and state cache purge to avoid data leakage
    wipeTenantCache();

    try {
      // 2. Call backend tenant switch endpoint
      await apiClient.post('/api/v1/tenancy/switch-active-tenant', { targetTenantId: tenantId });
    } catch (err) {
      if (!demoMode) {
        throw err;
      }
      // Fallback only permitted in explicit demo or isolated test mode
    }

    // 3. Update active tenant context
    const nextTenant = availableTenants.find((t) => t.id === tenantId);
    if (nextTenant) {
      setActiveTenant(nextTenant);
    }
  };

  const enableSupportAccess = (minutes = 45) => {
    if (!demoMode) return;
    if (activeTenant) {
      setActiveTenant({
        ...activeTenant,
        isSupportAccess: true,
        supportExpiresInMinutes: minutes,
      });
    }
  };

  const disableSupportAccess = () => {
    if (activeTenant) {
      setActiveTenant({
        ...activeTenant,
        isSupportAccess: false,
        supportExpiresInMinutes: undefined,
      });
    }
  };

  const subscriptionStatus = activeTenant?.subscriptionStatus || 'Unknown';
  const isSupportAccess = !!activeTenant?.isSupportAccess;

  return (
    <TenantContext.Provider
      value={{
        activeTenant,
        availableTenants,
        subscriptionStatus,
        isSupportAccess,
        switchTenant,
        enableSupportAccess,
        disableSupportAccess,
      }}
    >
      {children}
    </TenantContext.Provider>
  );
}

export function useTenant(): TenantContextValue {
  const context = useContext(TenantContext);
  if (!context) {
    throw new Error('useTenant must be used within a TenantProvider');
  }
  return context;
}
