import { fireEvent, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { activeJob, discoveryEvents, faceRecognitionEvents, scanEvents } from '../test/jobHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('JobStatusBanner', () => {
  it('shows nothing when no job is running', async () => {
    renderApp('/folders/1')
    await screen.findByRole('treeitem', { name: 'Holidays' })
    expect(screen.queryByText(/Scanning…|Discovering…/)).not.toBeInTheDocument()
  })

  it('restores and shows a scan already running server-side, with a link to its folder', async () => {
    server.use(
      activeJob({ kind: 'Scan', id: 1, folderId: 2, foldersProcessed: 3, filesFound: 40 }),
      scanEvents(1, [
        { Id: 1, Status: 'Enumerating', FoldersScanned: 3, FilesFound: 40, FilesEnriched: 0, ErrorMessage: null },
      ]),
    )
    renderApp('/folders/1')

    const banner = await screen.findByRole('alert')
    await waitFor(() => expect(banner).toHaveTextContent('Scanning… 3 folders, 40 files'))
    expect(screen.getByRole('link', { name: 'Holidays' })).toHaveAttribute(
      'href',
      '/folders/2',
    )
  })

  it('restores a whole-instance scan (no folder scope) and shows it without a folder link', async () => {
    server.use(
      activeJob({ kind: 'Scan', id: 1, folderId: null, foldersProcessed: 0, filesFound: 12 }),
      scanEvents(1, [
        { Id: 1, Status: 'Enriching', FoldersScanned: 0, FilesFound: 12, FilesEnriched: 4, ErrorMessage: null },
      ]),
    )
    renderApp('/folders/1')

    const banner = await screen.findByRole('alert')
    await waitFor(() => expect(banner).toHaveTextContent('Enriching… 4/12 files'))
    expect(screen.queryByRole('link', { name: 'Holidays' })).not.toBeInTheDocument()
  })

  it('restores a discovery already running server-side', async () => {
    server.use(
      activeJob({ kind: 'Discovery', id: 1, folderId: 2, foldersProcessed: 5 }),
      discoveryEvents(1, [{ Id: 1, Status: 'Enumerating', FoldersDiscovered: 5, ErrorMessage: null }]),
    )
    renderApp('/folders/1')

    const banner = await screen.findByRole('alert')
    await waitFor(() => expect(banner).toHaveTextContent('Discovering… 5 folders found'))
  })

  it('shows face recognition progress and cancels it', async () => {
    let cancelled = false
    server.use(
      activeJob({ kind: 'FaceRecognition', id: 4, folderId: null, status: 'Enriching', filesFound: 10 }),
      faceRecognitionEvents(4, [
        { Id: 4, Status: 'Enriching', ImagesFound: 10, ImagesProcessed: 3, FacesFound: 7, ErrorMessage: null },
      ]),
      http.post('/api/jobs/4/cancel', () => {
        cancelled = true
        return new HttpResponse(null, { status: 202 })
      }),
    )
    renderApp('/folders/1')

    const banner = await screen.findByRole('alert')
    await waitFor(() => expect(banner).toHaveTextContent('Recognizing faces… 3/10 images, 7 faces'))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(cancelled).toBe(true))
  })

  it('says it is grouping once every image is processed', async () => {
    server.use(
      activeJob({ kind: 'FaceRecognition', id: 4, folderId: null }),
      faceRecognitionEvents(4, [
        { Id: 4, Status: 'Enriching', ImagesFound: 2, ImagesProcessed: 2, FacesFound: 3, ErrorMessage: null },
      ]),
    )
    renderApp('/folders/1')

    const banner = await screen.findByRole('alert')
    await waitFor(() => expect(banner).toHaveTextContent('Grouping faces…'))
  })

  it('offers no cancel button for a scan', async () => {
    server.use(
      activeJob({ kind: 'Scan', id: 1, folderId: null }),
      scanEvents(1, [
        { Id: 1, Status: 'Enumerating', FoldersScanned: 0, FilesFound: 0, FilesEnriched: 0, ErrorMessage: null },
      ]),
    )
    renderApp('/folders/1')

    await screen.findByRole('alert')
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })
})
