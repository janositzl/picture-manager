import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, apiFetch } from './client'
import type { UserSummary } from './types'

export const usersQueryKey = ['users'] as const

export type UserCreateInput = {
  username: string
  displayName: string
  password: string
  role: 'Admin' | 'User'
  canRunFolderActions: boolean
}

export type UserUpdateInput = {
  displayName?: string
  role?: 'Admin' | 'User'
  isActive?: boolean
  canRunFolderActions?: boolean
}

const json = (method: string, body: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

export function useUsers() {
  return useQuery({
    queryKey: usersQueryKey,
    queryFn: ({ signal }) => apiFetch<UserSummary[]>('/api/users', { signal }),
  })
}

export function useCreateUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: UserCreateInput) => apiFetch<UserSummary>('/api/users', json('POST', input)),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: usersQueryKey }),
  })
}

export function useUpdateUser(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: UserUpdateInput) => apiFetch<UserSummary>(`/api/users/${id}`, json('PATCH', input)),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: usersQueryKey }),
  })
}

export function useResetPassword(id: number) {
  return useMutation({
    mutationFn: (newPassword: string) =>
      apiFetch<void>(`/api/users/${id}/reset-password`, json('POST', { newPassword })),
  })
}

export function useDeleteUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, albums }: { id: number; albums: 'transfer' | 'delete' }) =>
      apiFetch<void>(`/api/users/${id}?albums=${albums}`, { method: 'DELETE' }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: usersQueryKey }),
  })
}

export type UserFormErrors = Record<string, string | undefined> & { form?: string }

/**
 * Maps a failed user request onto the form: a 400 lands on its field (only names in `fields`),
 * anything else (including a 409 guard message) on the form as a whole.
 */
export function userFormErrors(error: unknown, fields: string[]): UserFormErrors {
  if (error instanceof ApiError) {
    if (error.status === 400) {
      const mapped: UserFormErrors = {}
      for (const [key, messages] of Object.entries(error.problem?.errors ?? {})) {
        const field = fields.find((f) => f.toLowerCase() === key.toLowerCase())
        if (field && messages[0]) mapped[field] = messages[0]
      }
      if (Object.keys(mapped).length > 0) return mapped
    }
    if (error.status === 409 && error.problem?.detail) return { form: error.problem.detail }
  }
  return { form: "Couldn't save the user." }
}
