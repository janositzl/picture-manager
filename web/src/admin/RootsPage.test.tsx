import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '../test/render'
import { RootsPage } from './RootsPage'

function renderPage() {
  return renderRoutes([{ path: '/admin/roots', element: <RootsPage /> }], '/admin/roots')
}

describe('RootsPage', () => {
  it('lists roots with their active state', async () => {
    renderPage()
    expect(await screen.findByDisplayValue('dev')).toBeInTheDocument()
    expect(screen.getByDisplayValue('archive')).toBeInTheDocument()
    expect(screen.getByDisplayValue('family_photos')).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'dev active' })).toBeChecked()
    expect(screen.getByRole('switch', { name: 'archive active' })).not.toBeChecked()
  })

  it('renames a root on blur', async () => {
    const { user } = renderPage()
    const nameField = await screen.findByDisplayValue('dev')
    await user.clear(nameField)
    await user.type(nameField, 'primary')
    await user.tab()
    await waitFor(() => expect(screen.getByDisplayValue('primary')).toBeInTheDocument())
  })

  it('shows a conflict when renaming to an existing name', async () => {
    const { user } = renderPage()
    const nameField = await screen.findByDisplayValue('dev')
    await user.clear(nameField)
    await user.type(nameField, 'archive')
    await user.tab()
    expect(
      await screen.findByText("A root with that name or export segment already exists."),
    ).toBeInTheDocument()
    expect(screen.getByDisplayValue('dev')).toBeInTheDocument()
  })

  it('activates a root', async () => {
    const { user } = renderPage()
    const toggle = await screen.findByRole('switch', { name: 'archive active' })
    await user.click(toggle)
    await waitFor(() => expect(toggle).toBeChecked())
  })

  it('creates a root and adds it to the table', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New root' }))
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'backup')
    await user.type(screen.getByRole('textbox', { name: 'Mount path' }), '/images/backup')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(screen.getByDisplayValue('backup')).toBeInTheDocument())
    expect(screen.queryByRole('dialog', { name: 'New root' })).not.toBeInTheDocument()
  })

  it('shows the name conflict on the field and stays open', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New root' }))
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'archive')
    await user.type(screen.getByRole('textbox', { name: 'Mount path' }), '/images/new')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    expect(
      await screen.findByText("A root named 'archive' already exists."),
    ).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'New root' })).toBeInTheDocument()
  })

  it('deletes a root after confirming', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete archive' }))
    await user.click(await screen.findByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(screen.queryByDisplayValue('archive')).not.toBeInTheDocument())
  })

  it('cancels a delete without removing the root', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete archive' }))
    await user.click(await screen.findByRole('button', { name: 'Cancel' }))
    expect(screen.getByDisplayValue('archive')).toBeInTheDocument()
  })
})
