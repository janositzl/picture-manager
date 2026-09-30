import { describe, expect, it } from 'vitest'
import { columnCount, tileSize } from './columns'

describe('columnCount', () => {
  it.each([
    [0, 180, 1],
    [179, 180, 1],
    [359, 180, 1],
    [360, 180, 2],
    [1200, 180, 6],
    [299, 150, 1],
    [300, 150, 2],
    [199, 100, 1],
    [200, 100, 2],
  ])('%i px, %i min width → %i columns', (width, minTileWidth, expected) =>
    expect(columnCount(width, minTileWidth)).toBe(expected),
  )
})

describe('tileSize', () => {
  it('fills the row, leaving the gaps', () => expect(tileSize(1200, 6)).toBe(196))
  it('never goes negative', () => expect(tileSize(0, 1)).toBe(0))
})
