import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('FolderTree', () => {
  it('redirects / to the first root', async () => {
    const { router } = renderApp('/')
    await waitFor(() => expect(router.state.location.pathname).toBe('/folders/1'))
  })

  it('expands the selected folder and its ancestors on a deep link, and marks the selection', async () => {
    renderApp('/folders/3')
    const madeira = await screen.findByRole('treeitem', { name: 'Madeira' })
    expect(madeira).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('treeitem', { name: 'Holidays' })).toHaveAttribute(
      'aria-expanded',
      'true',
    )
    expect(screen.getByRole('treeitem', { name: 'Old (missing)' })).toBeInTheDocument()
  })

  it('loads children when a node is expanded and hides them when collapsed', async () => {
    const { user } = renderApp('/folders/1')
    await user.click(await screen.findByRole('button', { name: 'Expand Holidays' }))
    expect(await screen.findByRole('treeitem', { name: 'Madeira' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Collapse Holidays' }))
    expect(screen.queryByRole('treeitem', { name: 'Madeira' })).not.toBeInTheDocument()
  })

  it('navigates when a folder is clicked', async () => {
    const { user, router } = renderApp('/folders/2')
    await user.click(await screen.findByRole('treeitem', { name: 'Madeira' }))
    expect(router.state.location.pathname).toBe('/folders/3')
  })

  it('shows the folder name in a tooltip on hover', async () => {
    const { user } = renderApp('/folders/1')
    const name = await screen.findByText('dev')
    await user.hover(name)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('dev')
  })

  it('offers Retry when the roots fail to load', async () => {
    server.use(
      http.get('/api/folders/roots', () => HttpResponse.json({ title: 'boom' }, { status: 500 })),
    )
    renderApp('/folders/1')
    expect(await screen.findByText("Couldn't load folders.")).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('shows a face-detection status per folder', async () => {
    renderApp('/folders/3')
    await screen.findByRole('treeitem', { name: 'Madeira' })
    const partial = await screen.findByRole('img', {
      name: 'Face detection: partially scanned (2 of 6 photos)',
    })
    expect(partial).toHaveAttribute('data-face-state', 'partial')
    expect(
      screen.getByRole('img', { name: 'Face detection: needs rescan (2 of 5 photos out of date)' }),
    ).toHaveAttribute('data-face-state', 'needsRescan')
    expect(
      screen.getByRole('img', {
        name: "Face detection: completed (3 photos, 1 couldn't be read)",
      }),
    ).toHaveAttribute('data-face-state', 'completed')
  })

  it('shows not scanned for a folder whose photos were never processed', async () => {
    server.use(
      http.get('/api/face-recognitions/coverage', () =>
        HttpResponse.json([{ folderId: 3, total: 3, done: 0, failed: 0, stale: 0 }]),
      ),
    )
    renderApp('/folders/3')
    expect(
      await screen.findByRole('img', { name: 'Face detection: not scanned (3 photos)' }),
    ).toHaveAttribute('data-face-state', 'notScanned')
  })

  it('renders the tree without face status when coverage fails to load', async () => {
    server.use(
      http.get('/api/face-recognitions/coverage', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    renderApp('/folders/3')
    expect(await screen.findByRole('treeitem', { name: 'Madeira' })).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /^Face detection:/ })).not.toBeInTheDocument()
    expect(screen.queryByText("Couldn't load folders.")).not.toBeInTheDocument()
  })
})
