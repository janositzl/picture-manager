import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { apiFetch } from './client'
import { toImageQuery, type ImageFilter } from './imageFilter'
import type { FolderDetail, FolderNode, ImageDetail, ImageListItem, Page } from './types'

export const queryKeys = {
  rootFolders: () => ['folders', 'roots'] as const,
  folderChildren: (id: number) => ['folders', id, 'children'] as const,
  folder: (id: number) => ['folders', id, 'detail'] as const,
  imageLists: () => ['images', 'list'] as const,
  images: (filter: ImageFilter) => ['images', 'list', filter] as const,
  image: (id: number) => ['images', 'detail', id] as const,
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
