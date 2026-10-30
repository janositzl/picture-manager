import { parseId } from '../routing/urlState'
import { readStored, writeStored } from '../shared/storage'

const LAST_FOLDER = 'pm.folders.lastSelected'
const GROUP_MODE = 'pm.folders.group'

export const readLastFolderId = (): number | null => parseId(readStored(LAST_FOLDER))
export const writeLastFolderId = (id: number): void => writeStored(LAST_FOLDER, String(id))

export type GroupMode = 'day' | 'all'

/** The folder view's last chosen grouping, used when the URL has no ?group=. */
export const readGroupMode = (): GroupMode => (readStored(GROUP_MODE) === 'day' ? 'day' : 'all')
export const writeGroupMode = (mode: GroupMode): void => writeStored(GROUP_MODE, mode)
