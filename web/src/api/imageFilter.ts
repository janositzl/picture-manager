import type { Order, Sort } from '../routing/urlState'

export const PAGE_SIZE = 100

/** Folder view: only photos that have (or lack) a detected face. */
export type FacesFilter = 'with' | 'without'

/** What a grid shows. It is also the query key, so each view caches separately. */
export type ImageFilter = (
  | { kind: 'folder'; folderId: number; faces?: FacesFilter; includeHidden?: boolean }
  | { kind: 'favorites' }
  | { kind: 'search'; q: string; in?: number }
  | { kind: 'person'; personId: number; state?: 'confirmed' | 'suggested' }
) & { sort: Sort; order: Order }

/** Query string for GET /api/images. */
export function toImageQuery(filter: ImageFilter, cursor: string | null): URLSearchParams {
  const params = new URLSearchParams()
  switch (filter.kind) {
    case 'folder':
      params.set('folderId', String(filter.folderId))
      if (filter.faces !== undefined) params.set('faces', filter.faces)
      if (filter.includeHidden === true) params.set('includeHidden', 'true')
      break
    case 'favorites':
      params.set('favoritesOnly', 'true')
      break
    case 'search':
      if (filter.q !== '') params.set('fileName', filter.q)
      if (filter.in !== undefined) params.set('folderId', String(filter.in))
      break
    case 'person':
      params.set('personId', String(filter.personId))
      if (filter.state !== undefined) params.set('personState', filter.state)
      break
  }
  params.set('sort', filter.sort)
  params.set('order', filter.order)
  params.set('limit', String(PAGE_SIZE))
  if (cursor !== null) params.set('cursor', cursor)
  return params
}
