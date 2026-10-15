import { screen, waitFor } from '@testing-library/react'
import { act } from 'react'
import { describe, expect, it } from 'vitest'
import { delay, http, HttpResponse } from 'msw'
import { albumStore } from '../test/albumHandlers'
import { madeiraImages } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const selectionBar = () => screen.findByRole('toolbar', { name: 'Selection' })

describe('selecting photos', () => {
  it('a ticked checkbox shows the selection bar, and clicks then toggle instead of opening', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    expect(await selectionBar()).toHaveTextContent('1 selected')
    await user.click(screen.getByRole('button', { name: 'IMG_0002.jpg' }))
    expect(await selectionBar()).toHaveTextContent('2 selected')
    expect(router.state.location.search).toBe('')
  })

  it('Shift-click selects a range, Esc clears, Ctrl+A selects all', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.keyboard('{Shift>}')
    await user.click(screen.getByRole('button', { name: 'IMG_0003.jpg' }))
    await user.keyboard('{/Shift}')
    expect(await selectionBar()).toHaveTextContent('3 selected')
    await user.keyboard('{Escape}')
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0002.jpg' }))
    await user.keyboard('{Control>}a{/Control}')
    expect(await selectionBar()).toHaveTextContent('3 selected')
  })

  it('Select all picks every matching photo when more pages remain, not just the loaded ones', async () => {
    const idsQueries: URLSearchParams[] = []
    server.use(
      http.get('/api/images', async ({ request }) => {
        // The second page never arrives, so the grid keeps "more to load" without fetching in a loop.
        if (new URL(request.url).searchParams.has('cursor')) await delay('infinite')
        return HttpResponse.json({ items: madeiraImages, nextCursor: 'more' })
      }),
      http.get('/api/images/ids', ({ request }) => {
        idsQueries.push(new URL(request.url).searchParams)
        return HttpResponse.json({ ids: [20, 21, 22, 23, 24], truncated: false })
      }),
    )
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))

    await user.click(await screen.findByRole('button', { name: 'Select all' }))

    await waitFor(async () => expect(await selectionBar()).toHaveTextContent('5 selected'))
    expect(idsQueries).toHaveLength(1)
    expect(idsQueries[0]!.get('folderId')).toBe('3')
    expect(idsQueries[0]!.has('limit')).toBe(false)
  })

  it('Select all selects the loaded photos without asking the server when they are all there', async () => {
    let idsRequested = false
    server.use(
      http.get('/api/images/ids', () => {
        idsRequested = true
        return HttpResponse.json({ ids: [], truncated: false })
      }),
    )
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))

    await user.click(await screen.findByRole('button', { name: 'Select all' }))

    expect(await selectionBar()).toHaveTextContent('3 selected')
    expect(idsRequested).toBe(false)
  })

  it('Ctrl-click starts a selection', async () => {
    const { user } = renderApp('/folders/3')
    const tile = await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    await user.keyboard('{Control>}')
    await user.click(tile)
    await user.keyboard('{/Control}')
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('the selection clears when moving to another folder', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await selectionBar()
    await act(() => router.navigate('/folders/2'))
    expect(await screen.findByRole('heading', { name: 'Holidays' })).toBeInTheDocument()
    expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument()
  })

  it('adds the selection to an album and clears it', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0003.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 2 photos to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([20, 22])
    await waitFor(() =>
      expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument(),
    )
  })

  it('Esc closes the picker but keeps the selection', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await screen.findByRole('dialog', { name: 'Add to album' })
    await user.keyboard('{Escape}')
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Add to album' })).not.toBeInTheDocument(),
    )
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('Ctrl+A in the search box does not select photos', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    screen.getByRole('textbox', { name: 'Search file names' }).focus()
    await user.keyboard('{Control>}a{/Control}')
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('adds a whole folder from its header', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'Folder actions' }))
    await user.click(await screen.findByRole('menuitem', { name: 'Add folder to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 3 photos to Empty.')).toBeInTheDocument()
  })

  it('offers no folder add or re-analyse when the folder has no photos of its own', async () => {
    const { user } = renderApp('/folders/1')
    expect(await screen.findByRole('heading', { name: 'dev' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Folder actions' }))
    expect(
      await screen.findByRole('menuitemcheckbox', { name: 'Show hidden photos' }),
    ).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Add folder to album…' })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Re-analyse faces…' })).not.toBeInTheDocument()
  })
})
