import { http, HttpResponse } from 'msw'
import type { Me } from '../api/types'
import { adminMe } from './fixtures'

let current: Me | null = adminMe

/** The signed-in user, or null. */
export const currentMe = (): Me | null => current

/** What GET /api/auth/me answers; null means signed out (401). */
export function signInAs(me: Me | null): void {
  current = me
}

/** Called before every test (setup.ts): signed in as the admin, so every control is visible. */
export function resetAuthStore(): void {
  current = adminMe
}

const unauthorized = () =>
  HttpResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 })

export const authHandlers = [
  http.get('/api/auth/me', () => (current ? HttpResponse.json(current) : unauthorized())),
  http.post('/api/auth/login', async ({ request }) => {
    const body = (await request.json()) as { username: string; password: string }
    if (body.password !== 'correct-password')
      return HttpResponse.json(
        { title: 'Invalid username or password.', status: 401 },
        { status: 401 },
      )
    current = { ...adminMe, username: body.username }
    return HttpResponse.json(current)
  }),
  http.post('/api/auth/logout', () => {
    current = null
    return new HttpResponse(null, { status: 204 })
  }),
  http.post('/api/auth/password', async ({ request }) => {
    const body = (await request.json()) as { currentPassword: string; newPassword: string }
    if (body.newPassword.length < 8)
      return HttpResponse.json(
        {
          title: 'Invalid',
          status: 400,
          errors: { newPassword: ['Must be at least 8 characters.'] },
        },
        { status: 400 },
      )
    current = { ...(current ?? adminMe), mustChangePassword: false }
    return HttpResponse.json(current)
  }),
]
