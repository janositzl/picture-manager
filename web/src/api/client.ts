import type { ProblemDetails } from './types'

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

let unauthorizedHandler: (() => void) | null = null

/** AuthGate registers this: any 401 outside /api/auth means the session ended, so show the login page. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler
}

function reportUnauthorized(path: string, status: number): void {
  if (status === 401 && !path.startsWith('/api/auth/')) unauthorizedHandler?.()
}

export function isNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404
}

/**
 * Fetches a same-origin API path ("/api/..."). Resolving against the page origin keeps relative
 * paths working in the browser and in tests (Node's fetch rejects bare relative URLs).
 */
export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(new URL(path, window.location.origin), {
    ...init,
    headers: { Accept: 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    reportUnauthorized(path, response.status)
    throw new ApiError(response.status, await readProblem(response))
  }

  if (response.status === 204) {
    return undefined as T
  }

  // Some endpoints (e.g. a 202 Accepted) answer with no body at all.
  const text = await response.text()
  return (text === '' ? undefined : JSON.parse(text)) as T
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}

/** Like apiFetch, for plain-text responses (album export). */
export async function apiFetchText(path: string, init?: RequestInit): Promise<string> {
  const response = await fetch(new URL(path, window.location.origin), {
    ...init,
    headers: { Accept: 'text/plain', ...init?.headers },
  })
  if (!response.ok) {
    reportUnauthorized(path, response.status)
    throw new ApiError(response.status, await readProblem(response))
  }
  return response.text()
}
