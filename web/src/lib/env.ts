export interface DemoEnvLike {
  DEV?: boolean;
  PROD?: boolean;
  MODE?: string;
  VITE_ENABLE_DEMO_MODE?: string;
}

/**
 * Demo mode (demo personas, role switcher modal, client-side tenant spoofing)
 * is enabled strictly during local development, Vitest runs, or explicit opt-in
 * via VITE_ENABLE_DEMO_MODE="true". Production bundles default to fail-closed.
 */
export function isDemoModeEnabled(env: DemoEnvLike = import.meta.env as DemoEnvLike): boolean {
  return Boolean(
    env.DEV || env.MODE === 'test' || env.VITE_ENABLE_DEMO_MODE === 'true'
  );
}

export const IS_DEMO_MODE: boolean = isDemoModeEnabled();
