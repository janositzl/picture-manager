# Marquee (Drag-a-Box) Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user select many photos with one drag gesture: press on empty grid space, drag a rectangle, every touched tile joins the selection.

**Architecture:** A pure hit-test (`tileIndicesInRect`) maps a rectangle in scroll-content coordinates to tile indices using the same layout math both grids already share (`columns`, `size`, `TILE_GAP`, `GRID_PADDING`), so it works on the virtualized `PhotoGrid` and the plain-DOM `AlbumGrid` alike. A `useMarquee` hook owns pointer handling, edge auto-scroll and the overlay box, and pushes results into the existing `useSelection` through one new method, `replace`. The existing selection bar (Add to album / Remove / Clear) is reused unchanged.

**Tech Stack:** React 19, TypeScript, Vite, MUI, Tailwind v4, Vitest + Testing Library (jsdom 30), msw.

**Spec:** No spec file (bounded change). Design approved in chat on 2026-09-30; its content is the Goal/Architecture above plus the Global Constraints below.

## Global Constraints

- A marquee starts only on empty grid space. A press on a tile, the tile checkbox or the star button must behave exactly as today (open, toggle, favorite, dnd-kit drag-reorder in albums).
- No marquee until the pointer has moved 4px; a plain click on empty space does nothing.
- Ctrl/Cmd/Shift held at press time makes the marquee additive (keeps the existing selection); otherwise it replaces it.
- Shrinking the box back deselects tiles it no longer touches (selection = base ∪ currently-hit).
- Dragging near the top/bottom edge (48px) of the grid auto-scrolls; the box keeps tracking.
- Mouse/pen only; touch is out of scope. No "invert selection" and no select-mode toggle (YAGNI).
- Frontend only: `/web`. No new dependencies. Follow existing style (no semicolons, single quotes, 2-space indent, Tailwind + MUI `sx`).
- Commands run from `C:\Work\PictureManager\web`. Keep any build/test error output under ~20 lines.

## Review Focus

- **Press on a tile/checkbox/star must not start a marquee** (would break open, toggle and album reorder). Pinned in Task 3 (hook) and Task 4 (album integration).
- **Press on the scrollbar** (right gutter) must not start a marquee. Pinned in Task 3.
- **Tiny jitter on a click** (<4px) must not change the selection. Pinned in Task 3.
- **Box shrinking** must deselect tiles that left the box, while keeping the pre-drag selection when additive. Pinned in Task 3.
- **Partially loaded virtualized grid / last row not full**: indices past `count` must never be returned (no `undefined` ids fed into the selection). Pinned in Task 1.

## File Structure

- Create `web/src/grid/marquee.ts` — pure geometry: `Rect`, `GridLayout`, `normalizeRect`, `tileIndicesInRect`.
- Create `web/src/grid/marquee.test.ts` — unit tests for the geometry.
- Modify `web/src/grid/useSelection.ts` — add `replace(ids)`.
- Modify `web/src/grid/useSelection.test.tsx` — test `replace`.
- Create `web/src/grid/useMarquee.tsx` — `useMarquee` hook + `MarqueeBox` overlay component.
- Create `web/src/grid/useMarquee.test.tsx` — hook behavior tests.
- Modify `web/src/test/setup.ts` — jsdom gap: `setPointerCapture` / `releasePointerCapture` stubs.
- Modify `web/src/grid/PhotoGrid.tsx`, `web/src/views/ImageBrowser.tsx` — wire into the virtualized grid.
- Modify `web/src/albums/AlbumGrid.tsx` — wire into the album grid.
- Create `web/src/albums/AlbumMarquee.test.tsx`, `web/src/views/Marquee.test.tsx` — integration tests.

---

### Task 1: Hit-test geometry

**Files:**
- Create: `web/src/grid/marquee.ts`
- Test: `web/src/grid/marquee.test.ts`

**Interfaces:**
- Consumes: `GRID_PADDING`, `TILE_GAP` from `./columns` (8 and 4).
- Produces:
  - `type Rect = { left: number; top: number; right: number; bottom: number }`
  - `type GridLayout = { columns: number; size: number; count: number }`
  - `normalizeRect(ax: number, ay: number, bx: number, by: number): Rect`
  - `tileIndicesInRect(rect: Rect, layout: GridLayout): number[]` — row-major tile indices (`row * columns + col`) whose box overlaps `rect`; coordinates are relative to the scroll container's top-left including scroll offset (so tile 0 spans `GRID_PADDING..GRID_PADDING+size`).

