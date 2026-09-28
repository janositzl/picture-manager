import { queryOptions, useInfiniteQuery, useQuery, type InfiniteData } from '@tanstack/react-query'
import { useEffect } from 'react'
import { apiFetch } from './client'
import { toImageQuery, type ImageFilter } from './imageFilter'
import type {
  AlbumDetail,
  AlbumImageItem,
  AlbumSummary,
  DuplicateGroup,
  FolderDetail,
  FolderNode,
  ImageDetail,
  ImageListItem,
  Page,
  RemovedFolder,
  RootSummary,
  SettingsDto,
} from './types'

export const queryKeys = {
  rootFolders: () => ['folders', 'roots'] as const,
  folderChildren: (id: number) => ['folders', id, 'children'] as const,
  folder: (id: number) => ['folders', id, 'detail'] as const,
  imageLists: () => ['images', 'list'] as const,
  images: (filter: ImageFilter) => ['images', 'list', filter] as const,
  image: (id: number) => ['images', 'detail', id] as const,
  albums: () => ['albums', 'list'] as const,
  album: (id: number) => ['albums', id, 'detail'] as const,
  albumExport: (id: number, prefix: string) => ['albums', id, 'export', prefix] as const,
  // Under the photo-list prefix, so patchFavorite updates album tiles too.
  albumImages: (id: number) => ['images', 'list', { kind: 'album', albumId: id }] as const,
  duplicates: () => ['duplicates'] as const,
  settings: () => ['settings'] as const,
  roots: () => ['roots'] as const,
  removedFolders: () => ['folders', 'removed'] as const,
}

export function useRootFolders() {
  return useQuery({
    queryKey: queryKeys.rootFolders(),
    queryFn: ({ signal }) => apiFetch<FolderNode[]>('/api/folders/roots', { signal }),
  })
}

export function useFolderChildren(id: number, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.folderChildren(id),
    queryFn: ({ signal }) => apiFetch<FolderNode[]>(`/api/folders/${id}/children`, { signal }),
    enabled,
  })
}

export function useFolder(id: number | null) {
  return useQuery({
    queryKey: queryKeys.folder(id ?? 0),
    queryFn: ({ signal }) => apiFetch<FolderDetail>(`/api/folders/${id}`, { signal }),
    enabled: id !== null,
  })
}

type UseImagesOptions = { refetchOnMount?: boolean }

/** The one hook behind every grid (and the viewer, which walks the same cached pages). */
export function useImages(filter: ImageFilter, options: UseImagesOptions = {}) {
  return useInfiniteQuery({
    queryKey: queryKeys.images(filter),
    queryFn: ({ pageParam, signal }) =>
      apiFetch<Page<ImageListItem>>(`/api/images?${toImageQuery(filter, pageParam)}`, { signal }),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage: Page<ImageListItem>) => lastPage.nextCursor,
    refetchOnMount: options.refetchOnMount ?? true,
  })
}

export function useImage(id: number | null) {
  return useQuery({
    queryKey: queryKeys.image(id ?? 0),
    queryFn: ({ signal }) => apiFetch<ImageDetail>(`/api/images/${id}`, { signal }),
    enabled: id !== null,
  })
}

const ALBUM_PAGE_SIZE = 200
const DUPLICATES_PAGE_SIZE = 50

export type AlbumImagesData = InfiniteData<Page<AlbumImageItem>, string | null>

function pageQuery(cursor: string | null, limit: number): URLSearchParams {
  const params = new URLSearchParams({ limit: String(limit) })
  if (cursor !== null) params.set('cursor', cursor)
  return params
}

/** The API sorts by name; the picker and the Albums page want the album being filled on top. */
function byRecentlyUpdated(albums: AlbumSummary[]): AlbumSummary[] {
  return [...albums].sort(
    (a, b) => b.updatedAt.localeCompare(a.updatedAt) || a.name.localeCompare(b.name),
  )
}

export const albumsQuery = queryOptions({
  queryKey: queryKeys.albums(),
  queryFn: ({ signal }) => apiFetch<AlbumSummary[]>('/api/albums', { signal }),
})

export function useAlbums() {
  return useQuery({ ...albumsQuery, select: byRecentlyUpdated })
}

export function useAlbum(id: number | null) {
  return useQuery({
    queryKey: queryKeys.album(id ?? 0),
    queryFn: ({ signal }) => apiFetch<AlbumDetail>(`/api/albums/${id}`, { signal }),
    enabled: id !== null,
  })
}

/** The whole album: keeps fetching pages until none are left, so it can be reordered as one list. */
export function useAlbumImages(id: number | null) {
  const query = useInfiniteQuery({
    queryKey: queryKeys.albumImages(id ?? 0),
    queryFn: ({ pageParam, signal }) =>
      apiFetch<Page<AlbumImageItem>>(
        `/api/albums/${id}/images?${pageQuery(pageParam, ALBUM_PAGE_SIZE)}`,
        { signal },
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage: Page<AlbumImageItem>) => lastPage.nextCursor,
    enabled: id !== null,
  })
  const { hasNextPage, isFetchingNextPage, isError, fetchNextPage } = query
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && !isError) void fetchNextPage()
  }, [hasNextPage, isFetchingNextPage, isError, fetchNextPage])
  return query
}

export function useDuplicates() {
  return useInfiniteQuery({
    queryKey: queryKeys.duplicates(),
    queryFn: ({ pageParam, signal }) =>
      apiFetch<Page<DuplicateGroup>>(
        `/api/duplicates?${pageQuery(pageParam, DUPLICATES_PAGE_SIZE)}`,
        { signal },
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage: Page<DuplicateGroup>) => lastPage.nextCursor,
  })
}

export function useSettings() {
  return useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => apiFetch<SettingsDto>('/api/settings', { signal }),
  })
}

export function useRoots() {
  return useQuery({
    queryKey: queryKeys.roots(),
    queryFn: ({ signal }) => apiFetch<RootSummary[]>('/api/roots', { signal }),
  })
}

export function useRemovedFolders() {
  return useQuery({
    queryKey: queryKeys.removedFolders(),
    queryFn: ({ signal }) => apiFetch<RemovedFolder[]>('/api/folders/removed', { signal }),
  })
}
