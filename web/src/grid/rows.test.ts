import { describe, expect, it } from 'vitest'
import type { ImageListItem } from '../api/types'
import { buildRows, dayOf } from './rows'

const item = (id: number, sortDate: string) => ({ id, sortDate }) as ImageListItem

describe('dayOf', () => {
  it('is the date part of sortDate, with no time zone shift', () =>
    expect(dayOf(item(1, '2025-08-14T23:59:59Z'))).toBe('2025-08-14'))
})

describe('buildRows', () => {
  const items = [1, 2, 3, 4, 5].map((id) => item(id, '2025-08-14T10:00:00Z'))

  it('without grouping, chunks the items like a plain grid', () => {
    expect(buildRows(items, 2)).toEqual([
      { kind: 'tiles', start: 0, end: 2, group: null },
      { kind: 'tiles', start: 2, end: 4, group: null },
      { kind: 'tiles', start: 4, end: 5, group: null },
    ])
  })

  it('has no rows for no items', () => expect(buildRows([], 3, dayOf)).toEqual([]))

  it('puts a header before each day and never mixes days in one row', () => {
    const mixed = [
      item(1, '2025-08-14T08:00:00Z'),
      item(2, '2025-08-14T09:00:00Z'),
      item(3, '2025-08-14T10:00:00Z'),
      item(4, '2025-08-15T08:00:00Z'),
    ]

    expect(buildRows(mixed, 2, dayOf)).toEqual([
      { kind: 'header', group: '2025-08-14' },
      { kind: 'tiles', start: 0, end: 2, group: '2025-08-14' },
      { kind: 'tiles', start: 2, end: 3, group: '2025-08-14' },
      { kind: 'header', group: '2025-08-15' },
      { kind: 'tiles', start: 3, end: 4, group: '2025-08-15' },
    ])
  })

  it('treats a column count below one as one', () =>
    expect(buildRows(items.slice(0, 2), 0)).toHaveLength(2))
})
