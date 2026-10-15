import { describe, expect, it } from 'vitest'
import { toImageIdsQuery, toImageQuery } from './imageFilter'

describe('toImageQuery', () => {
  it('maps a folder filter', () =>
    expect(
      toImageQuery({ kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }, null).toString(),
    ).toBe('folderId=3&sort=date&order=desc&limit=100'))

  it('adds includeHidden for a folder filter that asks for it', () => {
    const params = toImageQuery(
      { kind: 'folder', folderId: 3, includeHidden: true, sort: 'date', order: 'desc' },
      null,
    )
    expect(params.get('includeHidden')).toBe('true')
    expect(
      toImageQuery({ kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }, null).has(
        'includeHidden',
      ),
    ).toBe(false)
  })

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

  it('sends one withPersonId per person to require all of them', () => {
    const params = toImageQuery(
      { kind: 'person', personId: 7, withPersonIds: [3, 9], sort: 'date', order: 'desc' },
      null,
    )
    expect(params.getAll('withPersonId')).toEqual(['3', '9'])
  })

  it('builds the ids query without paging', () => {
    const params = toImageIdsQuery({ kind: 'folder', folderId: 3, sort: 'name', order: 'asc' })
    expect(params.get('folderId')).toBe('3')
    expect(params.get('sort')).toBe('name')
    expect(params.has('limit')).toBe(false)
    expect(params.has('cursor')).toBe(false)
  })
})
