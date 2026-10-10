import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import type { AddTarget } from '../api/albums'
import { albumStore } from '../test/albumHandlers'
import { renderRoutes } from '../test/render'
import { server } from '../test/server'
import { AlbumPicker } from './AlbumPicker'

function Harness({ target }: { target: AddTarget }) {
  const [open, setOpen] = useState(true)
  return open ? <AlbumPicker target={target} onClose={() => setOpen(false)} /> : <p>closed</p>
}

function renderPicker(target: AddTarget) {
  return renderRoutes(
    [
      { path: '/', element: <Harness target={target} /> },
      { path: '/albums/:albumId', element: <p>Album page</p> },
    ],
    '/',
  )
}

const albumButtons = async () =>
  within(await screen.findByRole('list', { name: 'Albums' })).getAllByRole('button')

describe('AlbumPicker', () => {
  it('lists albums recently updated first, and the filter narrows them', async () => {
    const { user } = renderPicker({ imageIds: [22] })
    expect((await albumButtons()).map((b) => b.textContent)).toEqual([
      'Best of 20252 photos',
      'Empty0 photos',
    ])
    await user.type(screen.getByRole('textbox', { name: 'Filter albums' }), 'emp')
    expect((await albumButtons()).map((b) => b.textContent)).toEqual(['Empty0 photos'])
  })

  it('adds to an album, reports what was already there, and remembers it', async () => {
    const { user, router } = renderPicker({ imageIds: [20, 22] })
    await user.click((await albumButtons())[0]!)
    expect(
      await screen.findByText('Added 1 photo to Best of 2025 (1 was already there).'),
    ).toBeInTheDocument()
    expect(screen.getByText('closed')).toBeInTheDocument()
    expect(albumStore.get(5)!.imageIds).toEqual([21, 20, 22])
    expect(localStorage.getItem('pm.albums.lastUsed')).toBe('5')
    await user.click(screen.getByRole('button', { name: 'Open album' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums/5'))
  })

  it('creates an album and adds to it in one step', async () => {
    const { user } = renderPicker({ imageIds: [20, 22] })
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'New album name' }), 'Trip')
    await user.click(screen.getByRole('button', { name: 'Create and add' }))
    expect(await screen.findByText('Added 2 photos to Trip.')).toBeInTheDocument()
    expect(albumStore.byName('Trip')!.imageIds).toEqual([20, 22])
  })

  it('says when a new album name is taken', async () => {
    const { user } = renderPicker({ imageIds: [20] })
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'New album name' }), 'best of 2025')
    await user.click(screen.getByRole('button', { name: 'Create and add' }))
    expect(await screen.findByText('An album with this name already exists.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('says when the album no longer exists, and stays open', async () => {
    server.use(
      http.post('/api/albums/:id/images', () =>
        HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 }),
      ),
    )
    const { user } = renderPicker({ imageIds: [20] })
    await user.click((await albumButtons())[0]!)
    expect(await screen.findByText('That album no longer exists.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('adds a whole folder', async () => {
    const { user } = renderPicker({ folderId: 3 })
    await user.click((await albumButtons())[1]!)
    expect(await screen.findByText('Added 3 photos to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([20, 21, 22])
  })

  it('offers only albums you can add to, naming the owner of shared ones', async () => {
    albumStore.addShared({ id: 7, name: 'Bob view only', permission: 'Viewer' })
    albumStore.addShared({ id: 8, name: 'Bob editable', permission: 'Editor' })
    renderPicker({ imageIds: [20] })
    const list = await screen.findByRole('list', { name: 'Albums' })
    expect(within(list).getByRole('button', { name: /Bob editable/ })).toBeInTheDocument()
    expect(within(list).getByText('Shared by Bob B · 0 photos')).toBeInTheDocument()
    expect(within(list).queryByRole('button', { name: /Bob view only/ })).not.toBeInTheDocument()
  })
})
