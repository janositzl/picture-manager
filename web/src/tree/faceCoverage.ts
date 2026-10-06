import type { FolderFaceCoverage } from '../api/types'

export type FaceCoverageState = 'none' | 'notScanned' | 'partial' | 'needsRescan' | 'completed'

/**
 * Failed (given-up) photos count as processed: re-running the job won't change them. Any stale photo
 * (processed by another model or for older content) outranks the rest, since it needs attention.
 */
export function faceCoverageState(coverage: FolderFaceCoverage | undefined): FaceCoverageState {
  if (!coverage || coverage.total === 0) return 'none'
  if (coverage.stale > 0) return 'needsRescan'
  const processed = coverage.done + coverage.failed
  if (processed === 0) return 'notScanned'
  return processed < coverage.total ? 'partial' : 'completed'
}

export function faceCoverageLabel(coverage: FolderFaceCoverage | undefined): string | null {
  const state = faceCoverageState(coverage)
  if (state === 'none' || !coverage) return null
  const photos = (n: number) => `${n} ${n === 1 ? 'photo' : 'photos'}`
  switch (state) {
    case 'notScanned':
      return `Face detection: not scanned (${photos(coverage.total)})`
    case 'partial':
      return `Face detection: partially scanned (${coverage.done + coverage.failed} of ${photos(coverage.total)})`
    case 'needsRescan':
      return `Face detection: needs rescan (${coverage.stale} of ${photos(coverage.total)} out of date)`
    case 'completed':
      return coverage.failed > 0
        ? `Face detection: completed (${photos(coverage.total)}, ${coverage.failed} couldn't be read)`
        : `Face detection: completed (${photos(coverage.total)})`
  }
}
