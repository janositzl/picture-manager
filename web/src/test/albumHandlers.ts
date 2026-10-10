import { http, HttpResponse, type PathParams } from 'msw'
import type {
  AlbumAccess,
  AlbumDetail,
  AlbumImageItem,
  AlbumShare,
  AlbumSummary,
  ImageListItem,
  SharePermission,
} from '../api/types'
import { currentMe } from './authHandlers'
import { userById } from './userHandlers'
import { albumSeed, duplicateGroups, holidaysImages, madeiraImages } from './fixtures'

type StoredAlbum = {
  id: number
  name: string
  description: string | null
  imageIds: number[]
  coverImageId?: number
  missing: Set<number>
  createdAt: string
  updatedAt: string
  owner: { id: number; displayName: string }
  shares: AlbumShare[]
}

let albums: StoredAlbum[] = []
let nextId = 100
let tick = 0

/** Called before every test (setup.ts), so each test starts from albumSeed. */
export function resetAlbumStore(): void {
  albums = albumSeed.map((album) => ({
    ...album,
    imageIds: [...album.imageIds],
    missing: new Set<number>(),
    createdAt: album.updatedAt,
    owner: { id: 1, displayName: 'Administrator' },
    shares: [],
  }))
  nextId = 100
  tick = 0
}
resetAlbumStore()

/** Lets tests inspect and adjust the fake server's albums. */
export const albumStore = {
  get: (id: number) => albums.find((album) => album.id === id),
  byName: (name: string) => albums.find((album) => album.name === name),
  /** An album Bob B (id 5) owns and shared with the signed-in admin. */
  addShared: (album: { id: number; name: string; permission: SharePermission; imageIds?: number[] }) => {
    albums.push({
      id: album.id,
      name: album.name,
      description: null,
      imageIds: [...(album.imageIds ?? [])],
      missing: new Set<number>(),
      createdAt: '2026-09-01T10:00:00.000Z',
      updatedAt: '2026-09-01T10:00:00.000Z',
      owner: { id: 5, displayName: 'Bob B' },
      shares: [{ userId: 1, displayName: 'Administrator', permission: album.permission }],
    })
  },
}

const library = (): ImageListItem[] => [...holidaysImages, ...madeiraImages]
const findImage = (id: number) => library().find((image) => image.id === id)
const notFound = () => HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 })
const albumFor = (params: PathParams) => albumStore.get(Number(params.id))

/** The signed-in user's access to the album, or null when it is neither theirs nor shared with them. */
const access = (album: StoredAlbum): AlbumAccess | null => {
  const me = currentMe()
  if (me === null) return null
  if (album.owner.id === me.id) return 'Owner'
  return album.shares.find((share) => share.userId === me.id)?.permission ?? null
}
const RANK: Record<AlbumAccess, number> = { Viewer: 0, Editor: 1, Owner: 2 }
const forbidden = () =>
  HttpResponse.json({ title: 'Forbidden', status: 403, detail: 'You can only view this album.' }, { status: 403 })

/** The album when the signed-in user may do this; otherwise the 404/403 response the server would send. */
function guard(params: PathParams, needed: AlbumAccess): StoredAlbum | Response {
  const album = albumFor(params)
  const level = album === undefined ? null : access(album)
  if (album === undefined || level === null) return notFound()
  return RANK[level] >= RANK[needed] ? album : forbidden()
}
/** Later than every seed date, and increasing, like a real clock. */
const now = () => new Date(Date.UTC(2026, 9, 1, 0, ++tick)).toISOString()

const summary = (album: StoredAlbum): AlbumSummary => {
  const first =
    album.coverImageId !== undefined && album.imageIds.includes(album.coverImageId)
      ? album.coverImageId
      : album.imageIds[0]
  return {
    id: album.id,
    name: album.name,
    description: album.description,
    imageCount: album.imageIds.length,
    coverThumbnailUrl: first === undefined ? null : `/api/images/${first}/thumbnail?v=H${first}`,
    updatedAt: album.updatedAt,
    access: access(album) ?? 'Viewer',
    ownerDisplayName: album.owner.displayName,
    shareCount: access(album) === 'Owner' ? album.shares.length : 0,
  }
}

const detail = (album: StoredAlbum): AlbumDetail => ({
  id: album.id,
  name: album.name,
  description: album.description,
  imageCount: album.imageIds.length,
  createdAt: album.createdAt,
  updatedAt: album.updatedAt,
  access: access(album) ?? 'Viewer',
  ownerDisplayName: album.owner.displayName,
})

