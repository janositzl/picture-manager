import { parseId } from '../routing/urlState'
import { readStored, writeStored } from '../shared/storage'

const LAST_USED = 'pm.albums.lastUsed'
const EXPORT_PREFIX = 'pm.albums.exportPrefix'
const SHOW_FOLDERS = 'pm.albums.showFolders'

export const readLastUsedAlbum = (): number | null => parseId(readStored(LAST_USED))
export const writeLastUsedAlbum = (id: number | null): void =>
  writeStored(LAST_USED, id === null ? null : String(id))

export const readExportPrefix = (): string => readStored(EXPORT_PREFIX) ?? ''
export const writeExportPrefix = (prefix: string): void =>
  writeStored(EXPORT_PREFIX, prefix === '' ? null : prefix)

export const readShowFolders = (): boolean => readStored(SHOW_FOLDERS) === 'true'
export const writeShowFolders = (show: boolean): void => writeStored(SHOW_FOLDERS, String(show))
