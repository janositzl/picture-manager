import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/client'
import { shouldRetry } from './queryClient'

describe('shouldRetry', () => {
  it('never retries a 4xx', () => {
    expect(shouldRetry(0, new ApiError(404, null))).toBe(false)
    expect(shouldRetry(0, new ApiError(400, null))).toBe(false)
  })

  it('retries a 5xx or a network error once', () => {
    expect(shouldRetry(0, new ApiError(500, null))).toBe(true)
    expect(shouldRetry(1, new ApiError(500, null))).toBe(false)
    expect(shouldRetry(0, new TypeError('Failed to fetch'))).toBe(true)
    expect(shouldRetry(1, new TypeError('Failed to fetch'))).toBe(false)
  })
})
