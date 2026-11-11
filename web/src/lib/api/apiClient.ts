import { csrfManager } from './csrf';
import { parseProblemDetails, ApiClientError } from './problemDetails';

export interface RequestOptions extends RequestInit {
  idempotencyKey?: string;
  tenantId?: string;
  correlationId?: string;
  skipCsrf?: boolean;
}

export class ApiClient {
  public async get<T>(url: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('GET', url, undefined, options);
  }

  public async post<T>(url: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('POST', url, body, options);
  }

  public async put<T>(url: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('PUT', url, body, options);
  }

  public async patch<T>(url: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('PATCH', url, body, options);
  }

  public async delete<T>(url: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('DELETE', url, undefined, options);
  }

  private async request<T>(
    method: string,
    url: string,
    body?: unknown,
    options: RequestOptions = {},
    isRetry = false
  ): Promise<T> {
    const isMutation = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method.toUpperCase());
    const headers = new Headers(options.headers);

    // 1. Correlation ID
    const correlationId = options.correlationId || this.generateCorrelationId();
    if (!headers.has('X-Correlation-Id')) {
      headers.set('X-Correlation-Id', correlationId);
    }

    // 2. Anti-CSRF Token for mutations
    if (isMutation && !options.skipCsrf) {
      const csrfToken = await csrfManager.getToken();
      headers.set(csrfManager.getHeaderName(), csrfToken);
    }

    // 3. Idempotency Key
    if (options.idempotencyKey) {
      headers.set('Idempotency-Key', options.idempotencyKey);
    }

    // 4. Tenant Scope (development/testing override or explicit scope)
    if (options.tenantId) {
      headers.set('X-Tenant-Id', options.tenantId);
    }

    // 5. Body and Content-Type
    let serializedBody: BodyInit | undefined;
    if (body !== undefined) {
      if (body instanceof FormData || body instanceof Blob || typeof body === 'string') {
        serializedBody = body as BodyInit;
      } else {
        serializedBody = JSON.stringify(body);
        if (!headers.has('Content-Type')) {
          headers.set('Content-Type', 'application/json');
        }
      }
    }

    if (!headers.has('Accept')) {
      headers.set('Accept', 'application/json, application/problem+json');
    }

    const init: RequestInit = {
      ...options,
      method,
      headers,
      body: serializedBody,
      credentials: options.credentials || 'include',
    };

    const response = await fetch(url, init);

    // 6. Handle 403 CSRF Retry
    if (response.status === 403 && !isRetry && isMutation && !options.skipCsrf) {
      const clone = response.clone();
      try {
        const errorData = await clone.json();
        const code = errorData?.code || errorData?.errorCode;
        if (code === 'CSRF_TOKEN_INVALID' || code === 'CSRF_TOKEN_MISSING') {
          await csrfManager.refreshToken();
          return this.request<T>(method, url, body, options, true);
        }
      } catch {
        // Not a JSON response, continue to standard error handling
      }
    }

    // 7. Handle Success or Failure
    if (!response.ok) {
      let errorBody: unknown;
      const contentType = response.headers.get('Content-Type') || '';

      if (contentType.includes('json')) {
        try {
          errorBody = await response.json();
        } catch {
          errorBody = await response.text();
        }
      } else {
        errorBody = await response.text();
      }

      const problem = parseProblemDetails(response.status, errorBody, response.headers);
      throw new ApiClientError(problem);
    }

    // Check if 204 No Content
    if (response.status === 204) {
      return undefined as T;
    }

    const resContentType = response.headers.get('Content-Type') || '';
    if (resContentType.includes('json')) {
      return (await response.json()) as T;
    }

    const text = await response.text();
    if (!text) {
      return undefined as T;
    }

    try {
      return JSON.parse(text) as T;
    } catch {
      return text as unknown as T;
    }
  }

  private generateCorrelationId(): string {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) {
      return crypto.randomUUID();
    }
    return `corr-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;
  }
}

export const apiClient = new ApiClient();
