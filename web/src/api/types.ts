export type FolderNode = {
  id: number
  name: string
  hasChildren: boolean
  imageCount: number
  isMissing: boolean
  isExcluded: boolean
}

export type BreadcrumbItem = { id: number; name: string }

export type FolderDetail = {
  id: number
  name: string
  rootId: number
  rootName: string
  relativePath: string
  imageCount: number
  isMissing: boolean
  breadcrumb: BreadcrumbItem[]
}

export type ImageListItem = {
  id: number
  folderId: number
  fileName: string
  extension: string
  width: number | null
  height: number | null
  /** Local-naive camera time without an offset, e.g. "2025-08-14T18:32:05". */
  dateTaken: string | null
  isFavorite: boolean
  thumbnailUrl: string | null
  previewUrl: string | null
  /** "{root name}/{relative path}", or just the root name for a root's top folder. */
  folderPath: string
}

export type AlbumRef = { id: number; name: string }

export type ImageDetail = ImageListItem & {
  fileSize: number
  fileModified: string
  orientation: number | null
  cameraMake: string | null
  cameraModel: string | null
  lensModel: string | null
  latitude: number | null
  longitude: number | null
  rawMetadata: unknown
  albums: AlbumRef[]
}

export type Page<T> = { items: T[]; nextCursor: string | null }

export type ProblemDetails = {
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}

export type AlbumSummary = {
  id: number
  name: string
  description: string | null
  imageCount: number
  coverThumbnailUrl: string | null
  updatedAt: string
}

export type AlbumDetail = {
  id: number
  name: string
  description: string | null
  imageCount: number
  createdAt: string
  updatedAt: string
}

/** Missing files stay listed, with null URLs. */
export type AlbumImageItem = ImageListItem & { isMissing: boolean }

export type AlbumAddResult = { added: number; skipped: number }

export type DuplicateGroup = { contentHash: string; count: number; images: ImageListItem[] }

export type JobStatus =
  'Pending' | 'Enumerating' | 'Enriching' | 'Completed' | 'Failed' | 'Cancelled'

export type DiscoveryProgress = {
  id: number
  status: JobStatus
  foldersDiscovered: number
  errorMessage: string | null
}

export type ScanProgress = {
  id: number
  status: JobStatus
  foldersScanned: number
  filesFound: number
  filesEnriched: number
  errorMessage: string | null
}
