import { parseId } from '../routing/urlState'
import { readStored, writeStored } from '../shared/storage'

const LAST_FOLDER = 'pm.folders.lastSelected'

export const readLastFolderId = (): number | null => parseId(readStored(LAST_FOLDER))
export const writeLastFolderId = (id: number): void => writeStored(LAST_FOLDER, String(id))
