import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { devRoot, excludedFolder, image, madeiraImages } from '../test/fixtures'
import { activeJob, controllableEvents, discoveryEvents, faceRecognitionEvents, scanEvents } from '../test/jobHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

async function openMenu(
  user: ReturnType<typeof import('@testing-library/user-event').default.setup>,
  folderName: string,
) {
  const node = await screen.findByRole('treeitem', { name: folderName })
  await user.click(within(node).getByRole('button', { name: `Actions for ${folderName}` }))
}

describe('folder actions', () => {
  it("refreshes a folder's structure and shows live progress until it completes", async () => {
    const events = controllableEvents('/api/discoveries', 1)
    server.use(events.handler)
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Refresh structure' }))

    events.send({ Id: 1, Status: 'Enumerating', FoldersDiscovered: 5, ErrorMessage: null })
    expect(await screen.findByText('Discovering… 5 folders found')).toBeInTheDocument()

    events.send({ Id: 1, Status: 'Completed', FoldersDiscovered: 12, ErrorMessage: null })
    events.close()
    await waitFor(() => expect(screen.queryByText(/Discovering…/)).not.toBeInTheDocument())
    expect(
      within(screen.getByRole('treeitem', { name: 'Holidays' })).getByRole('button', {
        name: 'Actions for Holidays',
      }),
    ).toBeEnabled()
  })

  it('posts the folder id and isRecursive: false for "Scan folder"', async () => {
    let body: unknown
    server.use(
      http.post('/api/scans', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ scanJobId: 1 })
      }),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Scan folder' }))
    await waitFor(() => expect(body).toEqual({ folderId: 2, isRecursive: false }))
  })

  it('posts isRecursive: true for "Scan folder + subfolders"', async () => {
    let body: unknown
    server.use(
      http.post('/api/scans', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ scanJobId: 1 })
      }),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Scan folder + subfolders' }))
    await waitFor(() => expect(body).toEqual({ folderId: 2, isRecursive: true }))
  })

  it('starts face recognition for the folder and its subfolders', async () => {
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
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Recognize faces' }))

    await waitFor(() => expect(body).toEqual({ folderId: 2, isRecursive: true }))
  })

  it('shows scan progress while enumerating, then while enriching', async () => {
    const events = controllableEvents('/api/scans', 1)
    server.use(events.handler)
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Scan folder' }))

    events.send({
      Id: 1,
      Status: 'Enumerating',
      FoldersScanned: 3,
      FilesFound: 40,
      FilesEnriched: 0,
      ErrorMessage: null,
    })
    expect(await screen.findByText('Scanning… 3 folders, 40 files')).toBeInTheDocument()

    events.send({
      Id: 1,
      Status: 'Enriching',
      FoldersScanned: 3,
      FilesFound: 40,
      FilesEnriched: 10,
      ErrorMessage: null,
    })
    expect(await screen.findByText('Enriching… 10/40 files')).toBeInTheDocument()

    events.send({
      Id: 1,
      Status: 'Completed',
      FoldersScanned: 3,
      FilesFound: 40,
      FilesEnriched: 40,
      ErrorMessage: null,
    })
    events.close()
  })

  it("disables other folders' actions while a job is running", async () => {
    server.use(
      discoveryEvents(1, [
        { Id: 1, Status: 'Enumerating', FoldersDiscovered: 1, ErrorMessage: null },
      ]),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Refresh structure' }))

    const dev = screen.getByRole('treeitem', { name: 'dev' })
    expect(within(dev).getByRole('button', { name: 'Actions for dev' })).toBeDisabled()
  })

  it('shows a message when a job is already in progress', async () => {
    server.use(
      http.post('/api/discoveries', () =>
        HttpResponse.json(
          { message: 'A scan or discovery is already in progress.' },
          { status: 409 },
        ),
      ),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Refresh structure' }))
    expect(
      await screen.findByText('Another job is already in progress.'),
    ).toBeInTheDocument()
  })

  it('restores a job still running on the backend after a page load, disabling actions without any user action', async () => {
    server.use(
      activeJob({ kind: 'Scan', id: 1, folderId: 2, foldersProcessed: 3, filesFound: 40 }),
      scanEvents(1, [
        { Id: 1, Status: 'Enumerating', FoldersScanned: 3, FilesFound: 40, FilesEnriched: 0, ErrorMessage: null },
      ]),
    )
    renderApp('/folders/1')

    const holidays = await screen.findByRole('treeitem', { name: 'Holidays' })
    expect(await within(holidays).findByText('Scanning… 3 folders, 40 files')).toBeInTheDocument()
    const dev = screen.getByRole('treeitem', { name: 'dev' })
    expect(within(dev).getByRole('button', { name: 'Actions for dev' })).toBeDisabled()
  })

  it('refreshes the grid when a folder scan completes, without navigating away or clicking again', async () => {
    const events = controllableEvents('/api/scans', 1)
    let includeNewImage = false
    server.use(
      events.handler,
      http.get('/api/images', () =>
        HttpResponse.json({
          items: includeNewImage ? [...madeiraImages, image(30, 3)] : madeiraImages,
          nextCursor: null,
        }),
      ),
    )
    const { user } = renderApp('/folders/3')
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'IMG_0030.jpg' })).not.toBeInTheDocument()

    await openMenu(user, 'Madeira')
    await user.click(await screen.findByRole('menuitem', { name: 'Scan folder' }))

    includeNewImage = true
    events.send({
      Id: 1,
      Status: 'Completed',
      FoldersScanned: 1,
      FilesFound: 4,
      FilesEnriched: 4,
      ErrorMessage: null,
    })
    events.close()

    expect(await screen.findByRole('button', { name: 'IMG_0030.jpg' })).toBeInTheDocument()
  })

  it('shows the job error when a scan fails', async () => {
    server.use(
      scanEvents(1, [
        {
          Id: 1,
          Status: 'Failed',
          FoldersScanned: 1,
          FilesFound: 0,
          FilesEnriched: 0,
          ErrorMessage: 'Disk unavailable.',
        },
      ]),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Scan folder' }))
    expect(await screen.findByText('Disk unavailable.')).toBeInTheDocument()
  })
})

