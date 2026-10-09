import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { queryKeys } from '../api/queries'
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
})
