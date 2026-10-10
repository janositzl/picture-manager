import { http, HttpResponse } from 'msw'
import type { UserSummary } from '../api/types'
import { currentMe } from './authHandlers'

const seed: UserSummary[] = [
  { id: 1, username: 'admin', displayName: 'Administrator', role: 'Admin', isActive: true, canRunFolderActions: false, mustChangePassword: false, createdAt: '2026-09-21T00:00:00Z', lastLoginAt: '2026-10-10T08:00:00Z', albumCount: 3 },
  { id: 5, username: 'bob', displayName: 'Bob B', role: 'User', isActive: true, canRunFolderActions: false, mustChangePassword: false, createdAt: '2026-10-01T00:00:00Z', lastLoginAt: null, albumCount: 2 },
  { id: 6, username: 'carol', displayName: 'Carol C', role: 'User', isActive: false, canRunFolderActions: true, mustChangePassword: false, createdAt: '2026-10-02T00:00:00Z', lastLoginAt: null, albumCount: 0 },
]
const SELF_ID = 1

let users: UserSummary[]

export const userById = (id: number): UserSummary | undefined => users.find((u) => u.id === id)
let nextId: number
/** What the last reset-password call received, for assertions. */
export const resetCalls: { id: number; newPassword: string }[] = []
/** The `albums` value of every delete call, for assertions. */
export const deleteCalls: { id: number; albums: string | null }[] = []

/** Called before every test (setup.ts). */
export function resetUserStore(): void {
  users = seed.map((u) => ({ ...u }))
  nextId = 100
  resetCalls.length = 0
  deleteCalls.length = 0
}
resetUserStore()

const problem = (status: number, extra: Record<string, unknown>) =>
  HttpResponse.json({ title: 'Problem', status, ...extra }, { status })
const invalid = (field: string, message: string) => problem(400, { errors: { [field]: [message] } })

export const userHandlers = [
  http.get('/api/users/directory', () => {
    const me = currentMe()
    return HttpResponse.json(
      users.filter((u) => u.isActive && u.id !== me?.id).map((u) => ({ id: u.id, displayName: u.displayName })),
    )
  }),
  http.get('/api/users', () => HttpResponse.json(users)),
  http.post('/api/users', async ({ request }) => {
    const body = (await request.json()) as {
      username: string; displayName: string; password: string; role: 'Admin' | 'User'; canRunFolderActions: boolean
    }
    const username = body.username.trim()
    if (users.some((u) => u.username.toLowerCase() === username.toLowerCase()))
      return problem(409, { detail: `A user named '${username}' already exists.` })
    if (body.password.length < 8) return invalid('password', 'Must be at least 8 characters.')
    const created: UserSummary = {
      id: nextId++, username, displayName: body.displayName.trim(), role: body.role, isActive: true,
      canRunFolderActions: body.canRunFolderActions, mustChangePassword: true,
      createdAt: new Date().toISOString(), lastLoginAt: null, albumCount: 0,
    }
    users.push(created)
    return HttpResponse.json(created, { status: 201 })
  }),
  http.patch('/api/users/:id', async ({ params, request }) => {
    const id = Number(params.id)
    const user = users.find((u) => u.id === id)
    if (!user) return problem(404, {})
    const body = (await request.json()) as Partial<Pick<UserSummary, 'displayName' | 'role' | 'isActive' | 'canRunFolderActions'>>
    if (id === SELF_ID && (body.isActive === false || body.role === 'User'))
      return problem(409, { detail: "You can't disable or demote your own account." })
    if (body.displayName !== undefined && body.displayName.trim() === '')
      return invalid('displayName', 'Display name is required.')
    Object.assign(user, { ...body, displayName: body.displayName?.trim() ?? user.displayName })
    return HttpResponse.json(user)
  }),
  http.post('/api/users/:id/reset-password', async ({ params, request }) => {
    const id = Number(params.id)
    if (!users.some((u) => u.id === id)) return problem(404, {})
    const { newPassword } = (await request.json()) as { newPassword: string }
    if (newPassword.length < 8) return invalid('newPassword', 'Must be at least 8 characters.')
    resetCalls.push({ id, newPassword })
    return new HttpResponse(null, { status: 204 })
  }),
  http.delete('/api/users/:id', ({ params, request }) => {
    const id = Number(params.id)
    const albums = new URL(request.url).searchParams.get('albums')
    deleteCalls.push({ id, albums })
    if (albums !== 'transfer' && albums !== 'delete') return invalid('albums', "Must be 'transfer' or 'delete'.")
    if (id === SELF_ID) return problem(409, { detail: "You can't delete your own account." })
    if (!users.some((u) => u.id === id)) return problem(404, {})
    users = users.filter((u) => u.id !== id)
    return new HttpResponse(null, { status: 204 })
  }),
]
