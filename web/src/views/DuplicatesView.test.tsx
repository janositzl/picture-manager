import { act, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { ImageListItem } from '../api/types'
import { albumStore } from '../test/albumHandlers'
import { holidaysImages, madeiraImages } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('DuplicatesView', () => {
  it("lists the groups with each copy's folder, loading more as it goes", async () => {
    renderApp('/duplicates')
    await waitFor(() =>
      expect(screen.getAllByRole('heading', { name: '2 copies' })).toHaveLength(2),
    )
    const first = screen.getByTestId('tile-20')
    expect(within(first).getByText('dev/Holidays/Madeira')).toBeInTheDocument()
    expect(within(screen.getByTestId('tile-10')).getByText('dev/Holidays')).toBeInTheDocument()
  })

  it('the viewer steps only within the group', async () => {
    const { user } = renderApp('/duplicates')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    expect(await screen.findByRole('img', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    await user.keyboard('{ArrowRight}')
    expect(await screen.findByRole('img', { name: 'IMG_0001 copy.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })

  it('starring a copy updates it', async () => {
    const { user } = renderApp('/duplicates')
    await user.click(await screen.findByRole('button', { name: 'Add IMG_0001.jpg to favorites' }))
    expect(
      await screen.findByRole('button', { name: 'Remove IMG_0001.jpg from favorites' }),
    ).toBeInTheDocument()
  })

  it('says when there are none', async () => {
    server.use(
      http.get('/api/duplicates', () => HttpResponse.json({ items: [], nextCursor: null })),
    )
    renderApp('/duplicates')
    expect(await screen.findByText('No duplicates found.')).toBeInTheDocument()
  })

  it('a selection spans groups and can go into an album', async () => {
    const { user } = renderApp('/duplicates')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0003.jpg' }))
    expect(await screen.findByRole('toolbar', { name: 'Selection' })).toHaveTextContent(
      '2 selected',
    )
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 2 photos to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([20, 22])
  })

  it('is reachable from the app bar', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('link', { name: 'Duplicates' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/duplicates'))
  })
})

const sized = (item: ImageListItem, width: number, height: number): ImageListItem => ({
  ...item,
  width,
  height,
  fileSize: 1000,
})

describe('DuplicatesView similar mode', () => {
  it('switches to similar mode and shows the largest copy first with its resolution', async () => {
    server.use(
      http.get('/api/duplicates/similar', () =>
        HttpResponse.json({
          items: [
            {
              key: 'S1',
              count: 3,
              maxDistance: 4,
              images: [
                sized(madeiraImages[0]!, 4000, 3000),
                sized(holidaysImages[0]!, 2000, 1500),
                sized(madeiraImages[2]!, 800, 600),
              ],
            },
          ],
          nextCursor: null,
        }),
      ),
    )
    const { user, router } = renderApp('/duplicates')
    await user.click(await screen.findByRole('button', { name: 'Similar' }))
    expect(await screen.findByRole('heading', { name: '3 similar photos' })).toBeInTheDocument()
    expect(router.state.location.search).toContain('mode=similar')
    const first = screen.getByTestId(`tile-${madeiraImages[0]!.id}`)
    expect(within(first).getByText(/4000 × 3000/)).toBeInTheDocument()
    expect(within(first).getByText('Largest')).toBeInTheDocument()
    expect(screen.getAllByText('Largest')).toHaveLength(1)
  })

  it('similar mode says when there are none', async () => {
    server.use(
      http.get('/api/duplicates/similar', () => HttpResponse.json({ items: [], nextCursor: null })),
    )
    renderApp('/duplicates?mode=similar')
    expect(await screen.findByText('No similar photos found.')).toBeInTheDocument()
  })

  it('omits the resolution and the Largest chip when dimensions are unknown', async () => {
    server.use(
      http.get('/api/duplicates/similar', () =>
        HttpResponse.json({
          items: [
            {
              key: 'S1',
              count: 2,
              maxDistance: 4,
              images: [
                { ...madeiraImages[0]!, width: null, height: null, fileSize: 1000 },
                { ...holidaysImages[0]!, width: null, height: null, fileSize: 900 },
              ],
            },
          ],
          nextCursor: null,
        }),
      ),
    )
    renderApp('/duplicates?mode=similar')
    const first = await screen.findByTestId(`tile-${madeiraImages[0]!.id}`)
    expect(within(first).getByText(madeiraImages[0]!.folderPath)).toBeInTheDocument()
    expect(screen.queryByText(/null/)).not.toBeInTheDocument()
    expect(screen.queryByText('Largest')).not.toBeInTheDocument()
  })

  it('shows the similar subtitle and switches back to exact, dropping the mode param', async () => {
    server.use(
      http.get('/api/duplicates/similar', () => HttpResponse.json({ items: [], nextCursor: null })),
    )
    const { user, router } = renderApp('/duplicates?mode=similar')
    expect(
      await screen.findByText(/Photos that look the same, including resized or re-encoded copies/),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Exact' }))
    await waitFor(() => expect(router.state.location.search).not.toContain('mode'))
    expect(await screen.findAllByRole('heading', { name: '2 copies' })).not.toHaveLength(0)
    expect(screen.getByText(/Photos with identical content/)).toBeInTheDocument()
  })

  it('shows the error state when similar groups fail to load', async () => {
    server.use(http.get('/api/duplicates/similar', () => new HttpResponse(null, { status: 500 })))
    renderApp('/duplicates?mode=similar')
    expect(await screen.findByText("Couldn't load duplicates.")).toBeInTheDocument()
  })

  it('only requests the groups of the active mode', async () => {
    const requested: string[] = []
    server.use(
      http.get('/api/duplicates/similar', ({ request }) => {
        requested.push(new URL(request.url).pathname)
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
      http.get('/api/duplicates', ({ request }) => {
        requested.push(new URL(request.url).pathname)
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    const exact = renderApp('/duplicates')
    expect(await screen.findByText('No duplicates found.')).toBeInTheDocument()
    expect(requested).toEqual(['/api/duplicates'])
    exact.unmount()

    requested.length = 0
    renderApp('/duplicates?mode=similar')
    expect(await screen.findByText('No similar photos found.')).toBeInTheDocument()
    expect(requested).toEqual(['/api/duplicates/similar'])
  })

  it('clears the selection when the mode changes', async () => {
    server.use(
      http.get('/api/duplicates/similar', () =>
        HttpResponse.json({
          items: [
            {
              key: 'S1',
              count: 2,
              maxDistance: 4,
              images: [sized(madeiraImages[0]!, 4000, 3000), sized(holidaysImages[0]!, 2000, 1500)],
            },
          ],
          nextCursor: null,
        }),
      ),
    )
    const { user, router } = renderApp('/duplicates?mode=similar')
    await user.click(
      await screen.findByRole('checkbox', { name: `Select ${madeiraImages[0]!.fileName}${madeiraImages[0]!.extension}` }),
    )
    expect(await screen.findByRole('toolbar', { name: 'Selection' })).toBeInTheDocument()
    await act(() => router.navigate('/duplicates'))
    await waitFor(() =>
      expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument(),
    )
  })

  it('shows a group only once when a later page repeats it', async () => {
    const group = {
      key: 'S1',
      count: 2,
      maxDistance: 4,
      images: [sized(madeiraImages[0]!, 4000, 3000), sized(holidaysImages[0]!, 2000, 1500)],
    }
    server.use(
      http.get('/api/duplicates/similar', ({ request }) =>
        new URL(request.url).searchParams.get('cursor') === null
          ? HttpResponse.json({ items: [group], nextCursor: 'p2' })
          : HttpResponse.json({ items: [group], nextCursor: null }),
      ),
    )
    renderApp('/duplicates?mode=similar')
    await waitFor(() => expect(screen.getAllByRole('heading', { name: '2 similar photos' })).toHaveLength(1))
    await new Promise((resolve) => setTimeout(resolve, 100))
    expect(screen.getAllByRole('heading', { name: '2 similar photos' })).toHaveLength(1)
    expect(screen.getAllByTestId(`tile-${madeiraImages[0]!.id}`)).toHaveLength(1)
  })
})
