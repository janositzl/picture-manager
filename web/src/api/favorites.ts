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
type ImageGroup = { images: ImageListItem[] }
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
  // The 'duplicates' prefix covers both the exact and the similar group caches.
  queryClient.setQueriesData<InfiniteData<Page<ImageGroup>, string | null>>(
    { queryKey: queryKeys.duplicates() },
    (data) =>
      data === undefined
        ? data
        : {
            ...data,
            pages: data.pages.map((page) => ({
              ...page,
              items: page.items.map((group) => ({
                ...group,
                images: group.images.map((item) =>
                  item.id === id ? { ...item, isFavorite } : item,
                ),
              })),
            })),
          },
  )
}

function isFavoritesList(queryKey: readonly unknown[]): boolean {
  const filter = queryKey[2] as ImageFilter | undefined
  return filter?.kind === 'favorites'
}

/** Per image: the latest toggle's sequence number and the last state the server confirmed. */
type Tracker = Map<number, { latest: number; confirmed: boolean }>
const trackers = new WeakMap<QueryClient, Tracker>()

function trackerFor(queryClient: QueryClient): Tracker {
  let tracker = trackers.get(queryClient)
  if (tracker === undefined) {
    tracker = new Map()
    trackers.set(queryClient, tracker)
  }
  return tracker
}

export function useSetFavorite() {
  const queryClient = useQueryClient()
  const notify = useNotify()
  const tracker = trackerFor(queryClient)

  return useMutation({
    // One queue for all toggles, so a PUT and a DELETE never race each other to the server.
    scope: { id: 'favorite' },
    mutationFn: ({ id, isFavorite }: FavoriteChange) =>
      apiFetch<void>(`/api/images/${id}/favorite`, { method: isFavorite ? 'PUT' : 'DELETE' }),
    onMutate: ({ id, isFavorite }: FavoriteChange) => {
      const entry = tracker.get(id)
      // A toggle always flips the shown state, so with nothing in flight the server has !isFavorite.
      const seq = (entry?.latest ?? 0) + 1
      tracker.set(id, { latest: seq, confirmed: entry?.confirmed ?? !isFavorite })
      patchFavorite(queryClient, id, isFavorite)
      return { seq }
    },
    onError: (_error, { id }, context) => {
      const entry = tracker.get(id)
      // Only the newest toggle may repaint: an older failure is superseded by what came after it.
      if (entry !== undefined && entry.latest === context?.seq) {
        patchFavorite(queryClient, id, entry.confirmed)
        tracker.delete(id)
      }
      notify("Couldn't update favorite.")
    },
    onSuccess: (_data, { id, isFavorite }, context) => {
      const entry = tracker.get(id)
      if (entry !== undefined) {
        if (entry.latest === context.seq) tracker.delete(id)
        else entry.confirmed = isFavorite
      }
      // Stale, not refetched: an open Favorites view keeps the dimmed photo until it's left.
      return queryClient.invalidateQueries({
        queryKey: queryKeys.imageLists(),
        predicate: (query) => isFavoritesList(query.queryKey),
        refetchType: 'none',
      })
    },
  })
}
