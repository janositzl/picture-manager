import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { ApiError, apiFetch, isNotFound } from './client'

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
