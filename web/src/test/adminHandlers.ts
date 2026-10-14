import { http, HttpResponse } from 'msw'
import type { AppVersionInfo, LogEntry, RemovedFolder, RootSummary, SettingsDto } from '../api/types'

const notFound = () => HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 })

const settingsSeed: SettingsDto = {
  excludedFolderNames: ['@eaDir'],
  excludedExtensions: ['.tmp'],
  includedExtensions: null,
  supportedExtensions: ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp', '.tif', '.tiff', '.heic', '.heif'],
}

export const systemInfoSeed: AppVersionInfo = {
  version: '1.2.3',
  buildTimeUtc: '2026-10-14T08:30:00Z',
  startedUtc: '2026-10-14T09:00:00Z',
  environment: 'Production',
  runtime: '.NET 10.0.0',
}

export const logEntriesSeed: LogEntry[] = [
  { timestamp: '2026-10-14 09:00:01.000 +00:00', level: 'Information', message: 'Starting PictureManager.Api', exception: null },
  { timestamp: '2026-10-14 09:05:00.000 +00:00', level: 'Warning', message: 'Root is unavailable', exception: null },
  {
    timestamp: '2026-10-14 09:06:00.000 +00:00',
    level: 'Error',
    message: 'Scan failed',
    exception: 'System.IO.IOException: disk gone\n   at Scanner.Run()',
  },
]

const rootsSeed: RootSummary[] = [
  {
    id: 1,
    name: 'dev',
    alias: null,
    mountPath: '/images/dev',
    isActive: true,
    exportSegment: 'dev',
  },
  {
    id: 2,
    name: 'archive',
    alias: 'family_photos',
    mountPath: '/images/archive',
    isActive: false,
    exportSegment: 'family_photos',
  },
]

const removedFoldersSeed: RemovedFolder[] = [
  { id: 40, name: 'Old', rootName: 'dev', relativePath: 'Holidays/Old' },
]

let settings: SettingsDto
let roots: RootSummary[]
let removedFolders: RemovedFolder[]
let nextRootId: number

/** Called before every test (setup.ts), so each test starts from the seeds above. */
export function resetAdminStore(): void {
  settings = { ...settingsSeed }
  roots = rootsSeed.map((root) => ({ ...root }))
  removedFolders = removedFoldersSeed.map((folder) => ({ ...folder }))
  nextRootId = 100
}
resetAdminStore()

export const adminStore = {
  root: (id: number) => roots.find((r) => r.id === id),
}

type SettingsBody = {
  excludedFolderNames?: (string | null)[] | null
  excludedExtensions?: (string | null)[] | null
  includedExtensions?: (string | null)[] | null
}

function normalize(values: (string | null)[] | null | undefined): string[] {
  const cleaned = (values ?? []).flatMap((v) => (v ?? '').trim() === '' ? [] : [v!.trim()])
  return [...new Set(cleaned.map((v) => v.toLowerCase()))].map(
    (lower) => cleaned.find((v) => v.toLowerCase() === lower)!,
  )
}

type RootPatchBody = { name?: string; alias?: string | null; isActive?: boolean }
type RootCreateBody = { name?: string; mountPath?: string; alias?: string | null }

