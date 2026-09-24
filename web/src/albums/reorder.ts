import type { InfiniteData } from '@tanstack/react-query'
import type { Page } from '../api/types'

/** Where `activeId` lands when dropped on `overId`, and the API's "move after" anchor (null = front). */
export function planMove(
  ids: readonly number[],
  activeId: number,
  overId: number,
): { order: number[]; afterImageId: number | null } | null {
  const from = ids.indexOf(activeId)
  const to = ids.indexOf(overId)
  if (from < 0 || to < 0 || from === to) return null
  const order = [...ids]
  order.splice(from, 1)
  order.splice(to, 0, activeId)
  const at = order.indexOf(activeId)
  return { order, afterImageId: at === 0 ? null : (order[at - 1] ?? null) }
}

/** Puts the cached items in `order`, keeping each page's size so page params stay aligned. */
export function reorderPages<T extends { id: number }>(
  data: InfiniteData<Page<T>, string | null>,
  order: readonly number[],
): InfiniteData<Page<T>, string | null> {
  const byId = new Map(data.pages.flatMap((page) => page.items).map((item) => [item.id, item]))
  const sorted = order.flatMap((id) => {
    const item = byId.get(id)
    return item === undefined ? [] : [item]
  })
  let offset = 0
  return {
    ...data,
    pages: data.pages.map((page) => {
      const items = sorted.slice(offset, offset + page.items.length)
      offset += page.items.length
      return { ...page, items }
    }),
  }
}
