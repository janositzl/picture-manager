import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from './client'
import { queryKeys } from './queries'
import type { AlbumShare, DirectoryUser, SharePermission } from './types'

export const albumSharesKey = (albumId: number) => ['albums', albumId, 'shares'] as const

export function useAlbumShares(albumId: number) {
  return useQuery({
    queryKey: albumSharesKey(albumId),
    queryFn: ({ signal }) => apiFetch<AlbumShare[]>(`/api/albums/${albumId}/shares`, { signal }),
  })
}

export function useUserDirectory() {
  return useQuery({
    queryKey: ['users', 'directory'] as const,
    queryFn: ({ signal }) => apiFetch<DirectoryUser[]>('/api/users/directory', { signal }),
  })
}

/** Shares, or changes an existing share's permission. The album list's share counts follow. */
export function useSetAlbumShare(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ userId, permission }: { userId: number; permission: SharePermission }) =>
      apiFetch<AlbumShare>(`/api/albums/${albumId}/shares/${userId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ permission }),
      }),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: albumSharesKey(albumId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
      ]),
  })
}

/** The owner removing someone, or a sharer leaving (their own id). */
export function useRemoveAlbumShare(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (userId: number) =>
      apiFetch<void>(`/api/albums/${albumId}/shares/${userId}`, { method: 'DELETE' }),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: albumSharesKey(albumId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
      ]),
  })
}
