import type { Order, Sort } from '../routing/urlState'

export const PAGE_SIZE = 100

/** What a grid shows. It is also the query key, so each view caches separately. */
export type ImageFilter = (
  | { kind: 'folder'; folderId: number }
  | { kind: 'favorites' }
  | { kind: 'search'; q: string; in?: number }
) & { sort: Sort; order: Order }

/** Query string for GET /api/images. */
export function toImageQuery(filter: ImageFilter, cursor: string | null): URLSearchParams {
  const params = new URLSearchParams()
  switch (filter.kind) {
    case 'folder':
      params.set('folderId', String(filter.folderId))
      break
    case 'favorites':
      params.set('favoritesOnly', 'true')
      break
    case 'search':
      if (filter.q !== '') params.set('fileName', filter.q)
      if (filter.in !== undefined) params.set('folderId', String(filter.in))
      break
  }
  params.set('sort', filter.sort)
  params.set('order', filter.order)
  params.set('limit', String(PAGE_SIZE))
  if (cursor !== null) params.set('cursor', cursor)
  return params
}
