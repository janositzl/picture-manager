import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { server } from '../test/server'
import { ApiError, apiFetch, isNotFound, withBase } from './client'

describe('apiFetch', () => {
  it('returns the parsed JSON body', async () => {
    server.use(http.get('/api/thing', () => HttpResponse.json({ id: 1 })))
    await expect(apiFetch<{ id: number }>('/api/thing')).resolves.toEqual({ id: 1 })
  })

  it('returns undefined for 204', async () => {
    server.use(http.put('/api/thing', () => new HttpResponse(null, { status: 204 })))
    await expect(apiFetch<void>('/api/thing', { method: 'PUT' })).resolves.toBeUndefined()
  })

  it('returns undefined for an empty 202 body', async () => {
    server.use(http.post('/api/thing', () => new HttpResponse(null, { status: 202 })))
    await expect(apiFetch<void>('/api/thing', { method: 'POST' })).resolves.toBeUndefined()
  })

  it('throws ApiError carrying the status and ProblemDetails', async () => {
    server.use(
      http.get('/api/thing', () =>
        HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 }),
      ),
    )
    const error = await apiFetch('/api/thing').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(404)
    expect((error as ApiError).problem?.title).toBe('Not Found')
    expect(isNotFound(error)).toBe(true)
  })

  it('throws ApiError with no problem when the error body is not JSON', async () => {
    server.use(http.get('/api/thing', () => new HttpResponse('oops', { status: 500 })))
    const error = (await apiFetch('/api/thing').catch((e: unknown) => e)) as ApiError
    expect(error.status).toBe(500)
    expect(error.problem).toBeNull()
    expect(error.message).toBe('Request failed with status 500')
    expect(isNotFound(error)).toBe(false)
  })
})

describe('withBase', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('leaves the path unchanged at the site root', () => {
    expect(withBase('/api/images/1/thumbnail')).toBe('/api/images/1/thumbnail')
  })

  it('prefixes the path under a sub-path build', () => {
    vi.stubEnv('BASE_URL', '/pm/')
    expect(withBase('/api/images/1/thumbnail')).toBe('/pm/api/images/1/thumbnail')
  })

  it('makes apiFetch request the prefixed path', async () => {
    vi.stubEnv('BASE_URL', '/pm/')
    server.use(http.get('/pm/api/thing', () => HttpResponse.json({ id: 2 })))
    await expect(apiFetch<{ id: number }>('/api/thing')).resolves.toEqual({ id: 2 })
  })
})
