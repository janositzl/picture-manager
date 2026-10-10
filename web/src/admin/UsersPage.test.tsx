import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '../test/render'
import { deleteCalls, resetCalls } from '../test/userHandlers'
import { UsersPage } from './UsersPage'

function renderPage() {
  return renderRoutes([{ path: '/admin/users', element: <UsersPage /> }], '/admin/users')
}

describe('UsersPage', () => {
  it('lists users with role, folder-actions permission, status and album count', async () => {
    renderPage()
    const bob = (await screen.findByText('bob')).closest('tr')!
    expect(within(bob).getByText('Bob B')).toBeInTheDocument()
    expect(within(bob).getByText('User')).toBeInTheDocument()
    expect(within(bob).getByText('Active')).toBeInTheDocument()
    expect(within(bob).getByText('2')).toBeInTheDocument()

    const admin = screen.getByText('admin').closest('tr')!
    expect(within(admin).getByText('Admin')).toBeInTheDocument()
    expect(within(admin).getByText('Always')).toBeInTheDocument()

    expect(within(screen.getByText('carol').closest('tr')!).getByText('Disabled')).toBeInTheDocument()
    expect(screen.getByText('3 users')).toBeInTheDocument()
  })

  it('creates a user and shows them in the table', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    await user.type(screen.getByRole('textbox', { name: 'Username' }), 'dave')
    await user.type(screen.getByRole('textbox', { name: 'Display name' }), 'Dave D')
    await user.type(screen.getByLabelText('Initial password'), 'long-enough-pw')
    await user.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText('dave')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'New user' })).not.toBeInTheDocument()
  })

  it('shows a duplicate username on the dialog and keeps it open', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    await user.type(screen.getByRole('textbox', { name: 'Username' }), 'BOB')
    await user.type(screen.getByRole('textbox', { name: 'Display name' }), 'Other Bob')
    await user.type(screen.getByLabelText('Initial password'), 'long-enough-pw')
    await user.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText("A user named 'BOB' already exists.")).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'New user' })).toBeInTheDocument()
  })

  it('shows a short password as a field error', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    await user.type(screen.getByRole('textbox', { name: 'Username' }), 'erin')
    await user.type(screen.getByRole('textbox', { name: 'Display name' }), 'Erin E')
    await user.type(screen.getByLabelText('Initial password'), 'short')
    await user.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText('Must be at least 8 characters.')).toBeInTheDocument()
  })

  it('shows the folder-actions checkbox checked and disabled when the role is Admin', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    const checkbox = screen.getByRole('checkbox', { name: /Can run folder actions/ })
    expect(checkbox).not.toBeChecked()
    expect(checkbox).toBeEnabled()

    await user.click(screen.getByRole('combobox', { name: 'Role' }))
    await user.click(screen.getByRole('option', { name: 'Admin' }))

    expect(checkbox).toBeChecked()
    expect(checkbox).toBeDisabled()
  })

  it('offers a retry when the list cannot be loaded', async () => {
    const { server } = await import('../test/server')
    const { http, HttpResponse } = await import('msw')
    server.use(http.get('/api/users', () => HttpResponse.json({ title: 'x', status: 500 }, { status: 500 })))
    renderPage()
    expect(await screen.findByText("Couldn't load users.")).toBeInTheDocument()
  })

  it('edits a user: name, role and folder-actions permission', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Edit bob' }))
    const name = screen.getByRole('textbox', { name: 'Display name' })
    await user.clear(name)
    await user.type(name, 'Robert')
    await user.click(screen.getByRole('checkbox', { name: /Can run folder actions/ }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    const row = (await screen.findByText('Robert')).closest('tr')!
    expect(within(row).getByText('Yes')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Edit bob' })).not.toBeInTheDocument()
  })

  it('disables a user with the Active switch', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Edit bob' }))
    await user.click(screen.getByRole('switch', { name: 'Active' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(within(screen.getByText('bob').closest('tr')!).getByText('Disabled')).toBeInTheDocument())
  })

  it('locks role and Active on your own account', async () => {
    const { user } = renderPage()
    expect(await screen.findByRole('button', { name: 'Delete admin' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Edit admin' }))

    expect(screen.getByRole('switch', { name: 'Active' })).toBeDisabled()
    expect(screen.getByRole('combobox', { name: 'Role' })).toHaveAttribute('aria-disabled', 'true')
  })

  it('resets a password and shows a short one as an error', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Reset password for bob' }))
    await user.type(screen.getByLabelText('New password'), 'short')
    await user.click(screen.getByRole('button', { name: 'Reset' }))
    expect(await screen.findByText('Must be at least 8 characters.')).toBeInTheDocument()
    expect(resetCalls).toEqual([])

    await user.clear(screen.getByLabelText('New password'))
    await user.type(screen.getByLabelText('New password'), 'fresh-password')
    await user.click(screen.getByRole('button', { name: 'Reset' }))

    await waitFor(() => expect(resetCalls).toEqual([{ id: 5, newPassword: 'fresh-password' }]))
    expect(screen.queryByRole('dialog', { name: 'Reset password' })).not.toBeInTheDocument()
  })

  it('deletes a user, transferring their albums by default', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete bob' }))
    expect(screen.getByText(/owns 2 albums/)).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Transfer their albums to me/ })).toBeChecked()
    await user.click(screen.getByRole('button', { name: 'Delete user' }))

    await waitFor(() => expect(screen.queryByText('bob')).not.toBeInTheDocument())
    expect(deleteCalls).toEqual([{ id: 5, albums: 'transfer' }])
  })

  it('deletes a user together with their albums when chosen', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete bob' }))
    await user.click(screen.getByRole('radio', { name: /Delete their albums/ }))
    await user.click(screen.getByRole('button', { name: 'Delete user' }))

    await waitFor(() => expect(deleteCalls).toEqual([{ id: 5, albums: 'delete' }]))
  })

  it('does not offer an album choice for a user with no albums', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete carol' }))

    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Delete user' }))
    await waitFor(() => expect(deleteCalls).toEqual([{ id: 6, albums: 'transfer' }]))
  })
})
