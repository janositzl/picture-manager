import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '../test/render'
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
})
