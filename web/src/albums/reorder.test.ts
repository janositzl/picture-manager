import { describe, expect, it } from 'vitest'
import { planMove, reorderPages } from './reorder'

describe('planMove', () => {
  it.each([
    [1, 3, [2, 3, 1, 4], 3],
    [4, 1, [4, 1, 2, 3], null],
    [2, 4, [1, 3, 4, 2], 4],
  ])('moving %i onto %i gives %j after %s', (active, over, order, after) =>
    expect(planMove([1, 2, 3, 4], active, over)).toEqual({ order, afterImageId: after }),
  )

  it('is a no-op onto itself or an unknown id', () => {
    expect(planMove([1, 2], 1, 1)).toBeNull()
    expect(planMove([1, 2], 1, 9)).toBeNull()
  })
})

describe('reorderPages', () => {
  it('keeps the page sizes and page params', () => {
    const data = {
      pages: [
        { items: [{ id: 1 }, { id: 2 }], nextCursor: 'c' },
        { items: [{ id: 3 }], nextCursor: null },
      ],
      pageParams: [null, 'c'],
    }
    const result = reorderPages(data, [3, 1, 2])
    expect(result.pages.map((p) => p.items.map((i) => i.id))).toEqual([[3, 1], [2]])
    expect(result.pageParams).toEqual([null, 'c'])
  })
})
