import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

const tileIds = () => screen.getAllByTestId(/^tile-/).map((tile) => tile.dataset.testid)

describe('AlbumView', () => {
  it('shows the header and the photos in album order', async () => {
    renderApp('/albums/5')
    expect(await screen.findByRole('heading', { name: 'Best of 2025' })).toBeInTheDocument()
    expect(screen.getByText('Keepers')).toBeInTheDocument()
    expect(screen.getByText('2 photos')).toBeInTheDocument()
    await screen.findByTestId('tile-20')
    expect(tileIds()).toEqual(['tile-21', 'tile-20'])
  })

  it('Show folders adds folder paths and is remembered', async () => {
    const { user } = renderApp('/albums/5')
    await screen.findByTestId('tile-20')
    expect(screen.queryByText('dev/Holidays/Madeira')).not.toBeInTheDocument()
    await user.click(screen.getByLabelText('Show folders'))
    expect(screen.getAllByText('dev/Holidays/Madeira')).toHaveLength(2)
    expect(localStorage.getItem('pm.albums.showFolders')).toBe('true')
  })

  it('keeps photos whose file is missing, with a placeholder', async () => {
    albumStore.get(5)!.missing.add(20)
    renderApp('/albums/5')
    expect(
      within(await screen.findByTestId('tile-20')).getByText('File missing'),
    ).toBeInTheDocument()
  })

  it.each(['/albums/404', '/albums/abc'])(
    'says an unknown album (%s) is not found',
    async (path) => {
      renderApp(path)
      expect(await screen.findByText('Album not found.')).toBeInTheDocument()
    },
  )

  it('says how to fill an empty album', async () => {
    renderApp('/albums/6')
    expect(
      await screen.findByText(
        'This album is empty. Select photos anywhere and choose Add to album.',
      ),
    ).toBeInTheDocument()
  })

  it('renames the album, and shows a taken name on the field', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'Edit…' }))
    const name = screen.getByRole('textbox', { name: 'Name' })
    await user.clear(name)
    await user.type(name, 'Empty')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByText('An album with this name already exists.')).toBeInTheDocument()
    await user.clear(name)
    await user.type(name, 'Keepers 2025')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByRole('heading', { name: 'Keepers 2025' })).toBeInTheDocument()
  })

  it('deletes after confirming, and goes to Albums', async () => {
    const { user, router } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'Delete…' }))
    expect(
      screen.getByText('Delete album Best of 2025? The photos stay in your library.'),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums'))
    expect(albumStore.get(5)).toBeUndefined()
  })

  it('removes selected photos after confirming', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Remove from album' }))
    expect(screen.getByText('Remove 1 photo from Best of 2025?')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Remove' }))
    expect(await screen.findByText('Removed 1 photo from Best of 2025.')).toBeInTheDocument()
    await waitFor(() => expect(tileIds()).toEqual(['tile-21']))
  })

  it('sets the one selected photo as the album cover', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Set as cover' }))
    expect(await screen.findByText('Cover of Best of 2025 updated.')).toBeInTheDocument()
    expect(albumStore.get(5)!.coverImageId).toBe(20)
  })

  it('offers Set as cover only while exactly one photo is selected', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0002.jpg' }))
    expect(screen.queryByRole('button', { name: 'Set as cover' })).not.toBeInTheDocument()
  })

  it('adding a selection with a missing file skips it and says so', async () => {
    albumStore.get(5)!.missing.add(20)
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0002.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(
      await screen.findByText('Added 1 photo to Empty (1 missing on disk was skipped).'),
    ).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([21])
  })

  it('the viewer steps through the album in album order', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'IMG_0002.jpg' }))
    expect(await screen.findByRole('img', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
    await user.keyboard('{ArrowRight}')
    expect(await screen.findByRole('img', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })

  it('a viewer can browse and export a shared album but not change it', async () => {
    albumStore.addShared({ id: 7, name: 'Bob trip', permission: 'Viewer', imageIds: [20, 21] })
    renderApp('/albums/7')
    expect(await screen.findByRole('heading', { name: 'Bob trip' })).toBeInTheDocument()
    expect(screen.getByText('Shared by Bob B')).toBeInTheDocument()
    expect(screen.getByText('Can view')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export…' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Leave…' })).toBeInTheDocument()
    for (const name of ['Sort by…', 'Edit…', 'Delete…', 'Share…'])
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
  })

  it('an editor can sort a shared album but not rename, delete or share it', async () => {
    albumStore.addShared({ id: 8, name: 'Bob edits', permission: 'Editor', imageIds: [20, 21] })
    renderApp('/albums/8')
    expect(await screen.findByRole('button', { name: 'Sort by…' })).toBeEnabled()
    expect(screen.getByText('Can edit')).toBeInTheDocument()
    for (const name of ['Edit…', 'Delete…', 'Share…'])
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
  })

  it('Shift+D in a view-only album explains and removes nothing', async () => {
    albumStore.addShared({ id: 7, name: 'Bob trip', permission: 'Viewer', imageIds: [20, 21] })
    const { user } = renderApp('/albums/7?image=20')
    expect(await screen.findByRole('img', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    await user.keyboard('{Shift>}D{/Shift}')
    expect(await screen.findByText('You can only view this album.')).toBeInTheDocument()
    expect(albumStore.get(7)!.imageIds).toEqual([20, 21])
  })

  it('leaving a shared album confirms, returns to Albums and drops it', async () => {
    albumStore.addShared({ id: 7, name: 'Bob trip', permission: 'Viewer' })
    const { user, router } = renderApp('/albums/7')
    await user.click(await screen.findByRole('button', { name: 'Leave…' }))
    expect(
      screen.getByText('Leave Bob trip? It disappears from your albums until Bob B shares it again.'),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Leave' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums'))
    expect(albumStore.get(7)!.shares).toEqual([])
  })
})
