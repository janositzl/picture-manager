import { http, HttpResponse } from 'msw'
import type { ImageListItem } from '../api/types'
import { adminHandlers } from './adminHandlers'
import { albumHandlers, duplicateHandlers } from './albumHandlers'
import {
  childrenById,
  faceCoverage,
  folderDetails,
  holidaysImages,
  imageDetail,
  madeiraImages,
  rootFolders,
} from './fixtures'
import { jobHandlers } from './jobHandlers'

const allImages = (): ImageListItem[] => [...holidaysImages, ...madeiraImages]

let hiddenIds = new Set<number>()

/** Called before every test (setup.ts): nothing is hidden. */
export function resetHiddenStore(): void {
  hiddenIds = new Set()
}
const notFound = () => HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 })

/**
 * A stateless fake of the phase 5 API over the fixtures. Order matters: /roots and /removed before
 * the generic /:id, which would otherwise treat "removed" as a folder id.
 */
export const handlers = [
  http.get('/api/face-recognitions/coverage', () => HttpResponse.json(faceCoverage)),
  http.get('/api/folders/roots', () => HttpResponse.json(rootFolders)),
  http.get('/api/folders/:id/children', ({ params }) =>
    HttpResponse.json(childrenById[Number(params.id)] ?? []),
  ),
  ...adminHandlers,
  http.get('/api/folders/:id', ({ params }) => {
    const detail = folderDetails[Number(params.id)]
    return detail ? HttpResponse.json(detail) : notFound()
  }),
  http.get('/api/images', ({ request }) => {
    const query = new URL(request.url).searchParams
    let items = allImages().map((i) => ({ ...i, isHidden: hiddenIds.has(i.id) }))
    const folderId = query.get('folderId')
    // Like the real API: hidden photos are listed only for one folder with includeHidden=true.
    if (folderId === null || query.get('includeHidden') !== 'true') items = items.filter((i) => !i.isHidden)
    if (folderId !== null) items = items.filter((i) => i.folderId === Number(folderId))
    if (query.get('favoritesOnly') === 'true') items = items.filter((i) => i.isFavorite)
    const fileName = query.get('fileName')?.toLowerCase()
    if (fileName) items = items.filter((i) => i.fileName.toLowerCase().includes(fileName))
    return HttpResponse.json({ items, nextCursor: null })
  }),
  http.get('/api/images/:id/faces', () => HttpResponse.json([])),
  http.get('/api/images/:id', ({ params }) => {
    const item = allImages().find((i) => i.id === Number(params.id))
    return item ? HttpResponse.json(imageDetail(item)) : notFound()
  }),
  http.put('/api/images/hidden', async ({ request }) => {
    const body = (await request.json()) as { imageIds: number[]; isHidden: boolean }
    for (const id of body.imageIds) {
      if (body.isHidden) hiddenIds.add(id)
      else hiddenIds.delete(id)
    }
    return HttpResponse.json({ affected: body.imageIds.length })
  }),
  http.put('/api/images/:id/favorite', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/images/:id/favorite', () => new HttpResponse(null, { status: 204 })),
  ...albumHandlers,
  ...duplicateHandlers,
  ...jobHandlers,
]

/** Serves `pages` in order for GET /api/images, whatever the filter; cursor "p{n}" asks for page n. */
export function pagedImages(pages: ImageListItem[][]) {
  return http.get('/api/images', ({ request }) => {
    const cursor = new URL(request.url).searchParams.get('cursor')
    const index = cursor === null ? 0 : Number(cursor.slice(1))
    return HttpResponse.json({
      items: pages[index] ?? [],
      nextCursor: index + 1 < pages.length ? `p${index + 1}` : null,
    })
  })
}