- [ ] **Step 1: Write the failing test**

Create `web/src/grid/marquee.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { normalizeRect, tileIndicesInRect } from './marquee'

// size 100, gap 4, padding 8 => columns at x 8-108, 112-212, 216-316; rows at y 8-108, 112-212, ...
const layout = { columns: 3, size: 100, count: 7 }
const rect = (left: number, top: number, right: number, bottom: number) => ({
  left,
  top,
  right,
  bottom,
})

describe('normalizeRect', () => {
  it('orders the corners whichever way the drag went', () => {
    expect(normalizeRect(50, 40, 10, 5)).toEqual(rect(10, 5, 50, 40))
  })
})

describe('tileIndicesInRect', () => {
  it('finds a single tile', () => {
    expect(tileIndicesInRect(rect(0, 0, 10, 10), layout)).toEqual([0])
  })

  it('finds tiles a box straddles', () => {
    expect(tileIndicesInRect(rect(100, 0, 120, 10), layout)).toEqual([0, 1])
    expect(tileIndicesInRect(rect(100, 100, 120, 120), layout)).toEqual([0, 1, 3, 4])
  })

  it('finds nothing when the box sits entirely in a gap', () => {
    expect(tileIndicesInRect(rect(109, 20, 111, 30), layout)).toEqual([])
  })

  it('never returns an index past the last tile (short last row)', () => {
    expect(tileIndicesInRect(rect(0, 0, 5000, 5000), layout)).toEqual([0, 1, 2, 3, 4, 5, 6])
    expect(tileIndicesInRect(rect(216, 216, 5000, 5000), layout)).toEqual([])
  })

  it('returns nothing for an empty or degenerate grid', () => {
    const all = rect(0, 0, 5000, 5000)
    expect(tileIndicesInRect(all, { columns: 3, size: 100, count: 0 })).toEqual([])
    expect(tileIndicesInRect(all, { columns: 3, size: 0, count: 5 })).toEqual([])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/grid/marquee.test.ts`
Expected: FAIL (cannot resolve `./marquee`).

- [ ] **Step 3: Write minimal implementation**

Create `web/src/grid/marquee.ts`:

```ts
import { GRID_PADDING, TILE_GAP } from './columns'

export type Rect = { left: number; top: number; right: number; bottom: number }
export type GridLayout = { columns: number; size: number; count: number }

export function normalizeRect(ax: number, ay: number, bx: number, by: number): Rect {
  return {
    left: Math.min(ax, bx),
    top: Math.min(ay, by),
    right: Math.max(ax, bx),
    bottom: Math.max(ay, by),
  }
}

/** Index range [first, last] of the cells along one axis that overlap [from, to); empty when first > last. */
function cellRange(from: number, to: number, size: number, cells: number): [number, number] {
  const pitch = size + TILE_GAP
  const first = Math.max(0, Math.floor((from - GRID_PADDING - size) / pitch) + 1)
  const last = Math.min(cells - 1, Math.ceil((to - GRID_PADDING) / pitch) - 1)
  return [first, last]
}

/**
 * Row-major indices of the tiles whose box overlaps `rect`. Coordinates are relative to the scroll
 * container's top-left, scroll offset included, so tile 0 spans GRID_PADDING..GRID_PADDING + size.
 */
export function tileIndicesInRect(rect: Rect, { columns, size, count }: GridLayout): number[] {
  if (columns < 1 || size <= 0 || count <= 0) return []
  const [firstCol, lastCol] = cellRange(rect.left, rect.right, size, columns)
  const [firstRow, lastRow] = cellRange(rect.top, rect.bottom, size, Math.ceil(count / columns))
  const indices: number[] = []
  for (let row = firstRow; row <= lastRow; row++) {
    for (let col = firstCol; col <= lastCol; col++) {
      const index = row * columns + col
      if (index < count) indices.push(index)
    }
  }
  return indices
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/grid/marquee.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/grid/marquee.ts web/src/grid/marquee.test.ts
git commit -m "feat: add marquee hit-test geometry for photo grids"
```

---

### Task 2: `useSelection.replace`

**Files:**
- Modify: `web/src/grid/useSelection.ts` (type `Selection`, hook body, return)
- Test: `web/src/grid/useSelection.test.tsx`