export const adminHandlers = [
  http.post('/api/roots', async ({ request }) => {
    const body = (await request.json()) as RootCreateBody
    const name = (body.name ?? '').trim()
    const mountPath = (body.mountPath ?? '').trim()
    if (name === '') {
      return HttpResponse.json(
        { title: 'Bad Request', status: 400, errors: { name: ['Name is required.'] } },
        { status: 400 },
      )
    }
    if (mountPath === '') {
      return HttpResponse.json(
        { title: 'Bad Request', status: 400, errors: { mountPath: ['Mount path is required.'] } },
        { status: 400 },
      )
    }
    const alias = body.alias == null || body.alias.trim() === '' ? null : body.alias.trim()
    const segment = alias ?? name
    if (
      roots.some((r) => r.mountPath === mountPath) ||
      roots.some((r) => r.name.toLowerCase() === name.toLowerCase()) ||
      roots.some((r) => r.exportSegment.toLowerCase() === segment.toLowerCase())
    ) {
      return HttpResponse.json(
        { title: 'Conflict', status: 409, detail: `A root named '${name}' already exists.` },
        { status: 409 },
      )
    }
    const root: RootSummary = {
      id: nextRootId++,
      name,
      alias,
      mountPath,
      isActive: true,
      exportSegment: segment,
    }
    roots.push(root)
    return HttpResponse.json(root, { status: 201 })
  }),
  http.get('/api/settings', () => HttpResponse.json(settings)),
  http.put('/api/settings', async ({ request }) => {
    const body = (await request.json()) as SettingsBody
    const excludedFolderNames = normalize(body.excludedFolderNames)
    const excludedExtensions = normalize(body.excludedExtensions).map((v) => v.toLowerCase())
    const includedExtensions =
      body.includedExtensions === null || body.includedExtensions === undefined
        ? null
        : normalize(body.includedExtensions).map((v) => v.toLowerCase())
    const pruneOnNextScan =
      excludedFolderNames.length > settings.excludedFolderNames.length ||
      excludedExtensions.length > settings.excludedExtensions.length
    settings = {
      excludedFolderNames,
      excludedExtensions,
      includedExtensions,
      supportedExtensions: settings.supportedExtensions,
    }
    return HttpResponse.json({ ...settings, pruneOnNextScan })
  }),
  http.get('/api/roots', () => HttpResponse.json(roots)),
  http.patch('/api/roots/:id', async ({ params, request }) => {
    const root = adminStore.root(Number(params.id))
    if (!root) return notFound()
    const body = (await request.json()) as RootPatchBody
    if (body.name !== undefined) {
      const trimmed = body.name.trim()
      if (trimmed === '') {
        return HttpResponse.json(
          { title: 'Bad Request', status: 400, errors: { name: ['Name is required.'] } },
          { status: 400 },
        )
      }
      if (roots.some((r) => r.id !== root.id && r.name.toLowerCase() === trimmed.toLowerCase())) {
        return HttpResponse.json(
          { title: 'Conflict', status: 409, detail: `A root named '${trimmed}' already exists.` },
          { status: 409 },
        )
      }
      root.name = trimmed
    }
    if ('alias' in body) {
      const alias = body.alias === null ? null : body.alias!.trim() || null
      root.alias = alias
      root.exportSegment = alias ?? root.name
    }
    if (body.isActive !== undefined) root.isActive = body.isActive
    return HttpResponse.json(root)
  }),
  http.delete('/api/roots/:id', ({ params }) => {
    const id = Number(params.id)
    if (!roots.some((r) => r.id === id)) return notFound()
    roots = roots.filter((r) => r.id !== id)
    return new HttpResponse(null, { status: 204 })
  }),
  http.get('/api/folders/removed', () => HttpResponse.json(removedFolders)),
  http.post('/api/folders/:id/restore', ({ params }) => {
    const id = Number(params.id)
    if (!removedFolders.some((f) => f.id === id)) return notFound()
    removedFolders = removedFolders.filter((f) => f.id !== id)
    return new HttpResponse(null, { status: 204 })
  }),
  http.delete('/api/folders/:id', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/folders/:id/removed', ({ params }) => {
    const id = Number(params.id)
    if (!removedFolders.some((f) => f.id === id)) return notFound()
    removedFolders = removedFolders.filter((f) => f.id !== id)
    return new HttpResponse(null, { status: 204 })
  }),
  http.get('/api/system/info', () => HttpResponse.json(systemInfoSeed)),
  http.get('/api/system/logs', ({ request }) => {
    const warningsOnly = new URL(request.url).searchParams.get('minLevel') === 'Warning'
    const entries = warningsOnly
      ? logEntriesSeed.filter((e) => e.level === 'Warning' || e.level === 'Error' || e.level === 'Fatal')
      : logEntriesSeed
    return HttpResponse.json({ fileName: 'picturemanager-20261014.log', entries, truncated: false })
  }),
]
