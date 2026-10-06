import { describe, expect, it } from 'vitest'
import { faceCoverageLabel, faceCoverageState } from './faceCoverage'

const c = (total: number, done: number, failed = 0, stale = 0) => ({
  folderId: 1,
  total,
  done,
  failed,
  stale,
})

describe('faceCoverageState', () => {
  it.each([
    [undefined, 'none'],
    [c(0, 0), 'none'],
    [c(5, 0), 'notScanned'],
    [c(5, 2), 'partial'],
    [c(5, 0, 1), 'partial'],
    [c(5, 5), 'completed'],
    [c(5, 4, 1), 'completed'],
    [c(5, 5, 0, 0), 'completed'],
    [c(5, 3, 0, 2), 'needsRescan'],
    [c(5, 0, 0, 5), 'needsRescan'],
    [c(5, 1, 0, 1), 'needsRescan'],
  ] as const)('%o -> %s', (coverage, expected) => {
    expect(faceCoverageState(coverage)).toBe(expected)
  })
})

describe('faceCoverageLabel', () => {
  it('describes each state', () => {
    expect(faceCoverageLabel(undefined)).toBeNull()
    expect(faceCoverageLabel(c(1, 0))).toBe('Face detection: not scanned (1 photo)')
    expect(faceCoverageLabel(c(480, 300, 12))).toBe(
      'Face detection: partially scanned (312 of 480 photos)',
    )
    expect(faceCoverageLabel(c(480, 400, 0, 80))).toBe(
      'Face detection: needs rescan (80 of 480 photos out of date)',
    )
    expect(faceCoverageLabel(c(480, 480))).toBe('Face detection: completed (480 photos)')
    expect(faceCoverageLabel(c(480, 477, 3))).toBe(
      "Face detection: completed (480 photos, 3 couldn't be read)",
    )
  })
})
