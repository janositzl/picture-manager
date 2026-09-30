import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useTileSize } from './tileSize'

describe('useTileSize', () => {
  it('defaults to large', () => {
    const { result } = renderHook(() => useTileSize())
    expect(result.current[0]).toBe('large')
  })

  it('persists the chosen size across hook instances', () => {
    const { result: a } = renderHook(() => useTileSize())
    act(() => a.current[1]('small'))
    expect(a.current[0]).toBe('small')

    const { result: b } = renderHook(() => useTileSize())
    expect(b.current[0]).toBe('small')
  })

  it('falls back to large for an invalid stored value', () => {
    localStorage.setItem('pm.grid.tileSize', 'huge')
    const { result } = renderHook(() => useTileSize())
    expect(result.current[0]).toBe('large')
  })
})
