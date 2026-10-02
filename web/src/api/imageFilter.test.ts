import { describe, expect, it } from 'vitest'
import { toImageQuery } from './imageFilter'

describe('toImageQuery', () => {
  it('maps a folder filter', () =>
    expect(
      toImageQuery({ kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }, null).toString(),
    ).toBe('folderId=3&sort=date&order=desc&limit=100'))

  it('maps favorites and appends the cursor', () =>
    expect(toImageQuery({ kind: 'favorites', sort: 'name', order: 'asc' }, 'abc').toString()).toBe(
      'favoritesOnly=true&sort=name&order=asc&limit=100&cursor=abc',
    ))

  it('maps a search, scoped and unscoped', () => {
    expect(
      toImageQuery({ kind: 'search', q: 'a&b', sort: 'date', order: 'desc' }, null).get('fileName'),
    ).toBe('a&b')
    const scoped = toImageQuery(
      { kind: 'search', q: 'img', in: 7, sort: 'date', order: 'desc' },
      null,
    )
    expect(scoped.get('folderId')).toBe('7')
  })

  it('filters by person', () => {
    const params = toImageQuery({ kind: 'person', personId: 7, sort: 'date', order: 'desc' }, null)
    expect(params.get('personId')).toBe('7')
  })
})
