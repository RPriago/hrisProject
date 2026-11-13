import { QueryClient } from '@tanstack/react-query';
import { csrfManager } from './api/csrf';

type MemoryWipeListener = () => void;
const memoryWipeListeners = new Set<MemoryWipeListener>();

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 2, // 2 minutes
      retry: (failureCount, error) => {
        // Do not retry 4xx errors
        if (typeof error === 'object' && error !== null && 'statusCode' in error) {
          const status = (error as { statusCode: number }).statusCode;
          if (status >= 400 && status < 500) return false;
        }
        return failureCount < 2;
      },
      refetchOnWindowFocus: false,
    },
  },
});

/**
 * Registers a callback invoked whenever the active tenant context changes or the user logs out.
 * Returns an unregister function.
 */
export function registerMemoryWipeListener(listener: MemoryWipeListener): () => void {
  memoryWipeListeners.add(listener);
  return () => {
    memoryWipeListeners.delete(listener);
  };
}

/**
 * Completely purges TanStack Query memory cache, clears Anti-CSRF token cache,
 * executes registered memory wipe callbacks (form drafts, search inputs, preview refs),
 * and enforces browser storage hygiene (no tokens or PII in localStorage/sessionStorage).
 */
export function wipeTenantCache(): void {
  // 1. Cancel any in-flight queries and purge all query/mutation caches in TanStack Query
  void queryClient.cancelQueries();
  queryClient.clear();

  // 2. Clear CSRF token cache
  csrfManager.clear();

  // 3. Invoke all registered in-memory state resetters
  for (const listener of memoryWipeListeners) {
    try {
      listener();
    } catch (err) {
      console.error('Error executing memory wipe listener:', err);
    }
  }

  // 4. Clean storage from any unintended token or PII leaks
  try {
    const keysToRemove = ['token', 'user', 'employee', 'search_history', 'auth_session'];
    for (const key of keysToRemove) {
      localStorage.removeItem(key);
      sessionStorage.removeItem(key);
    }
  } catch {
    // Storage access might be restricted in some iframe or sandbox environments
  }
}
