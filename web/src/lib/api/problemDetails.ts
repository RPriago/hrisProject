export interface ProblemDetails {
  type?: string;
  title?: string;
  status: number;
  code?: string;
  detail?: string;
  instance?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}

export interface PaginatedResponse<T> {
  items: T[];
  pagination: {
    page: number;
    pageSize: number;
    totalItems: number;
    totalPages: number;
  };
  meta?: {
    scopeApplied?: string;
    timeZoneId?: string;
    generatedAtUtc?: string;
    [key: string]: unknown;
  };
}

export class ApiClientError extends Error {
  public readonly problem: ProblemDetails;
  public readonly statusCode: number;
  public readonly code?: string;
  public readonly correlationId?: string;

  constructor(problem: ProblemDetails) {
    const message = problem.detail || problem.title || `API request failed with status ${problem.status}`;
    super(message);
    this.name = 'ApiClientError';
    this.problem = problem;
    this.statusCode = problem.status;
    this.code = problem.code;
    this.correlationId = problem.correlationId;

    // Restore prototype chain for instanceof checks
    Object.setPrototypeOf(this, ApiClientError.prototype);
  }
}

export function parseProblemDetails(
  status: number,
  body: unknown,
  responseHeaders?: Headers
): ProblemDetails {
  const correlationIdFromHeader = responseHeaders?.get('X-Correlation-Id') || undefined;

  if (body && typeof body === 'object') {
    const b = body as Record<string, unknown>;
    const correlationId = (b.correlationId as string) || correlationIdFromHeader;

    return {
      type: (b.type as string) || `https://httpstatuses.com/${status}`,
      title: (b.title as string) || getDefaultTitleForStatus(status),
      status: typeof b.status === 'number' ? b.status : status,
      code: (b.code as string) || (b.errorCode as string) || getDefaultCodeForStatus(status),
      detail: (b.detail as string) || (b.message as string) || undefined,
      instance: (b.instance as string) || undefined,
      correlationId: correlationId,
      errors: (b.errors as Record<string, string[]>) || undefined,
      ...b,
    };
  }

  const detailText = typeof body === 'string' && body.trim().length > 0 ? body.trim() : undefined;

  return {
    type: `https://httpstatuses.com/${status}`,
    title: getDefaultTitleForStatus(status),
    status,
    code: getDefaultCodeForStatus(status),
    detail: detailText,
    correlationId: correlationIdFromHeader,
  };
}

function getDefaultTitleForStatus(status: number): string {
  switch (status) {
    case 400: return 'Bad Request';
    case 401: return 'Unauthorized';
    case 403: return 'Forbidden';
    case 404: return 'Not Found';
    case 409: return 'Conflict';
    case 422: return 'Unprocessable Entity';
    case 500: return 'Internal Server Error';
    case 502: return 'Bad Gateway';
    case 503: return 'Service Unavailable';
    default: return 'Request Failed';
  }
}

function getDefaultCodeForStatus(status: number): string {
  switch (status) {
    case 400: return 'BAD_REQUEST';
    case 401: return 'UNAUTHORIZED';
    case 403: return 'FORBIDDEN';
    case 404: return 'NOT_FOUND';
    case 409: return 'CONFLICT';
    case 422: return 'VALIDATION_FAILED';
    case 500: return 'INTERNAL_SERVER_ERROR';
    default: return `HTTP_${status}`;
  }
}
