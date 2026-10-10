import type { AlbumAccess, SharePermission } from '../api/types'

type HasAccess = { access: AlbumAccess }

/** Owners and editors may add, remove, reorder and pick the cover. */
export const canEdit = (album: HasAccess): boolean => album.access !== 'Viewer'

export const isOwner = (album: HasAccess): boolean => album.access === 'Owner'

/** How a share level reads in the UI; "Viewer"/"Editor" stay API words. */
export const PERMISSION_LABEL: Record<SharePermission, string> = {
  Viewer: 'Can view',
  Editor: 'Can edit',
}
