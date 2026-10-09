import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { act } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { queryKeys } from '../api/queries'
import { meQueryKey } from '../api/auth'
import { activeJob, scanEvents } from '../test/jobHandlers'
import { signInAs } from '../test/authHandlers'
import { adminMe } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('authentication', () => {
  it('shows the login page when signed out, and the app after a successful login', async () => {
    signInAs(null)
    const { user } = renderApp('/folders/1')

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByRole('navigation', { name: 'Folders' })).toBeInTheDocument()
  })

  it('shows the server message for a wrong password', async () => {
    signInAs(null)
    const { user } = renderApp('/')

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByText('Invalid username or password.')).toBeInTheDocument()
    expect(screen.getByLabelText('Username')).toHaveValue('admin')
    expect(screen.getByLabelText('Password')).toHaveValue('')
  })

  it('forces a password change before showing the app', async () => {
    signInAs({ ...adminMe, mustChangePassword: true })
    const { user } = renderApp('/folders/1')

    const dialog = await screen.findByRole('dialog', { name: 'Choose a new password' })
    expect(within(dialog).queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Current password'), 'initial-pw')
    await user.type(within(dialog).getByLabelText('New password'), 'brand-new-pw')
    await user.type(within(dialog).getByLabelText('Repeat new password'), 'brand-new-pw')
    await user.click(within(dialog).getByRole('button', { name: 'Change password' }))

    expect(await screen.findByRole('navigation', { name: 'Folders' })).toBeInTheDocument()
  })

  it('rejects mismatched new passwords without calling the API', async () => {
    signInAs({ ...adminMe, mustChangePassword: true })
    const { user } = renderApp('/')

    const dialog = await screen.findByRole('dialog', { name: 'Choose a new password' })
    await user.type(within(dialog).getByLabelText('Current password'), 'initial-pw')
    await user.type(within(dialog).getByLabelText('New password'), 'brand-new-pw')
    await user.type(within(dialog).getByLabelText('Repeat new password'), 'different-pw')
    await user.click(within(dialog).getByRole('button', { name: 'Change password' }))

    expect(await within(dialog).findByText("The new passwords don't match.")).toBeInTheDocument()
  })

  it('a 401 from any API call shows the login page', async () => {
    server.use(
      http.get('/api/folders/roots', () =>
        HttpResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 }),
      ),
    )
    renderApp('/folders/1')

    expect(await screen.findByRole('button', { name: 'Log in' })).toBeInTheDocument()
  })

  it('login clears cached queries from the previous session', async () => {
    signInAs(null)
    const { user, queryClient } = renderApp('/')
    queryClient.setQueryData(queryKeys.albums(), [{ id: 99, name: 'Previous user album' }])

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    await screen.findByRole('navigation', { name: 'Folders' })
    expect(queryClient.getQueryData(queryKeys.albums())).toBeUndefined()
  })

  it("does not carry the previous user's running job into the next session", async () => {
    server.use(
      activeJob({ kind: 'Scan', id: 1, folderId: 2, foldersProcessed: 3, filesFound: 40 }),
      scanEvents(1, [
        {
          Id: 1,
          Status: 'Enumerating',
          FoldersScanned: 3,
          FilesFound: 40,
          FilesEnriched: 0,
          ErrorMessage: null,
        },
      ]),
    )
    const { user, queryClient } = renderApp('/folders/1')
    expect((await screen.findAllByText('Scanning… 3 folders, 40 files')).length).toBeGreaterThan(0)

    // The session ends; the next user has no running job.
    server.use(http.get('/api/jobs/active', () => new HttpResponse(null, { status: 204 })))
    signInAs(null)
    queryClient.setQueryData(meQueryKey, null)
    await user.type(await screen.findByLabelText('Username'), 'bob')
    await user.type(screen.getByLabelText('Password'), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    await screen.findByRole('navigation', { name: 'Folders' })
    expect(screen.queryByText(/^Scanning…/)).not.toBeInTheDocument()
  })

  it('picks up a job another user starts while this one is signed in', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    try {
      server.use(
        scanEvents(1, [
          {
            Id: 1,
            Status: 'Enumerating',
            FoldersScanned: 3,
            FilesFound: 40,
            FilesEnriched: 0,
            ErrorMessage: null,
          },
        ]),
      )
      renderApp('/folders/1')
      await screen.findByRole('navigation', { name: 'Folders' })
      expect(screen.queryByText(/^Scanning…/)).not.toBeInTheDocument()

      // Someone else starts a scan; nothing was running when this page loaded.
      server.use(
        activeJob({ kind: 'Scan', id: 1, folderId: 2, foldersProcessed: 3, filesFound: 40 }),
      )
      await act(async () => {
        vi.advanceTimersByTime(10_000)
      })

      expect((await screen.findAllByText('Scanning… 3 folders, 40 files')).length).toBeGreaterThan(
        0,
      )
    } finally {
      vi.useRealTimers()
    }
  })
})
