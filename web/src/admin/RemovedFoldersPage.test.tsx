import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '../test/render'
import { server } from '../test/server'
import { RemovedFoldersPage } from './RemovedFoldersPage'

function renderPage() {
  return renderRoutes(
    [{ path: '/admin/removed-folders', element: <RemovedFoldersPage /> }],
    '/admin/removed-folders',
  )
}

describe('RemovedFoldersPage', () => {
  it('lists removed folders', async () => {
    renderPage()
    expect(await screen.findByText('Old')).toBeInTheDocument()
    expect(screen.getByText('Holidays/Old')).toBeInTheDocument()
  })

  it('says there is nothing removed', async () => {
    server.use(http.get('/api/folders/removed', () => HttpResponse.json([])))
    renderPage()
    expect(await screen.findByText('No removed folders.')).toBeInTheDocument()
  })

  it('restores a folder and removes it from the list', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Restore' }))
    await waitFor(() => expect(screen.getByText('No removed folders.')).toBeInTheDocument())
  })
})