**Interfaces:**
- Consumes: existing `useSelection(orderedIds, resetKey)`.
- Produces: `Selection.replace: (ids: Iterable<number>) => void` — sets the selected set to exactly `ids` (empty iterable clears; `isSelecting` follows). Anchor is kept.

- [ ] **Step 1: Write the failing test**

Append inside the `describe('useSelection', ...)` block in `web/src/grid/useSelection.test.tsx`, before its closing `})`:

```tsx
  it('replace sets exactly the given photos, and an empty set ends selecting', () => {
    const { result } = renderHook(() => useSelection(ids, 'k'))
    act(() => result.current.toggle(1, { shift: false }))
    act(() => result.current.replace([2, 3]))
    expect(sorted(result.current.selected)).toEqual([2, 3])
    expect(result.current.isSelecting).toBe(true)
    act(() => result.current.replace([]))
    expect(result.current.isSelecting).toBe(false)
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/grid/useSelection.test.tsx`
Expected: FAIL (`replace is not a function`).

- [ ] **Step 3: Write minimal implementation**

In `web/src/grid/useSelection.ts`:

Add to the `Selection` type, after `selectAll: () => void`:

```ts
  /** Makes the selection exactly these photos (used by the drag-a-box gesture). */
  replace: (ids: Iterable<number>) => void
```

Add after the `selectAll` const:

```ts
  const replace = (ids: Iterable<number>) =>
    setState({ key: resetKey, ids: new Set(ids), anchor: current.anchor })
```

Change the return to:

```ts
  return {
    selected: current.ids,
    count: current.ids.size,
    isSelecting,
    toggle,
    selectAll,
    replace,
    clear,
  }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/grid/useSelection.test.tsx`
Expected: PASS (all tests in the file).

- [ ] **Step 5: Commit**

```bash
git add web/src/grid/useSelection.ts web/src/grid/useSelection.test.tsx
git commit -m "feat: let a selection be replaced wholesale"
```

---

### Task 3: `useMarquee` hook and overlay

**Files:**
- Create: `web/src/grid/useMarquee.tsx`
- Modify: `web/src/test/setup.ts` (append jsdom stubs)
- Test: `web/src/grid/useMarquee.test.tsx`

**Interfaces:**
- Consumes: `Rect`, `GridLayout`, `normalizeRect`, `tileIndicesInRect` (Task 1); `Selection['selected' | 'replace']` (Task 2).
- Produces:
  - `useMarquee(element: HTMLDivElement | null, layout: GridLayout, orderedIds: readonly number[], selection: Pick<Selection, 'selected' | 'replace'>): { bind: { onPointerDown, onPointerMove, onPointerUp, onPointerCancel }; box: Rect | null }` — spread `bind` on the scroll container `<div>`; `box` is non-null only while a marquee is active.
  - `MarqueeBox({ box }: { box: Rect | null })` — overlay; render it inside the scroll container (which must be `position: relative`).

- [ ] **Step 1: Add jsdom stubs** (jsdom has no pointer capture)

Append to `web/src/test/setup.ts`:

```ts
// jsdom has no pointer capture; the marquee captures the pointer so a drag can leave the grid.
Element.prototype.setPointerCapture ??= function setPointerCapture() {}
Element.prototype.releasePointerCapture ??= function releasePointerCapture() {}
```

- [ ] **Step 2: Write the failing test**

Create `web/src/grid/useMarquee.test.tsx`. The harness renders a real scroll `div` (setup.ts gives it a 1200x800 box at 0,0) with 6 logical tiles: `columns: 3, size: 100`, so tile ids 10..15 sit at x 8-108 / 112-212 / 216-316 and y 8-108 / 112-212.

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { MarqueeBox, useMarquee } from './useMarquee'
import { useSelection } from './useSelection'

const ids = [10, 11, 12, 13, 14, 15]
const layout = { columns: 3, size: 100, count: ids.length }

function Harness() {
  const [element, setElement] = useState<HTMLDivElement | null>(null)
  const selection = useSelection(ids, 'k')
  const { bind, box } = useMarquee(element, layout, ids, selection)
  return (
    <div ref={setElement} data-testid="grid" className="relative" {...bind}>
      <button type="button" data-testid="tile-button">
        tile
      </button>
      <output data-testid="selected">{[...selection.selected].sort().join(',')}</output>
      <MarqueeBox box={box} />
    </div>
  )
}

