import { describe, expect, it } from 'vitest'
import { columnCount, tileSize } from './columns'

describe('columnCount', () => {
  it.each([
    [0, 1],
    [179, 1],
    [359, 1],
    [360, 2],
    [1200, 6],
  ])('%i px → %i columns', (width, expected) => expect(columnCount(width)).toBe(expected))
})

describe('tileSize', () => {
  it('fills the row, leaving the gaps', () => expect(tileSize(1200, 6)).toBe(196))
  it('never goes negative', () => expect(tileSize(0, 1)).toBe(0))
})
