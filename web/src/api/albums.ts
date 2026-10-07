import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { reorderPages } from '../albums/reorder'
import { useNotify } from '../app/notify'
import { apiFetch } from './client'
import { queryKeys, type AlbumImagesData } from './queries'
import type { AlbumAddResult, AlbumDetail } from './types'

export type AddTarget = { imageIds: number[] } | { folderId: number }
export type AlbumInput = { name: string; description: string | null }
type MoveVars = { imageId: number; afterImageId: number | null; order: number[] }

const jsonRequest = (method: string, body: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

/** After adding or removing: counts, covers and dates, the album's photos, and "in albums" details. */
function refreshAlbumContents(queryClient: QueryClient, albumId: number) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: ['albums'] }),
    queryClient.invalidateQueries({ queryKey: queryKeys.albumImages(albumId) }),
    queryClient.invalidateQueries({ queryKey: ['images', 'detail'] }),
  ])
}

export function useCreateAlbum() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: AlbumInput) =>
      apiFetch<AlbumDetail>('/api/albums', jsonRequest('POST', input)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
  })
}

export function useUpdateAlbum(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: AlbumInput) =>
      apiFetch<AlbumDetail>(`/api/albums/${id}`, jsonRequest('PATCH', input)),
    onSuccess: (album) => {
      queryClient.setQueryData(queryKeys.album(id), album)
      return queryClient.invalidateQueries({ queryKey: queryKeys.albums() })
    },
  })
}

export function useDeleteAlbum(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiFetch<void>(`/api/albums/${id}`, { method: 'DELETE' }),
    // The album's own queries are left to expire: removing them while its view is still mounted
    // would refetch and flash "Album not found." before the navigation away.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
  })
}

export function useAddToAlbum() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ albumId, target }: { albumId: number; target: AddTarget }) =>
      apiFetch<AlbumAddResult>(`/api/albums/${albumId}/images`, jsonRequest('POST', target)),
    onSuccess: (_result, { albumId }) => refreshAlbumContents(queryClient, albumId),
  })
}

export function useRemoveFromAlbum(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ imageIds }: { imageIds: number[] }) =>
      apiFetch<void>(`/api/albums/${albumId}/images/remove`, jsonRequest('POST', { imageIds })),
    onSuccess: () => refreshAlbumContents(queryClient, albumId),
  })
}

/** Removal where the album is only known at call time (the viewer's Shift+D outside an album). */
export function useRemoveImagesFromAlbum() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ albumId, imageIds }: { albumId: number; imageIds: number[] }) =>
      apiFetch<void>(`/api/albums/${albumId}/images/remove`, jsonRequest('POST', { imageIds })),
    onSuccess: (_result, { albumId }) => refreshAlbumContents(queryClient, albumId),
  })
}

export type AlbumSort = 'dateAsc' | 'dateDesc' | 'name'

/** Rewrites the album's stored order; the grid and exports then follow it. */
export function useSortAlbum(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (by: AlbumSort) =>
      apiFetch<void>(`/api/albums/${albumId}/sort`, jsonRequest('POST', { by })),
    onSuccess: () => refreshAlbumContents(queryClient, albumId),
  })
}

/** Chooses which of the album's photos is shown as its cover. */
export function useSetAlbumCover(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (imageId: number) =>
      apiFetch<void>(`/api/albums/${albumId}/cover`, jsonRequest('PUT', { imageId })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
  })
}

export function useMoveInAlbum(albumId: number) {
  const queryClient = useQueryClient()
  const notify = useNotify()
  return useMutation({
    // One queue per album: each move is computed against the order the previous one left.
    scope: { id: `album-move-${albumId}` },
    mutationFn: ({ imageId, afterImageId }: MoveVars) =>
      apiFetch<void>(
        `/api/albums/${albumId}/images/${imageId}/move`,
        jsonRequest('POST', { afterImageId }),
      ),
    onMutate: async ({ order }: MoveVars) => {
      const key = queryKeys.albumImages(albumId)
      await queryClient.cancelQueries({ queryKey: key })
      const previous = queryClient.getQueryData<AlbumImagesData>(key)
      if (previous !== undefined) {
        queryClient.setQueryData<AlbumImagesData>(key, reorderPages(previous, order))
      }
      return { previous }
    },
    onError: (_error, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(queryKeys.albumImages(albumId), context.previous)
      }
      notify("Couldn't save the new order.")
    },
    // `scope` only blocks a queued move while this one is *pending*, not through onError/onSuccess,
    // so a queued move's own request can land before or after this one's rollback settles either
    // way. Whichever move settles last corrects the cache here, so the final state always matches
    // what the server actually has, however the two responses interleaved.
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.albumImages(albumId) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
  })
}
