import type { AlbumAddResult } from '../api/types'

export const photoCount = (n: number): string => (n === 1 ? '1 photo' : `${n} photos`)

const were = (n: number) => (n === 1 ? 'was' : 'were')

export function addedMessage(result: AlbumAddResult, albumName: string, unavailable = 0): string {
  const notes: string[] = []
  if (result.skipped > 0) notes.push(`${result.skipped} ${were(result.skipped)} already there`)
  if (unavailable > 0) notes.push(`${unavailable} missing on disk ${were(unavailable)} skipped`)
  const suffix = notes.length > 0 ? ` (${notes.join('; ')})` : ''
  return `Added ${photoCount(result.added)} to ${albumName}${suffix}.`
}
