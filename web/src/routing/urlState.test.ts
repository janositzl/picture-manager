import { describe, expect, it } from 'vitest'
import {
  parseGridParams,
  parseHiddenParam,
  parseId,
  parseSearchState,
  withParams,
} from './urlState'

const params = (search: string) => new URLSearchParams(search)

describe('parseId', () => {
  it.each([
    ['7', 7],
    ['123', 123],
  ])('accepts %s', (value, expected) => expect(parseId(value)).toBe(expected))

  it.each([null, '', '0', '-1', '1.5', 'abc', '07', '99999999999999999999'])(
    'rejects %s',
    (value) => expect(parseId(value)).toBeNull(),
  )
})

describe('parseGridParams', () => {
  it('defaults to name, ascending, no image', () =>
    expect(parseGridParams(params(''))).toEqual({ sort: 'name', order: 'asc', image: null }))

  it('defaults date sorts to ascending', () =>
    expect(parseGridParams(params('?sort=date'))).toEqual({
      sort: 'date',
      order: 'asc',
      image: null,
    }))

  it('defaults name sorts to ascending', () =>
    expect(parseGridParams(params('?sort=name'))).toEqual({
      sort: 'name',
      order: 'asc',
      image: null,
    }))

  it('keeps an explicit order', () =>
    expect(parseGridParams(params('?sort=name&order=desc&image=5'))).toEqual({
      sort: 'name',
      order: 'desc',
      image: 5,
    }))

  it('falls back on invalid values and drops an invalid image id', () =>
    expect(parseGridParams(params('?sort=size&order=up&image=abc'))).toEqual({
      sort: 'name',
      order: 'asc',
      image: null,
    }))
})

describe('parseSearchState', () => {
  it('trims the query and parses the scope', () =>
    expect(parseSearchState(params('?q=%20img%20&in=3'))).toEqual({ q: 'img', in: 3 }))

  it('treats a missing query as empty and drops an invalid scope', () =>
    expect(parseSearchState(params('?in=x'))).toEqual({ q: '', in: null }))
})

describe('withParams', () => {
  it('sets, replaces and removes keys without touching the original', () => {
    const current = params('?sort=name&image=4')
    const next = withParams(current, { image: 9, order: 'desc', sort: null })
    expect(next.toString()).toBe('image=9&order=desc')
    expect(current.toString()).toBe('sort=name&image=4')
  })

  it('removes a key set to an empty string', () =>
    expect(withParams(params('?q=x'), { q: '' }).toString()).toBe(''))
})

describe('parseHiddenParam', () => {
  it('is true for ?hidden=1 only', () => {
    expect(parseHiddenParam(params('hidden=1'))).toBe(true)
    expect(parseHiddenParam(params('hidden=0'))).toBe(false)
    expect(parseHiddenParam(params(''))).toBe(false)
  })
})
