export type Sort = 'date' | 'name'
export type Order = 'asc' | 'desc'

export type GridParams = { sort: Sort; order: Order; image: number | null }
export type SearchParamsState = { q: string; in: number | null }

/** The API's default direction for each sort. */
export function defaultOrder(sort: Sort): Order {
  return sort === 'date' ? 'desc' : 'asc'
}

/** A positive integer id, or null for anything else ("", "0", "-3", "1.5", "07", "abc", overflow). */
export function parseId(value: string | null): number | null {
  if (value === null || !/^[1-9][0-9]*$/.test(value)) return null
  const id = Number(value)
  return Number.isSafeInteger(id) ? id : null
}

/** Invalid sort/order values fall back to the defaults; an invalid image id is dropped. */
export function parseGridParams(params: URLSearchParams): GridParams {
  const sort: Sort = params.get('sort') === 'date' ? 'date' : 'name'
  const rawOrder = params.get('order')
  const order: Order = rawOrder === 'asc' || rawOrder === 'desc' ? rawOrder : defaultOrder(sort)
  return { sort, order, image: parseId(params.get('image')) }
}

export function parseSearchState(params: URLSearchParams): SearchParamsState {
  return { q: (params.get('q') ?? '').trim(), in: parseId(params.get('in')) }
}

/** A copy of `current` with each change applied; null or '' removes the key. */
export function withParams(
  current: URLSearchParams,
  changes: Record<string, string | number | null>,
): URLSearchParams {
  const next = new URLSearchParams(current)
  for (const [key, value] of Object.entries(changes)) {
    if (value === null || value === '') next.delete(key)
    else next.set(key, String(value))
  }
  return next
}
