import { screen, waitFor, within } from '@testing-library/react'
import { delay, http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { folderDetails, madeiraImages } from '../test/fixtures'
import { pagedImages } from '../test/handlers'
import { faceRecognitionEvents } from '../test/jobHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('FolderView', () => {
  it('shows the folder name, its clickable path, the photo count and its photos', async () => {
    renderApp('/folders/3')
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()
    const path = screen.getByRole('navigation', { name: 'Folder path' })
    expect(within(path).getByRole('link', { name: 'Holidays' })).toHaveAttribute(
      'href',
      '/folders/2',
    )
    expect(screen.getByText('3 photos')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })

  it('warns about a missing folder and hides its count', async () => {
    renderApp('/folders/4')
    expect(
      await screen.findByText(
        "This folder is missing on disk. Its photos are hidden until it's back. Rescan or remove it (Admin).",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText('1 photo')).not.toBeInTheDocument()
  })

  it('explains a folder with no direct photos', async () => {
    renderApp('/folders/1')
    expect(
      await screen.findByText('No photos directly in this folder. Pick a subfolder in the tree.'),
    ).toBeInTheDocument()
  })

  it('shows "Folder not found" for an unknown folder', async () => {
    renderApp('/folders/99')
    expect(await screen.findByText('Folder not found.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to the first folder' })).toHaveAttribute(
      'href',
      '/',
    )
  })

  it('changing the sort starts a fresh listing (no stale cursor) in that order', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        const query = new URL(request.url).searchParams
        requests.push(query)
        return query.get('cursor') === null
          ? HttpResponse.json({ items: madeiraImages, nextCursor: 'p1' })
          : HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    const { user, router } = renderApp('/folders/3')
    await waitFor(() => expect(requests.some((q) => q.get('cursor') === 'p1')).toBe(true))

    await user.click(screen.getByRole('combobox', { name: /Sort/ }))
    await user.click(await screen.findByRole('option', { name: 'Date' }))

    await waitFor(() => expect(requests.some((q) => q.get('sort') === 'date')).toBe(true))
    const firstDateRequest = requests.find((q) => q.get('sort') === 'date')!
    expect(firstDateRequest.get('order')).toBe('desc')
    expect(firstDateRequest.get('cursor')).toBeNull()
    expect(router.state.location.search).toBe('?sort=date')
  })

  it('filters photos by face with the icon toggle, keeping the choice in the URL', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        const query = new URL(request.url).searchParams
        requests.push(query)
        return HttpResponse.json({ items: query.get('faces') === 'without' ? [] : madeiraImages, nextCursor: null })
      }),
    )
    const { user, router } = renderApp('/folders/3')
    await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    expect(requests[0]?.get('faces')).toBeNull()
    expect(screen.getByRole('button', { name: 'All photos' })).toHaveAttribute('aria-pressed', 'true')

    await user.click(screen.getByRole('button', { name: 'Photos with a face' }))
    await waitFor(() => expect(requests.some((q) => q.get('faces') === 'with')).toBe(true))
    expect(router.state.location.search).toBe('?faces=with')

    await user.click(screen.getByRole('button', { name: 'Photos without a face' }))
    expect(await screen.findByText('No photos without a face here.')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?faces=without')

    await user.click(screen.getByRole('button', { name: 'All photos' }))
    await waitFor(() => expect(router.state.location.search).toBe(''))
  })

  it('re-analyses the folder own photos after confirming, from the header button', async () => {
    let body: unknown = null
    server.use(
      http.post('/api/face-recognitions', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ faceRecognitionJobId: 9 })
      }),
      faceRecognitionEvents(9, [
        { Id: 9, Status: 'Completed', ImagesFound: 0, ImagesProcessed: 0, FacesFound: 0, ErrorMessage: null },
      ]),
    )
    const { user } = renderApp('/folders/3')

    await user.click(await screen.findByRole('button', { name: 'Re-analyse faces' }))
    const dialog = await screen.findByRole('dialog', { name: 'Re-analyse faces?' })
    expect(body).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Re-analyse' }))

    await waitFor(() => expect(body).toEqual({ folderId: 3, isRecursive: false, reanalyze: true, preset: 'detailed' }))
  })

  it('re-analyses with the fast preset when chosen in the dialog', async () => {
    let body: unknown = null
    server.use(
      http.post('/api/face-recognitions', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ faceRecognitionJobId: 9 })
      }),
      faceRecognitionEvents(9, [
        { Id: 9, Status: 'Completed', ImagesFound: 0, ImagesProcessed: 0, FacesFound: 0, ErrorMessage: null },
      ]),
    )
    const { user } = renderApp('/folders/3')

    await user.click(await screen.findByRole('button', { name: 'Re-analyse faces' }))
    const dialog = await screen.findByRole('dialog', { name: 'Re-analyse faces?' })
    await user.click(within(dialog).getByRole('button', { name: 'Fast' }))
    await user.click(within(dialog).getByRole('button', { name: 'Re-analyse' }))

    await waitFor(() => expect(body).toEqual({ folderId: 3, isRecursive: false, reanalyze: true }))
  })

  it('toggles the direction', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'Ascending, switch to descending' }))
    expect(router.state.location.search).toBe('?order=desc')
  })

  it('puts the photo in the URL when a tile is clicked', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    expect(router.state.location.search).toBe('?image=20')
  })

  it('stars a photo from its tile without opening it', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'Add IMG_0001.jpg to favorites' }))
    expect(
      await screen.findByRole('button', { name: 'Remove IMG_0001.jpg from favorites' }),
    ).toBeInTheDocument()
    expect(router.state.location.search).toBe('')
  })

  it('loads the next page as the end of the grid comes into view', async () => {
    server.use(pagedImages([madeiraImages.slice(0, 2), madeiraImages.slice(2)]))
    renderApp('/folders/3')
    expect(await screen.findByRole('button', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })

  it('keeps the previous folder visible while the next one loads, instead of flashing a blank skeleton', async () => {
    server.use(
      http.get('/api/folders/3', async () => {
        await delay(50)
        return HttpResponse.json(folderDetails[3])
      }),
    )
    const { user } = renderApp('/folders/2')
    expect(await screen.findByRole('heading', { name: 'Holidays' })).toBeInTheDocument()

    await user.click(await screen.findByRole('treeitem', { name: 'Madeira' }))

    // While folder 3 is still loading, the previous folder's content must stay put — no blank skeleton.
    expect(screen.queryByLabelText('Loading photos')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Holidays' })).toBeInTheDocument()

    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()
  })

  it('changing the thumbnail size updates the toggle and survives a reload', async () => {
    const { user, unmount } = renderApp('/folders/3')
    await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    await user.click(screen.getByRole('button', { name: 'Small thumbnails' }))
    expect(screen.getByRole('button', { name: 'Small thumbnails' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    unmount()

    renderApp('/folders/3')
    await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    expect(screen.getByRole('button', { name: 'Small thumbnails' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('offers Retry when photos fail to load', async () => {
    let fail = true
    server.use(
      http.get('/api/images', () =>
        fail
          ? HttpResponse.json({ title: 'boom' }, { status: 500 })
          : HttpResponse.json({ items: madeiraImages, nextCursor: null }),
      ),
    )
    const { user } = renderApp('/folders/3')
    expect(await screen.findByText("Couldn't load photos.")).toBeInTheDocument()
    fail = false
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
  })
})
