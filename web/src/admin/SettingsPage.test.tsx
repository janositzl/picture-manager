import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '../test/render'
import { server } from '../test/server'
import { SettingsPage } from './SettingsPage'

function renderPage() {
  return renderRoutes([{ path: '/admin/settings', element: <SettingsPage /> }], '/admin/settings')
}

describe('SettingsPage', () => {
  it('shows the current exclusions', async () => {
    renderPage()
    expect(await screen.findByText('@eaDir')).toBeInTheDocument()
    expect(screen.getByText('.tmp')).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'All extensions allowed' })).toBeChecked()
    expect(screen.getByText(/All of: .jpg, .jpeg, .png/)).toBeInTheDocument()
  })

  it('warns that the next scan will prune when a new exclusion is saved', async () => {
    const { user } = renderPage()
    const field = await screen.findByPlaceholderText('e.g. @eaDir')
    await user.type(field, 'Thumbs.db')
    await user.click(screen.getByRole('button', { name: 'Add to Excluded folder names' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))
    expect(
      await screen.findByText('Saved. The next scan will remove newly-excluded files and folders.'),
    ).toBeInTheDocument()
  })

  it('shows a field error from a failed save', async () => {
    server.use(
      http.put('/api/settings', () =>
        HttpResponse.json(
          {
            title: 'Bad Request',
            status: 400,
            errors: { excludedFolderNames: ['Folder names cannot contain / or \\.'] },
          },
          { status: 400 },
        ),
      ),
    )
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Folder names cannot contain / or \\.')).toBeInTheDocument()
  })

  it('adds a comma-separated batch of folder names in one go', async () => {
    const { user } = renderPage()
    const field = await screen.findByPlaceholderText('e.g. @eaDir')
    await user.type(field, 'Thumbs.db, .DS_Store')
    await user.click(screen.getByRole('button', { name: 'Add to Excluded folder names' }))
    expect(screen.getByText('Thumbs.db')).toBeInTheDocument()
    expect(screen.getByText('.DS_Store')).toBeInTheDocument()
  })

  it('toggles to a specific extension list', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('switch', { name: 'All extensions allowed' }))
    const section = (await screen.findByText('Included extensions')).closest('div')!
    await user.type(within(section).getByPlaceholderText('e.g. .jpg'), '.jpg')
    await user.click(within(section).getByRole('button', { name: 'Add to Included extensions' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.getByText('.jpg')).toBeInTheDocument())
  })
})
