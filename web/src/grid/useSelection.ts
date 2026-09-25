import { useEffect, useEffectEvent, useState } from 'react'

export type SelectMods = { shift: boolean }
export type Selection = {
  selected: ReadonlySet<number>
  count: number
  isSelecting: boolean
  toggle: (id: number, mods: SelectMods) => void
  selectAll: () => void
  clear: () => void
}

type State = { key: string; ids: ReadonlySet<number>; anchor: number | null }

/** In-memory selection over a grid, in grid order; starts empty again whenever `resetKey` changes. */
export function useSelection(orderedIds: readonly number[], resetKey: string): Selection {
  const [state, setState] = useState<State>({ key: resetKey, ids: new Set(), anchor: null })
  const current: State =
    state.key === resetKey ? state : { key: resetKey, ids: new Set(), anchor: null }
  const isSelecting = current.ids.size > 0

  const toggle = (id: number, { shift }: SelectMods) => {
    const ids = new Set(current.ids)
    const from = current.anchor === null ? -1 : orderedIds.indexOf(current.anchor)
    const to = orderedIds.indexOf(id)
    if (shift && from >= 0 && to >= 0) {
      for (const rangeId of orderedIds.slice(Math.min(from, to), Math.max(from, to) + 1))
        ids.add(rangeId)
    } else if (ids.has(id)) {
      ids.delete(id)
    } else {
      ids.add(id)
    }
    setState({ key: resetKey, ids, anchor: id })
  }

  const selectAll = () =>
    setState({ key: resetKey, ids: new Set(orderedIds), anchor: current.anchor })
  const clear = () => setState({ key: resetKey, ids: new Set(), anchor: null })

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (
      event.target instanceof HTMLElement &&
      event.target.closest('input:not([type="checkbox"]), textarea')
    )
      return
    if (event.key === 'Escape') {
      clear()
    } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') {
      event.preventDefault()
      selectAll()
    }
  })

  useEffect(() => {
    if (!isSelecting) return
    // Bubble phase: an open dialog (MUI Modal) stops Escape first, so Esc closes it, not the selection.
    const listener = (event: KeyboardEvent) => onKeyDown(event)
    window.addEventListener('keydown', listener)
    return () => window.removeEventListener('keydown', listener)
  }, [isSelecting])

  return { selected: current.ids, count: current.ids.size, isSelecting, toggle, selectAll, clear }
}
