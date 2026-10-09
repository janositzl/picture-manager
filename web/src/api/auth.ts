import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { ApiError, apiFetch } from './client'
import type { Me } from './types'

export const meQueryKey = ['me'] as const

const jsonPost = (body: unknown): RequestInit => ({
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

/**
 * Drops everything cached except `me`. QueryClient.clear() would also detach AuthGate's observer
 * from the `me` query, so the UI would never see the new value.
 */
function clearSessionData(queryClient: QueryClient): void {
  queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== meQueryKey[0] })
}

/** The signed-in user, or null when there is no session (a 401 is an answer here, not an error). */
export function useCurrentUser() {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: async () => {
      try {
        return await apiFetch<Me>('/api/auth/me')
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    staleTime: Infinity,
  })
}

/** What the UI may offer. The server enforces the same rules; this only hides controls that would be refused. */
export function usePermissions(): { isAdmin: boolean; canRunFolderActions: boolean } {
  const me = useCurrentUser().data
  return { isAdmin: me?.role === 'Admin', canRunFolderActions: me?.canRunFolderActions ?? false }
}

export function useLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (credentials: { username: string; password: string }) =>
      apiFetch<Me>('/api/auth/login', jsonPost(credentials)),
    onSuccess: (me) => {
      // Nothing cached under a previous session may leak into this one.
      clearSessionData(queryClient)
      queryClient.setQueryData(meQueryKey, me)
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiFetch<void>('/api/auth/logout', { method: 'POST' }),
    onSettled: () => {
      clearSessionData(queryClient)
      queryClient.setQueryData(meQueryKey, null)
    },
  })
}

export function useChangePassword() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      apiFetch<Me>('/api/auth/password', jsonPost(body)),
    onSuccess: (me) => queryClient.setQueryData(meQueryKey, me),
  })
}