const selected = () => screen.getByTestId('selected').textContent
const grid = () => screen.getByTestId('grid')
const press = (x: number, y: number, init: object = {}) =>
  fireEvent.pointerDown(grid(), { button: 0, pointerId: 1, clientX: x, clientY: y, ...init })
const move = (x: number, y: number) =>
  fireEvent.pointerMove(grid(), { pointerId: 1, clientX: x, clientY: y })
const release = (x: number, y: number) =>
  fireEvent.pointerUp(grid(), { pointerId: 1, clientX: x, clientY: y })

describe('useMarquee', () => {
  it('selects the tiles the dragged box touches and shows the box', () => {
    render(<Harness />)
    press(2, 2)
    move(130, 50)
    expect(selected()).toBe('10,11')
    expect(screen.getByTestId('marquee-box')).toBeInTheDocument()
    release(130, 50)
    expect(screen.queryByTestId('marquee-box')).not.toBeInTheDocument()
    expect(selected()).toBe('10,11')
  })

  it('deselects tiles again when the box shrinks back', () => {
    render(<Harness />)
    press(2, 2)
    move(130, 50)
    move(50, 50)
    expect(selected()).toBe('10')
  })

  it('a replacing marquee discards the old selection; Ctrl keeps it', () => {
    render(<Harness />)
    press(2, 2)
    move(50, 50)
    release(50, 50)
    expect(selected()).toBe('10')
    press(2, 120)
    move(50, 150)
    release(50, 150)
    expect(selected()).toBe('13')
    press(230, 2, { ctrlKey: true })
    move(250, 50)
    release(250, 50)
    expect(selected()).toBe('12,13')
  })

  it('ignores movement under the 4px threshold', () => {
    render(<Harness />)
    press(2, 2)
    move(4, 4)
    release(4, 4)
    expect(selected()).toBe('')
    expect(screen.queryByTestId('marquee-box')).not.toBeInTheDocument()
  })

  it('does not start on an interactive element such as a tile', () => {
    render(<Harness />)
    fireEvent.pointerDown(screen.getByTestId('tile-button'), {
      button: 0,
      pointerId: 1,
      clientX: 2,
      clientY: 2,
    })
    move(250, 250)
    expect(selected()).toBe('')
  })

  it('does not start on the scrollbar gutter or with a non-primary button', () => {
    render(<Harness />)
    press(1200, 50) // jsdom's clientWidth is 1200, and the scrollbar gutter begins at clientWidth
    move(10, 50)
    expect(selected()).toBe('')
    press(2, 2, { button: 2 })
    move(250, 250)
    expect(selected()).toBe('')
  })
})
```

Note: jsdom can't render a real scrollbar, so the gutter test simply presses at `x = clientWidth` (1200), which the implementation's `clientX - rect.left >= element.clientWidth` check treats as the gutter.

- [ ] **Step 3: Run test to verify it fails**

Run: `npx vitest run src/grid/useMarquee.test.tsx`
Expected: FAIL (cannot resolve `./useMarquee`).

- [ ] **Step 4: Write the implementation**

Create `web/src/grid/useMarquee.tsx`:

```tsx
import { alpha, Box } from '@mui/material'
import { useEffect, useRef, useState, type PointerEvent } from 'react'
import { normalizeRect, tileIndicesInRect, type GridLayout, type Rect } from './marquee'
import type { Selection } from './useSelection'

/** Pointer travel before a press on empty space becomes a marquee, so a stray click does nothing. */
const DRAG_THRESHOLD = 4
/** Distance from the grid's top/bottom edge at which dragging starts to scroll it. */
const SCROLL_EDGE = 48
const MAX_SCROLL_PER_FRAME = 24
/** Presses on these start their own interaction (open, drag-reorder, toggle, favorite). */
const INTERACTIVE = '[role="button"], button, input, a'

type Drag = {
  originX: number
  originY: number
  startX: number
  startY: number
  clientX: number
  clientY: number
  base: ReadonlySet<number>
  active: boolean
}

/**
 * Drag a rectangle over empty grid space to select every tile it touches. Spread `bind` on the
 * scroll container (which must be `position: relative`) and render <MarqueeBox box={box} /> in it.
 * Hit-testing uses the grid's layout numbers, so tiles a virtualizer hasn't mounted still count.
 */
