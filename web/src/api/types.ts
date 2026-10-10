export type FolderNode = {
  id: number
  name: string
  hasChildren: boolean
  isMissing: boolean
  isExcluded: boolean
  /** True once a scan of this folder has completed. */
  isScanned: boolean
}

/**
 * Face-detection coverage of a folder and its subfolders for the current face model. Folders with no
 * photos beneath them have no entry. stale = processed earlier, but by another model or for older content.
 */
export type FolderFaceCoverage = {
  folderId: number
  total: number
  done: number
  failed: number
  stale: number
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
  /** True when the file couldn't be decoded as an image during enrichment (corrupt or misnamed). */
  isInvalid: boolean
  /** Only true in the folder view's "Show hidden" list; hidden photos appear nowhere else. */
  isHidden?: boolean
  /** In a person's list: that person's face in this photo (for its crop). */
  faceId?: number | null
  /** Bytes; present on duplicate-group results (exact and similar). */
  fileSize?: number
}

export type AlbumAccess = 'Owner' | 'Editor' | 'Viewer'
export type SharePermission = 'Viewer' | 'Editor'

export type AlbumRef = { id: number; name: string; access: AlbumAccess }

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
  access: AlbumAccess
  ownerDisplayName: string
  /** How many people the caller shared it with; 0 unless they own it. */
  shareCount: number
}

export type AlbumDetail = {
  id: number
  name: string
  description: string | null
  imageCount: number
  createdAt: string
  updatedAt: string
  access: AlbumAccess
  ownerDisplayName: string
}

/** Missing files stay listed, with null URLs. */
export type AlbumImageItem = ImageListItem & { isMissing: boolean }

export type AlbumAddResult = { added: number; skipped: number }

export type AlbumShare = { userId: number; displayName: string; permission: SharePermission }

/** An active user other than the caller, as offered for sharing. */
export type DirectoryUser = { id: number; displayName: string }

export type SimilarGroup = {
  key: string
  count: number
  maxDistance: number
  images: ImageListItem[]
}

export type DuplicateGroup = { contentHash: string; count: number; images: ImageListItem[] }

export type JobStatus =
  'Pending' | 'Enumerating' | 'Enriching' | 'Completed' | 'Failed' | 'Cancelled'

export type ActiveJobDto = {
  kind: 'Scan' | 'Discovery' | 'FaceRecognition'
  id: number
  folderId: number | null
  status: JobStatus
  foldersProcessed: number
  filesFound: number
  filesEnriched: number
  facesFound: number
  errorMessage: string | null
}

export type FaceRecognitionProgress = {
  id: number
  status: JobStatus
  imagesFound: number
  imagesProcessed: number
  facesFound: number
  errorMessage: string | null
}

export type SettingsDto = {
  excludedFolderNames: string[]
  excludedExtensions: string[]
  includedExtensions: string[] | null
  /** The config-level ceiling (Scanning:SupportedExtensions): read-only, not editable through this API. */
  supportedExtensions: string[]
}

export type SettingsSaveResult = SettingsDto & { pruneOnNextScan: boolean }

export type RootSummary = {
  id: number
  name: string
  alias: string | null
  mountPath: string
  isActive: boolean
  exportSegment: string
}

export type RemovedFolder = { id: number; name: string; rootName: string; relativePath: string }

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

export type Me = {
  id: number
  username: string
  displayName: string
  role: 'Admin' | 'User'
  mustChangePassword: boolean
  canRunFolderActions: boolean
}

export type UserSummary = {
  id: number
  username: string
  displayName: string
  role: 'Admin' | 'User'
  isActive: boolean
  canRunFolderActions: boolean
  mustChangePassword: boolean
  createdAt: string
  lastLoginAt: string | null
  albumCount: number
}
