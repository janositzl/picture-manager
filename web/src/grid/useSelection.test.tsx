import { act, fireEvent, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useSelection } from './useSelection'

const ids = [1, 2, 3, 4, 5]
const sorted = (set: ReadonlySet<number>) => [...set].sort((a, b) => a - b)

describe('useSelection', () => {
  it('toggles single photos', () => {
    const { result } = renderHook(() => useSelection(ids, 'k'))
    act(() => result.current.toggle(2, { shift: false }))
    expect(result.current.isSelecting).toBe(true)
    act(() => result.current.toggle(2, { shift: false }))
    expect(result.current.count).toBe(0)
  })

  it('Shift selects the range from the last clicked photo, either direction', () => {
    const { result } = renderHook(() => useSelection(ids, 'k'))
    act(() => result.current.toggle(4, { shift: false }))
    act(() => result.current.toggle(2, { shift: true }))
    expect(sorted(result.current.selected)).toEqual([2, 3, 4])
  })

  it('Ctrl+A selects all and Esc clears, only while selecting', () => {
    const { result } = renderHook(() => useSelection(ids, 'k'))
    fireEvent.keyDown(window, { key: 'a', ctrlKey: true })
    expect(result.current.count).toBe(0)
    act(() => result.current.toggle(1, { shift: false }))
    fireEvent.keyDown(window, { key: 'a', ctrlKey: true })
    expect(result.current.count).toBe(5)
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(result.current.count).toBe(0)
  })

  it('starts empty again when the reset key changes', () => {
    const { result, rerender } = renderHook(({ key }) => useSelection(ids, key), {
      initialProps: { key: 'a' },
    })
    act(() => result.current.toggle(1, { shift: false }))
    rerender({ key: 'b' })
    expect(result.current.count).toBe(0)
  })
})
