import {
  useMutation,
  useQueryClient,
  type InfiniteData,
  type QueryClient,
} from '@tanstack/react-query'
import { useNotify } from '../app/notify'
import { apiFetch } from './client'
import type { ImageFilter } from './imageFilter'
import { queryKeys } from './queries'
import type { ImageDetail, ImageListItem, Page } from './types'

type ListData = InfiniteData<Page<ImageListItem>, string | null>
type FavoriteChange = { id: number; isFavorite: boolean }

/** Sets isFavorite on the image in every cached list page and in its cached detail. */
export function patchFavorite(queryClient: QueryClient, id: number, isFavorite: boolean): void {
  queryClient.setQueriesData<ListData>({ queryKey: queryKeys.imageLists() }, (data) =>
    data === undefined
      ? data
      : {
          ...data,
          pages: data.pages.map((page) => ({
            ...page,
            items: page.items.map((item) => (item.id === id ? { ...item, isFavorite } : item)),
          })),
        },
  )
  queryClient.setQueryData<ImageDetail>(queryKeys.image(id), (detail) =>
    detail === undefined ? detail : { ...detail, isFavorite },
  )
}

function isFavoritesList(queryKey: readonly unknown[]): boolean {
  const filter = queryKey[2] as ImageFilter | undefined
  return filter?.kind === 'favorites'
}

export function useSetFavorite() {
  const queryClient = useQueryClient()
  const notify = useNotify()

  return useMutation({
    mutationFn: ({ id, isFavorite }: FavoriteChange) =>
      apiFetch<void>(`/api/images/${id}/favorite`, { method: isFavorite ? 'PUT' : 'DELETE' }),
    onMutate: ({ id, isFavorite }: FavoriteChange) => {
      const snapshot = queryClient.getQueriesData({ queryKey: ['images'] })
      patchFavorite(queryClient, id, isFavorite)
      return { snapshot }
    },
    onError: (_error, _change, context) => {
      context?.snapshot.forEach(([key, data]) => queryClient.setQueryData(key, data))
      notify("Couldn't update favorite.")
    },
    onSuccess: () =>
      // Stale, not refetched: an open Favorites view keeps the dimmed photo until it's left.
      queryClient.invalidateQueries({
        queryKey: queryKeys.imageLists(),
        predicate: (query) => isFavoritesList(query.queryKey),
        refetchType: 'none',
      }),
  })
}