export function useMarquee(
  element: HTMLDivElement | null,
  layout: GridLayout,
  orderedIds: readonly number[],
  selection: Pick<Selection, 'selected' | 'replace'>,
) {
  const drag = useRef<Drag | null>(null)
  const frame = useRef(0)
  const [box, setBox] = useState<Rect | null>(null)
  const latest = useRef({ layout, orderedIds, selection })
  useEffect(() => {
    latest.current = { layout, orderedIds, selection }
  })
  useEffect(() => () => cancelAnimationFrame(frame.current), [])

  const toContent = (clientX: number, clientY: number) => {
    const bounds = element!.getBoundingClientRect()
    return {
      x: clientX - bounds.left + element!.scrollLeft,
      y: clientY - bounds.top + element!.scrollTop,
    }
  }

  const update = (current: Drag) => {
    const { x, y } = toContent(current.clientX, current.clientY)
    const rect = normalizeRect(current.startX, current.startY, x, y)
    const { layout: grid, orderedIds: order, selection: target } = latest.current
    const hit = tileIndicesInRect(rect, grid).map((index) => order[index])
    target.replace([...current.base, ...hit])
    setBox(rect)
  }

  const autoScroll = () => {
    const current = drag.current
    if (current === null || element === null) return
    const bounds = element.getBoundingClientRect()
    const above = bounds.top + SCROLL_EDGE - current.clientY
    const below = current.clientY - (bounds.bottom - SCROLL_EDGE)
    const step = (distance: number) =>
      Math.min(MAX_SCROLL_PER_FRAME, Math.ceil((distance / SCROLL_EDGE) * MAX_SCROLL_PER_FRAME))
    if (current.active && (above > 0 || below > 0)) {
      element.scrollTop += above > 0 ? -step(above) : step(below)
      update(current)
    }
    frame.current = requestAnimationFrame(autoScroll)
  }

  const finish = () => {
    drag.current = null
    cancelAnimationFrame(frame.current)
    setBox(null)
  }

  const bind = {
    onPointerDown(event: PointerEvent<HTMLDivElement>) {
      if (element === null || drag.current !== null || event.button !== 0) return
      if ((event.target as HTMLElement).closest(INTERACTIVE) !== null) return
      const bounds = element.getBoundingClientRect()
      if (event.clientX - bounds.left >= element.clientWidth) return // the scrollbar
      const { x, y } = toContent(event.clientX, event.clientY)
      const additive = event.ctrlKey || event.metaKey || event.shiftKey
      drag.current = {
        originX: event.clientX,
        originY: event.clientY,
        startX: x,
        startY: y,
        clientX: event.clientX,
        clientY: event.clientY,
        base: additive ? new Set(latest.current.selection.selected) : new Set(),
        active: false,
      }
      element.setPointerCapture(event.pointerId)
    },
    onPointerMove(event: PointerEvent<HTMLDivElement>) {
      const current = drag.current
      if (current === null) return
      current.clientX = event.clientX
      current.clientY = event.clientY
      if (!current.active) {
        const travelled = Math.hypot(event.clientX - current.originX, event.clientY - current.originY)
        if (travelled < DRAG_THRESHOLD) return
        current.active = true
        frame.current = requestAnimationFrame(autoScroll)
      }
      update(current)
    },
    onPointerUp: finish,
    onPointerCancel: finish,
  }

  return { bind, box }
}

