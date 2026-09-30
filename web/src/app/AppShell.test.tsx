import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'

afterEach(() => {
  localStorage.clear()
})

describe('AppShell folder panel resizing', () => {
  it('resizes the folder tree panel by dragging the resize handle, and persists the width', async () => {
    renderApp('/folders/1')
    const nav = await screen.findByRole('navigation', { name: 'Folders' })
    const handle = screen.getByRole('separator', { name: 'Resize folder panel' })

    fireEvent.pointerDown(handle, { clientX: 280, pointerId: 1 })
    fireEvent.pointerMove(window, { clientX: 340, pointerId: 1 })
    fireEvent.pointerUp(window, { clientX: 340, pointerId: 1 })

    await waitFor(() => expect(nav).toHaveStyle({ width: '340px' }))
    expect(localStorage.getItem('pm.tree.width')).toBe('340')
  })

  it('clamps the panel width to a minimum', async () => {
    renderApp('/folders/1')
    const nav = await screen.findByRole('navigation', { name: 'Folders' })
    const handle = screen.getByRole('separator', { name: 'Resize folder panel' })

    fireEvent.pointerDown(handle, { clientX: 280, pointerId: 1 })
    fireEvent.pointerMove(window, { clientX: 0, pointerId: 1 })
    fireEvent.pointerUp(window, { clientX: 0, pointerId: 1 })

    await waitFor(() => expect(nav).toHaveStyle({ width: '200px' }))
  })

  it('restores the persisted panel width on load', async () => {
    localStorage.setItem('pm.tree.width', '360')
    renderApp('/folders/1')
    const nav = await screen.findByRole('navigation', { name: 'Folders' })
    expect(nav).toHaveStyle({ width: '360px' })
  })
})
