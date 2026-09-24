import { act, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { ImageFilter } from '../api/imageFilter'
import { madeiraImages } from '../test/fixtures'
import { renderApp, renderRoutes } from '../test/render'
import { server } from '../test/server'
import { PhotoViewer } from './PhotoViewer'

const viewerImage = (name: string) => screen.findByRole('img', { name })
const infoPanel = () => screen.queryByRole('complementary', { name: 'Photo info' })

describe('PhotoViewer', () => {
  it('opens from the grid and steps with the arrow keys', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    expect(await viewerImage('IMG_0001.jpg')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Previous photo' })).not.toBeInTheDocument()

    await user.keyboard('{ArrowRight}')
    expect(await viewerImage('IMG_0002.jpg')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?image=21')

    await user.keyboard('{ArrowLeft}')
    expect(await viewerImage('IMG_0001.jpg')).toBeInTheDocument()
  })

  it('Esc closes the viewer and Back does not walk through every photo', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    await viewerImage('IMG_0001.jpg')
    await user.keyboard('{ArrowRight}')
    await user.keyboard('{ArrowRight}')
    await waitFor(() => expect(router.state.location.search).toBe('?image=22'))

    await act(() => router.navigate(-1))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.search).toBe('')

    await user.click(screen.getByRole('button', { name: 'IMG_0002.jpg' }))
    await viewerImage('IMG_0002.jpg')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.search).toBe('')
  })

  it('Esc on a deep link closes without leaving the app', async () => {
    const { user, router } = renderApp('/folders/3?image=21')
    await viewerImage('IMG_0002.jpg')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/folders/3')
    expect(router.state.location.search).toBe('')
  })

  it('F toggles the favorite, and the grid tile follows', async () => {
    const { user } = renderApp('/folders/3?image=20')
    await viewerImage('IMG_0001.jpg')
    await screen.findByRole('button', { name: 'Add to favorites' })
    await user.keyboard('f')
    expect(await screen.findByRole('button', { name: 'Remove from favorites' })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(
      await screen.findByRole('button', { name: 'Remove IMG_0001.jpg from favorites' }),
    ).toBeInTheDocument()
  })

  it('I toggles the info panel and remembers it', async () => {
    const first = renderApp('/folders/3?image=20')
    await viewerImage('IMG_0001.jpg')
    expect(infoPanel()).toBeInTheDocument()
    await first.user.keyboard('i')
    expect(infoPanel()).not.toBeInTheDocument()
    expect(localStorage.getItem('pm.viewer.infoOpen')).toBe('false')
    first.unmount()

    renderApp('/folders/3?image=20')
    await viewerImage('IMG_0001.jpg')
    expect(infoPanel()).not.toBeInTheDocument()
  })

  it('shows the photo details, with a Go to folder link that closes the viewer', async () => {
    const { user, router } = renderApp('/favorites?image=21')
    await viewerImage('IMG_0002.jpg')
    const panel = await screen.findByRole('complementary', { name: 'Photo info' })
    expect(await within(panel).findByText('IMG_0002.jpg')).toBeInTheDocument()
    expect(within(panel).getByText('2025-08-14 18:32')).toBeInTheDocument()
    expect(within(panel).getByText('1200 × 800')).toBeInTheDocument()
    expect(within(panel).getByText('2.3 MB')).toBeInTheDocument()
    expect(within(panel).getByText('Canon EOS R6')).toBeInTheDocument()
    expect(within(panel).getByText('Best of 2025')).toBeInTheDocument()
    expect(within(panel).getByRole('link', { name: /OpenStreetMap/ })).toHaveAttribute(
      'href',
      expect.stringContaining('mlat=32.6669'),
    )

    await user.click(within(panel).getByRole('link', { name: 'Go to folder' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/folders/3'))
    expect(router.state.location.search).toBe('')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('opens a deep-linked photo that is not in the list, without arrows', async () => {
    renderApp('/favorites?image=20')
    expect(await viewerImage('IMG_0001.jpg')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByTestId('tile-21')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Previous photo' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })

  it('says an unknown photo is no longer available, and Close returns to the grid', async () => {
    const { user, router } = renderApp('/folders/3?image=999')
    expect(await screen.findByText('This photo is no longer available.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(router.state.location.search).toBe(''))
  })

  it('ignores a malformed image param', async () => {
    renderApp('/folders/3?image=abc')
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('stepping past the last loaded photo fetches the next page', async () => {
    const cursors: (string | null)[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')
        cursors.push(cursor)
        return cursor === null
          ? HttpResponse.json({ items: madeiraImages.slice(0, 2), nextCursor: 'p1' })
          : HttpResponse.json({ items: madeiraImages.slice(2), nextCursor: null })
      }),
    )
    const filter: ImageFilter = { kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }
    const { user, router } = renderRoutes(
      [{ path: '/', element: <PhotoViewer filter={filter} /> }],
      '/?image=21',
    )

    await viewerImage('IMG_0002.jpg')
    await screen.findByRole('button', { name: 'Next photo' })
    expect(cursors).toEqual([null])

    await user.keyboard('{ArrowRight}')
    expect(await viewerImage('IMG_0003.jpg')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?image=22')
    expect(cursors).toEqual([null, 'p1'])
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })
})
