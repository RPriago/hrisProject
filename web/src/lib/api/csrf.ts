export interface CsrfResponse {
  csrfToken?: string;
  token?: string;
  headerName?: string;
}

export class CsrfManager {
  private cachedToken: string | null = null;
  private headerName: string = 'X-CSRF-Token';
  private inFlightPromise: Promise<string> | null = null;

  public async getToken(): Promise<string> {
    if (this.cachedToken) {
      return this.cachedToken;
    }

    if (this.inFlightPromise) {
      return this.inFlightPromise;
    }

    this.inFlightPromise = this.fetchFreshToken().finally(() => {
      this.inFlightPromise = null;
    });

    return this.inFlightPromise;
  }

  public async refreshToken(): Promise<string> {
    this.cachedToken = null;
    return this.getToken();
  }

  public clear(): void {
    this.cachedToken = null;
    this.inFlightPromise = null;
  }

  public getHeaderName(): string {
    return this.headerName;
  }

  private async fetchFreshToken(): Promise<string> {
    const res = await fetch('/api/v1/auth/csrf', {
      method: 'GET',
      credentials: 'include',
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

    if (data.headerName) {
      this.headerName = data.headerName;
    }

    this.cachedToken = token;
    return token;
  }
}

export const csrfManager = new CsrfManager();
