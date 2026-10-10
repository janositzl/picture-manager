import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

async function openShare() {
  const view = renderApp('/albums/5')
  await view.user.click(await screen.findByRole('button', { name: 'Share…' }))
  return { ...view, dialog: await screen.findByRole('dialog', { name: 'Share Best of 2025' }) }
}

describe('ShareDialog', () => {
  it('starts with only the owner and says so', async () => {
    const { dialog } = await openShare()
    const people = within(dialog).getByRole('list', { name: 'People with access' })
    expect(within(people).getByText('Administrator')).toBeInTheDocument()
    expect(within(people).getByText('Owner')).toBeInTheDocument()
    expect(await within(dialog).findByText('Only you can see this album.')).toBeInTheDocument()
  })

  it('shares with a person who can view, and lists them', async () => {
    const { user, dialog } = await openShare()
    await user.click(within(dialog).getByRole('combobox', { name: 'Add people' }))
    await user.click(await screen.findByRole('option', { name: 'Bob B' }))
    await user.click(within(dialog).getByRole('button', { name: 'Share' }))

    expect(await within(dialog).findByRole('combobox', { name: 'Access for Bob B' })).toHaveTextContent('Can view')
    expect(albumStore.get(5)!.shares).toEqual([{ userId: 5, displayName: 'Bob B', permission: 'Viewer' }])
  })

  it('shares as an editor when chosen', async () => {
    const { user, dialog } = await openShare()
    await user.click(within(dialog).getByRole('combobox', { name: 'Add people' }))
    await user.click(await screen.findByRole('option', { name: 'Bob B' }))
    await user.click(within(dialog).getByRole('combobox', { name: 'Access' }))
    await user.click(await screen.findByRole('option', { name: 'Can edit' }))
    await user.click(within(dialog).getByRole('button', { name: 'Share' }))

    await waitFor(() => expect(albumStore.get(5)!.shares[0]?.permission).toBe('Editor'))
  })

  it('changes a person’s access and removes them', async () => {
    albumStore.get(5)!.shares.push({ userId: 5, displayName: 'Bob B', permission: 'Viewer' })
    const { user, dialog } = await openShare()
    await user.click(await within(dialog).findByRole('combobox', { name: 'Access for Bob B' }))
    await user.click(await screen.findByRole('option', { name: 'Can edit' }))
    await waitFor(() => expect(albumStore.get(5)!.shares[0]?.permission).toBe('Editor'))

    await user.click(within(dialog).getByRole('button', { name: 'Remove Bob B' }))
    await waitFor(() => expect(albumStore.get(5)!.shares).toEqual([]))
    expect(await within(dialog).findByText('Only you can see this album.')).toBeInTheDocument()
  })

  it('does not offer people who already have access, or disabled accounts', async () => {
    albumStore.get(5)!.shares.push({ userId: 5, displayName: 'Bob B', permission: 'Viewer' })
    const { user, dialog } = await openShare()
    await within(dialog).findByRole('combobox', { name: 'Access for Bob B' })
    await user.click(within(dialog).getByRole('combobox', { name: 'Add people' }))
    expect(await screen.findByText('Everyone already has access.')).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Carol C' })).not.toBeInTheDocument()
  })
})
