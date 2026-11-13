export interface CsrfResponse {
  csrfToken?: string;
  token?: string;
  headerName?: string;
}

export class CsrfManager {
  private static readonly VALID_HEADER_NAME = /^X-CSRF-[A-Za-z0-9-]+$/i;
  private cachedToken: string | null = null;
  private headerName: string = 'X-CSRF-Token';
  private inFlightPromise: Promise<string> | null = null;
  private abortController: AbortController | null = null;
  private epoch = 0;

  public async getToken(): Promise<string> {
    if (this.cachedToken) {
      return this.cachedToken;
    }

    if (this.inFlightPromise) {
      return this.inFlightPromise;
    }

    const requestEpoch = this.epoch;
    const promise = this.fetchFreshToken(requestEpoch).finally(() => {
      if (this.inFlightPromise === promise) {
        this.inFlightPromise = null;
      }
    });
    this.inFlightPromise = promise;

    return promise;
  }

  public async refreshToken(): Promise<string> {
    this.clear();
    return this.getToken();
  }

  public clear(): void {
    this.epoch += 1;
    this.cachedToken = null;
    this.inFlightPromise = null;
    if (this.abortController) {
      this.abortController.abort();
      this.abortController = null;
    }
  }

  public getHeaderName(): string {
    return this.headerName;
  }

  private async fetchFreshToken(requestEpoch: number): Promise<string> {
    if (this.abortController) {
      this.abortController.abort();
    }
    const controller = new AbortController();
    this.abortController = controller;

    const res = await fetch('/api/v1/auth/csrf', {
      method: 'GET',
      credentials: 'include',
      signal: controller.signal,
      headers: {
        Accept: 'application/json',
      },
    });

    if (!res.ok) {
      throw new Error(`Failed to fetch Anti-CSRF token (HTTP ${res.status})`);
    }

    const data = (await res.json()) as CsrfResponse;
    const token = data.csrfToken || data.token;

    if (!token) {
      throw new Error('Anti-CSRF endpoint returned an empty token.');
    }

    if (requestEpoch !== this.epoch) {
      throw new Error('Anti-CSRF token fetch invalidated by context reset.');
    }

    if (typeof data.headerName === 'string' && CsrfManager.VALID_HEADER_NAME.test(data.headerName)) {
      this.headerName = data.headerName;
    }

    this.cachedToken = token;
    if (this.abortController === controller) {
      this.abortController = null;
    }
    return token;
  }
}

export const csrfManager = new CsrfManager();