export function MarqueeBox({ box }: { box: Rect | null }) {
  if (box === null) return null
  return (
    <Box
      aria-hidden
      data-testid="marquee-box"
      sx={{
        position: 'absolute',
        left: box.left,
        top: box.top,
        width: box.right - box.left,
        height: box.bottom - box.top,
        zIndex: 10,
        pointerEvents: 'none',
        border: '1px solid',
        borderColor: 'primary.main',
        borderRadius: '2px',
        bgcolor: (theme) => alpha(theme.palette.primary.main, 0.2),
      }}
    />
  )
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npx vitest run src/grid/useMarquee.test.tsx`
Expected: PASS (6 tests).

If `clientX`/`clientY` are `undefined`/0 inside handlers (an older jsdom without `PointerEvent`), every coordinate test fails the same way: replace `fireEvent.pointerX` in the test helpers with `fireEvent(el, Object.assign(new MouseEvent('pointerdown', { bubbles: true, clientX, clientY, ctrlKey, button }), { pointerId: 1 }))`. jsdom 30 is expected to support `PointerEvent`, so try the plain form first.

- [ ] **Step 6: Lint the new files**

Run: `npx eslint src/grid/useMarquee.tsx src/grid/marquee.ts src/grid/useMarquee.test.tsx`
Expected: no errors. If `react-hooks` complains about reading refs or the `element!` assertions, keep behavior and adjust the smallest thing (e.g. guard `element === null` at the top of `toContent`'s callers) rather than disabling rules.

- [ ] **Step 7: Commit**

```bash
git add web/src/grid/useMarquee.tsx web/src/grid/useMarquee.test.tsx web/src/test/setup.ts
git commit -m "feat: add drag-a-box selection hook with edge auto-scroll"
```

---

### Task 4: Wire into AlbumGrid (the reported case)

**Files:**
- Modify: `web/src/albums/AlbumGrid.tsx`
- Test: `web/src/albums/AlbumMarquee.test.tsx`

**Interfaces:**
- Consumes: `useMarquee`, `MarqueeBox` (Task 3); `Selection.replace` (Task 2); `columns`, `size`, `items` already computed in `AlbumGrid`.
- Produces: `AlbumGrid` supports the marquee; no prop changes.

- [ ] **Step 1: Write the failing test**

Create `web/src/albums/AlbumMarquee.test.tsx` (album 5 has three photos after the assignment, as in `AlbumReorder.test.tsx`):

```tsx
import { fireEvent, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

beforeEach(() => {
  albumStore.get(5)!.imageIds = [20, 21, 22]
})

const dragBox = (from: [number, number], to: [number, number], init: object = {}) => {
  const grid = screen.getByTestId('album-grid')
  fireEvent.pointerDown(grid, { button: 0, pointerId: 1, clientX: from[0], clientY: from[1], ...init })
  fireEvent.pointerMove(grid, { pointerId: 1, clientX: to[0], clientY: to[1] })
  fireEvent.pointerUp(grid, { pointerId: 1, clientX: to[0], clientY: to[1] })
}

describe('drag-a-box selection in an album', () => {
  it('selects every photo the box covers and shows the selection bar', async () => {
    renderApp('/albums/5')
    await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    dragBox([2, 2], [1100, 700])
    expect(await screen.findByRole('toolbar', { name: 'Selection' })).toHaveTextContent(
      '3 selected',
    )
    expect(screen.getByTestId('tile-20')).toHaveAttribute('data-selected', 'true')
  })

  it('a press that starts on a photo does not start a box, so reorder and open still work', async () => {
    renderApp('/albums/5')
    const tile = await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    fireEvent.pointerDown(tile, { button: 0, pointerId: 1, clientX: 50, clientY: 50 })
    fireEvent.pointerMove(screen.getByTestId('album-grid'), {
      pointerId: 1,
      clientX: 1100,
      clientY: 700,
    })
    expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/albums/AlbumMarquee.test.tsx`
Expected: FAIL on the first test (no selection toolbar appears).

- [ ] **Step 3: Wire the hook**

In `web/src/albums/AlbumGrid.tsx`:

1. Imports: add `useState` is already imported; add
   ```ts
   import { MarqueeBox, useMarquee } from '../grid/useMarquee'
   ```
2. Track the scroll element. Add below `const [width, setWidth] = useState(0)`:
   ```ts
   const [scrollElement, setScrollElement] = useState<HTMLDivElement | null>(null)
   ```
3. In `attach`, call the setter first:
   ```ts
   const attach = useCallback((element: HTMLDivElement | null) => {
     setScrollElement(element)
     if (element === null) return
     ...unchanged...
   ```
4. After `const ids = items.map((item) => item.id)` add:
   ```ts
   const { bind, box } = useMarquee(
     scrollElement,
     { columns, size, count: items.length },
     ids,
     selection,
   )
   ```
5. On the scroll container `<div ref={attach} ...>`: change `className` to `"relative min-h-0 flex-1 select-none overflow-y-auto bg-zinc-50/60 dark:bg-transparent"`, add `{...bind}` after `data-testid="album-grid"`, and render `<MarqueeBox box={box} />` as the last child inside that div (after the `{width > 0 && (...)}` block).

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/albums`
Expected: PASS, including the existing `AlbumReorder`, `AlbumView` and `AlbumSort` tests (drag-reorder must be unaffected).

- [ ] **Step 5: Commit**

```bash
git add web/src/albums/AlbumGrid.tsx web/src/albums/AlbumMarquee.test.tsx
git commit -m "feat: drag-a-box selection in albums"
```

---

### Task 5: Wire into the virtualized PhotoGrid

**Files:**
- Modify: `web/src/grid/PhotoGrid.tsx`, `web/src/views/ImageBrowser.tsx`
- Test: `web/src/views/Marquee.test.tsx`

**Interfaces:**
- Consumes: `useMarquee`, `MarqueeBox`; `Selection` type from `./useSelection`.
- Produces: `PhotoGrid` gains a required prop `selection: Selection`; `ImageBrowser` passes its existing `selection`.

- [ ] **Step 1: Write the failing test**

Create `web/src/views/Marquee.test.tsx` (folder 3 has three photos, per `Selection.test.tsx`):

```tsx
import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'

describe('drag-a-box selection in a folder', () => {
  it('selects the photos under the box', async () => {
    renderApp('/folders/3')
    await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    const grid = screen.getByTestId('photo-grid')
    fireEvent.pointerDown(grid, { button: 0, pointerId: 1, clientX: 2, clientY: 2 })
    fireEvent.pointerMove(grid, { pointerId: 1, clientX: 1100, clientY: 400 })
    fireEvent.pointerUp(grid, { pointerId: 1, clientX: 1100, clientY: 400 })
    expect(await screen.findByRole('toolbar', { name: 'Selection' })).toHaveTextContent(
      '3 selected',
    )
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/views/Marquee.test.tsx`
Expected: FAIL (no selection toolbar).

- [ ] **Step 3: Wire the hook**

In `web/src/grid/PhotoGrid.tsx`:

1. Imports: add
   ```ts
   import { MarqueeBox, useMarquee } from './useMarquee'
   import type { Selection } from './useSelection'
   ```
2. `Props`: add `selection: Selection`; destructure `selection` in the function parameters.
3. After `const rowCount = ...` add:
   ```ts
   const { bind, box } = useMarquee(
     scrollElement,
     { columns, size, count: items.length },
     items.map((item) => item.id),
     selection,
   )
   ```
4. On the scroll container: change `className` to `"relative min-h-0 flex-1 select-none overflow-y-auto bg-zinc-50/60 dark:bg-transparent"`, add `{...bind}` after `data-testid="photo-grid"`, and render `<MarqueeBox box={box} />` immediately before `{isFetchingNextPage && <LinearProgress />}`.

In `web/src/views/ImageBrowser.tsx`, add `selection={selection}` to the `<PhotoGrid ... />` props (after `items={items}`).

Note: only loaded items can be selected; a marquee never triggers paging beyond what auto-scroll already reveals (the existing prefetch effect loads the next page as rows near the end scroll into view).

- [ ] **Step 4: Run the full frontend suite, lint and type-check**

Run: `npm run test`
Expected: all tests pass.

Run: `npx tsc -b` then `npx eslint src`
Expected: no errors. (Report at most 20 lines if anything fails.)

- [ ] **Step 5: Manual check in the real app** (jsdom cannot verify real layout or scrolling)

Start the app (see `run` skill / the project's usual dev command), open an album with 200 photos, and confirm:
1. Dragging from the gap between tiles or the grid margin draws a box and selects touched tiles; the selection bar shows the count; Remove/Add to album act on exactly those.
2. Dragging a tile still reorders (album) and clicking still opens the viewer.
3. Dragging past the bottom edge scrolls and keeps extending the box.
4. Ctrl + drag adds to an existing selection; shrinking the box deselects.
5. Same gesture works in a folder view (virtualized grid), including rows scrolled out of view.
6. Dragging on the vertical scrollbar still just scrolls.

- [ ] **Step 6: Commit**

```bash
git add web/src/grid/PhotoGrid.tsx web/src/views/ImageBrowser.tsx web/src/views/Marquee.test.tsx
git commit -m "feat: drag-a-box selection in folder, search and favorites grids"
```

---

## Self-Review Notes

- **Coverage:** marquee start rules (Task 3), additive/replace/shrink (Task 3), auto-scroll (Task 3 code, verified manually in Task 5 because jsdom cannot scroll), both grids (Tasks 4, 5), selection bar reused (no change).
- **Type consistency:** `Rect`/`GridLayout` defined in Task 1 and used unchanged in Tasks 3-5; `replace(ids: Iterable<number>)` from Task 2 is what `useMarquee` calls; `useMarquee(element, layout, orderedIds, selection)` signature is identical in Tasks 4 and 5.
- **Known limitation:** auto-scroll has no automated test (rAF + real layout); covered by manual check step 3.
