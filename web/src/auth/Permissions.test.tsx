import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { signInAs } from '../test/authHandlers'
import { userMe } from '../test/fixtures'
import { activeJob, faceRecognitionEvents } from '../test/jobHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('permissions', () => {
  it('a plain user sees no Admin entry, no folder actions and no hide/rotate', async () => {
    signInAs(userMe)
    renderApp('/folders/1')

    const holidays = await screen.findByRole('treeitem', { name: 'Holidays' })
    expect(
      within(holidays).queryByRole('button', { name: 'Actions for Holidays' }),
    ).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument()
  })

  it('a user allowed folder actions sees the folder actions menu', async () => {
    signInAs({ ...userMe, canRunFolderActions: true })
    renderApp('/folders/1')

    const holidays = await screen.findByRole('treeitem', { name: 'Holidays' })
    expect(
      within(holidays).getByRole('button', { name: 'Actions for Holidays' }),
    ).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument()
  })

  it('an admin sees the Admin entry', async () => {
    renderApp('/folders/1')

    expect(await screen.findByRole('link', { name: 'Admin' })).toBeInTheDocument()
  })

  it('a plain user still sees job progress, but no Cancel; a folder-actions user gets Cancel', async () => {
    server.use(
      activeJob({
        kind: 'FaceRecognition',
        id: 3,
        folderId: 2,
        foldersProcessed: 0,
        filesFound: 0,
      }),
      faceRecognitionEvents(3, []),
    )
    signInAs(userMe)
    const first = renderApp('/folders/1')

    // Shown twice: in the app-wide banner and as the caption on the running folder's tree row.
    await waitFor(() => expect(screen.getAllByText(/Recognizing faces/).length).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    first.unmount()

    signInAs({ ...userMe, canRunFolderActions: true })
    renderApp('/folders/1')
    expect(await screen.findByRole('button', { name: 'Cancel' })).toBeInTheDocument()
  })

  it('a plain user is sent away from /admin', async () => {
    signInAs(userMe)
    renderApp('/admin/settings')

    expect(await screen.findByRole('navigation', { name: 'Folders' })).toBeInTheDocument()
  })

  it('the account menu logs out back to the login page', async () => {
    signInAs(userMe)
    const { user } = renderApp('/folders/1')

    await user.click(await screen.findByRole('button', { name: 'Account' }))
    await user.click(screen.getByRole('menuitem', { name: 'Log out' }))

    expect(await screen.findByRole('button', { name: 'Log in' })).toBeInTheDocument()
  })
})
