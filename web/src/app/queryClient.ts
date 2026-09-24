import { QueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'

/** No retry for a 4xx (the answer won't change); one retry for network errors and 5xx. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status < 500) return false
  return failureCount < 1
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: shouldRetry,
        // Long enough that Back to a view is instant; favorites lists are marked stale explicitly.
        staleTime: 30_000,
        // A refetch on focus could drop a dimmed, just-unstarred photo from an open Favorites view.
        refetchOnWindowFocus: false,
      },
    },
  })
}