describe('folder exclusion', () => {
  it('excludes a folder: sends the PUT and refetches the tree', async () => {
    let excludeBody: unknown
    server.use(
      http.put('/api/folders/:id/exclusion', async ({ request }) => {
        excludeBody = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Exclude folder' }))
    await waitFor(() => expect(excludeBody).toEqual({ isExcluded: true }))
  })

  it('shows the X icon on an excluded folder and, inherited, on its child', async () => {
    server.use(http.get('/api/folders/roots', () => HttpResponse.json([devRoot, excludedFolder])))
    const { user } = renderApp('/folders/1')

    expect(await screen.findByRole('treeitem', { name: 'Excluded (excluded)' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Expand Excluded' }))
    expect(
      await screen.findByRole('treeitem', { name: 'Nested (excluded)' }),
    ).toBeInTheDocument()
  })

  it('disables scan and refresh actions, and shows the checked, disabled toggle, on an excluded folder', async () => {
    server.use(http.get('/api/folders/roots', () => HttpResponse.json([devRoot, excludedFolder])))
    const { user } = renderApp('/folders/1')
    const node = await screen.findByRole('treeitem', { name: 'Excluded (excluded)' })
    await user.click(within(node).getByRole('button', { name: 'Actions for Excluded' }))

    expect(screen.getByRole('menuitem', { name: 'Refresh structure' })).toHaveAttribute(
      'aria-disabled',
      'true',
    )
    expect(screen.getByRole('menuitem', { name: 'Scan folder' })).toHaveAttribute(
      'aria-disabled',
      'true',
    )
    expect(screen.getByRole('menuitem', { name: 'Scan folder + subfolders' })).toHaveAttribute(
      'aria-disabled',
      'true',
    )
    expect(screen.getByRole('menuitem', { name: /Exclude folder/ })).not.toHaveAttribute(
      'aria-disabled',
      'true',
    )
  })

  it("disables a child's toggle while its parent is excluded", async () => {
    server.use(http.get('/api/folders/roots', () => HttpResponse.json([devRoot, excludedFolder])))
    const { user } = renderApp('/folders/1')
    await user.click(await screen.findByRole('button', { name: 'Expand Excluded' }))
    const node = await screen.findByRole('treeitem', { name: 'Nested (excluded)' })
    await user.click(within(node).getByRole('button', { name: 'Actions for Nested' }))

    expect(
      screen.getByRole('menuitem', { name: /Exclude folder.*Excluded via parent folder/s }),
    ).toHaveAttribute('aria-disabled', 'true')
  })

  it('shows the error message when excluding fails', async () => {
    server.use(
      http.put(
        '/api/folders/:id/exclusion',
        () =>
          HttpResponse.json(
            { title: 'Conflict', status: 409, detail: 'A scan is running; change exclusion after it finishes.' },
            { status: 409 },
          ),
      ),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Exclude folder' }))
    expect(
      await screen.findByText('A scan is running; change exclusion after it finishes.'),
    ).toBeInTheDocument()
  })
})

describe('folder removal', () => {
  it("does not offer removal for a root's top folder", async () => {
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'dev')
    expect(screen.queryByRole('menuitem', { name: 'Remove from collection' })).not.toBeInTheDocument()
  })

  it('removes a folder after confirming, and refreshes the tree', async () => {
    let deletedId: number | null = null
    server.use(
      http.delete('/api/folders/:id', ({ params }) => {
        deletedId = Number(params.id)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Remove from collection' }))

    expect(await screen.findByRole('heading', { name: 'Remove folder' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Remove' }))

    await waitFor(() => expect(deletedId).toBe(2))
    expect(screen.queryByRole('heading', { name: 'Remove folder' })).not.toBeInTheDocument()
  })

  it('cancels without removing the folder', async () => {
    let called = false
    server.use(
      http.delete('/api/folders/:id', () => {
        called = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Remove from collection' }))
    await user.click(await screen.findByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('heading', { name: 'Remove folder' })).not.toBeInTheDocument()
    expect(called).toBe(false)
  })

  it('shows the error message when a scan is running', async () => {
    server.use(
      http.delete(
        '/api/folders/:id',
        () =>
          HttpResponse.json(
            { title: 'Conflict', status: 409, detail: 'A scan is running; remove the folder after it finishes.' },
            { status: 409 },
          ),
      ),
    )
    const { user } = renderApp('/folders/1')
    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Remove from collection' }))
    await user.click(await screen.findByRole('button', { name: 'Remove' }))

    expect(
      await screen.findByText('A scan is running; remove the folder after it finishes.'),
    ).toBeInTheDocument()
  })

  it('navigates to the parent folder after removing the folder currently being viewed', async () => {
    server.use(http.delete('/api/folders/:id', () => new HttpResponse(null, { status: 204 })))
    const { user } = renderApp('/folders/3')
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()

    await openMenu(user, 'Madeira')
    await user.click(await screen.findByRole('menuitem', { name: 'Remove from collection' }))
    await user.click(await screen.findByRole('button', { name: 'Remove' }))

    expect(await screen.findByRole('heading', { name: 'Holidays' })).toBeInTheDocument()
  })

  it('does not navigate away when removing a folder other than the one being viewed', async () => {
    server.use(http.delete('/api/folders/:id', () => new HttpResponse(null, { status: 204 })))
    const { user } = renderApp('/folders/3')
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()

    await openMenu(user, 'Holidays')
    await user.click(await screen.findByRole('menuitem', { name: 'Remove from collection' }))
    await user.click(await screen.findByRole('button', { name: 'Remove' }))

    await waitFor(() =>
      expect(screen.queryByRole('heading', { name: 'Remove folder' })).not.toBeInTheDocument(),
    )
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()
  })
})