function albumItem(album: StoredAlbum, id: number): AlbumImageItem[] {
  const image = findImage(id)
  if (image === undefined) return []
  const missing = album.missing.has(id)
  return [
    {
      ...image,
      isMissing: missing,
      thumbnailUrl: missing ? null : image.thumbnailUrl,
      previewUrl: missing ? null : image.previewUrl,
    },
  ]
}

function nameProblem(name: string | null | undefined, exceptId?: number) {
  const trimmed = (name ?? '').trim()
  if (trimmed === '') {
    return HttpResponse.json(
      {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { name: ['Name is required.'] },
      },
      { status: 400 },
    )
  }
  const me = currentMe()
  if (
    albums.some(
      (a) => a.id !== exceptId && a.owner.id === me?.id && a.name.toLowerCase() === trimmed.toLowerCase(),
    )
  ) {
    return HttpResponse.json(
      { title: 'Conflict', status: 409, detail: `An album named '${trimmed}' already exists.` },
      { status: 409 },
    )
  }
  return null
}

type NameBody = { name?: string | null; description?: string | null }

export const albumHandlers = [
  http.get('/api/albums', () => HttpResponse.json(albums.filter((a) => access(a) !== null).map(summary))),
  http.post('/api/albums', async ({ request }) => {
    const body = (await request.json()) as NameBody
    const problem = nameProblem(body.name)
    if (problem) return problem
    const created = now()
    const album: StoredAlbum = {
      id: nextId++,
      name: (body.name ?? '').trim(),
      description: body.description ?? null,
      imageIds: [],
      missing: new Set<number>(),
      createdAt: created,
      updatedAt: created,
      owner: { id: currentMe()?.id ?? 1, displayName: currentMe()?.displayName ?? 'Administrator' },
      shares: [],
    }
    albums.push(album)
    return HttpResponse.json(detail(album), { status: 201 })
  }),
  http.get('/api/albums/:id', ({ params }) => {
    const album = guard(params, 'Viewer')
    return album instanceof Response ? album : HttpResponse.json(detail(album))
  }),
  http.patch('/api/albums/:id', async ({ params, request }) => {
    const album = guard(params, 'Owner')
    if (album instanceof Response) return album
    const body = (await request.json()) as NameBody
    if (body.name !== undefined) {
      const problem = nameProblem(body.name, album.id)
      if (problem) return problem
      album.name = (body.name ?? '').trim()
    }
    if (body.description !== undefined) album.description = body.description
    album.updatedAt = now()
    return HttpResponse.json(detail(album))
  }),
  http.delete('/api/albums/:id', ({ params }) => {
    const album = guard(params, 'Owner')
    if (album instanceof Response) return album
    albums = albums.filter((a) => a !== album)
    return new HttpResponse(null, { status: 204 })
  }),
  http.get('/api/albums/:id/images', ({ params }) => {
    const album = guard(params, 'Viewer')
    if (album instanceof Response) return album
    return HttpResponse.json({
      items: album.imageIds.flatMap((id) => albumItem(album, id)),
      nextCursor: null,
    })
  }),
  http.post('/api/albums/:id/images', async ({ params, request }) => {
    const album = guard(params, 'Editor')
    if (album instanceof Response) return album
    const body = (await request.json()) as { imageIds?: number[]; folderId?: number }
    const candidates = [
      ...new Set(
        body.imageIds ??
          library()
            .filter((image) => image.folderId === body.folderId)
            .map((image) => image.id),
      ),
    ]
    const unknown = candidates.filter((id) => findImage(id) === undefined)
    if (unknown.length > 0) {
      return HttpResponse.json(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { imageIds: [`Unknown or unavailable image ids: ${unknown.join(', ')}.`] },
        },
        { status: 400 },
      )
    }
    const toAdd = candidates.filter((id) => !album.imageIds.includes(id))
    album.imageIds.push(...toAdd)
    if (toAdd.length > 0) album.updatedAt = now()
    return HttpResponse.json({ added: toAdd.length, skipped: candidates.length - toAdd.length })
  }),
  http.post('/api/albums/:id/images/remove', async ({ params, request }) => {
    const album = guard(params, 'Editor')
    if (album instanceof Response) return album
    const { imageIds } = (await request.json()) as { imageIds: number[] }
    album.imageIds = album.imageIds.filter((id) => !imageIds.includes(id))
    album.updatedAt = now()
    return new HttpResponse(null, { status: 204 })
  }),
  http.post('/api/albums/:id/images/:imageId/move', async ({ params, request }) => {
    const album = guard(params, 'Editor')
    if (album instanceof Response) return album
    const imageId = Number(params.imageId)
    const { afterImageId } = (await request.json()) as { afterImageId: number | null }
    if (!album.imageIds.includes(imageId)) {
      return HttpResponse.json({ title: 'Bad Request', status: 400 }, { status: 400 })
    }
    const order = album.imageIds.filter((id) => id !== imageId)
    order.splice(afterImageId === null ? 0 : order.indexOf(afterImageId) + 1, 0, imageId)
    album.imageIds = order
    album.updatedAt = now()
    return new HttpResponse(null, { status: 204 })
  }),
  http.post('/api/albums/:id/sort', async ({ params, request }) => {
    const album = guard(params, 'Editor')
    if (album instanceof Response) return album
    const { by } = (await request.json()) as { by: 'dateAsc' | 'dateDesc' | 'name' }
    const key = (id: number) =>
      by === 'name' ? (findImage(id)?.fileName ?? '') : (findImage(id)?.dateTaken ?? '')
    const sign = by === 'dateDesc' ? -1 : 1
    album.imageIds = [...album.imageIds].sort(
      (a, b) => sign * key(a).localeCompare(key(b)) || a - b,
    )
    album.updatedAt = now()
    return new HttpResponse(null, { status: 204 })
  }),
  http.put('/api/albums/:id/cover', async ({ params, request }) => {
    const album = guard(params, 'Editor')
    if (album instanceof Response) return album
    const { imageId } = (await request.json()) as { imageId: number }
    if (!album.imageIds.includes(imageId)) {
      return HttpResponse.json(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { imageId: ['Choose a photo from this album.'] },
        },
        { status: 400 },
      )
    }
    album.coverImageId = imageId
    album.updatedAt = now()
    return new HttpResponse(null, { status: 204 })
  }),
  http.get('/api/albums/:id/export', ({ params, request }) => {
    const album = guard(params, 'Viewer')
    if (album instanceof Response) return album
    const prefix = (new URL(request.url).searchParams.get('prefix') ?? '').replace(/\/+$/, '')
    const text = album.imageIds
      .flatMap((id) => {
        const image = findImage(id)
        return image ? [`${prefix}/${image.folderPath}/${image.fileName}${image.extension}\n`] : []
      })
      .join('')
    return new HttpResponse(text, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } })
  }),
  http.get('/api/albums/:id/shares', ({ params }) => {
    const album = guard(params, 'Owner')
    return album instanceof Response ? album : HttpResponse.json(album.shares)
  }),
  http.put('/api/albums/:id/shares/:userId', async ({ params, request }) => {
    const album = guard(params, 'Owner')
    if (album instanceof Response) return album
    const userId = Number(params.userId)
    const { permission } = (await request.json()) as { permission: SharePermission }
    const target = userById(userId)
    if (target === undefined || !target.isActive || userId === currentMe()?.id) {
      return HttpResponse.json(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { userId: ['Choose an active user.'] },
        },
        { status: 400 },
      )
    }
    const share = { userId, displayName: target.displayName, permission }
    album.shares = [...album.shares.filter((s) => s.userId !== userId), share].sort((a, b) =>
      a.displayName.localeCompare(b.displayName),
    )
    return HttpResponse.json(share)
  }),
  http.delete('/api/albums/:id/shares/:userId', ({ params }) => {
    const album = guard(params, 'Viewer')
    if (album instanceof Response) return album
    const userId = Number(params.userId)
    if (access(album) !== 'Owner' && userId !== currentMe()?.id) return forbidden()
    if (!album.shares.some((s) => s.userId === userId)) return notFound()
    album.shares = album.shares.filter((s) => s.userId !== userId)
    return new HttpResponse(null, { status: 204 })
  }),
]

/** Two pages of one group each, so tests can see "load more". */
export const duplicateHandlers = [
  http.get('/api/duplicates', ({ request }) =>
    new URL(request.url).searchParams.get('cursor') === null
      ? HttpResponse.json({ items: duplicateGroups.slice(0, 1), nextCursor: 'd1' })
      : HttpResponse.json({ items: duplicateGroups.slice(1), nextCursor: null }),
  ),
]
