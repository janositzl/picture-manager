import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { imageDetail, madeiraImages } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const viewerImage = (name: string) => screen.findByRole('img', { name })

describe('adding from the viewer', () => {
  it('A opens the picker for the current photo', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('a')
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 1 photo to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([22])
    expect(screen.getByRole('img', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })

  it('the Add to album button opens the picker too', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.click(screen.getByRole('button', { name: 'Add to album' }))
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('Shift+A adds straight to the last used album', async () => {
    localStorage.setItem('pm.albums.lastUsed', '6')
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByText('Added 1 photo to Empty.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Add to album' })).not.toBeInTheDocument()
  })

  it('Shift+A with no last used album opens the picker', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('Shift+A with a deleted last album opens the picker and forgets it', async () => {
    localStorage.setItem('pm.albums.lastUsed', '999')
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
    expect(localStorage.getItem('pm.albums.lastUsed')).toBeNull()
  })

  it('Esc closes the picker, not the viewer', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('a')
    await screen.findByRole('dialog', { name: 'Add to album' })
    await user.keyboard('{Escape}')
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Add to album' })).not.toBeInTheDocument(),
    )
    expect(screen.getByRole('img', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })
})

describe('albums you can only view', () => {
  it('Shift+D does not remove a photo that is only in view-only albums', async () => {
    albumStore.addShared({ id: 7, name: 'Bob view only', permission: 'Viewer', imageIds: [22] })
    server.use(
      http.get('/api/images/22', () =>
        HttpResponse.json({
          ...imageDetail(madeiraImages.find((i) => i.id === 22)!),
          albums: [{ id: 7, name: 'Bob view only', access: 'Viewer' }],
        }),
      ),
    )
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}D{/Shift}')
    expect(await screen.findByText('This photo is only in albums you can view.')).toBeInTheDocument()
    expect(albumStore.get(7)!.imageIds).toEqual([22])
  })

  it('Shift+A opens the picker when the last used album became view-only', async () => {
    albumStore.addShared({ id: 7, name: 'Bob view only', permission: 'Viewer' })
    localStorage.setItem('pm.albums.lastUsed', '7')
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
    expect(albumStore.get(7)!.imageIds).toEqual([])
  })
})
