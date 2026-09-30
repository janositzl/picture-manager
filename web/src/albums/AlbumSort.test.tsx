import { screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

const tileIds = () => screen.getAllByTestId(/^tile-/).map((tile) => tile.dataset.testid)

beforeEach(() => {
  albumStore.get(5)!.imageIds = [22, 20, 21]
})

describe('sorting an album', () => {
  it('asks for confirmation, then stores the sorted order', async () => {
    const { user } = renderApp('/albums/5')
    await screen.findByTestId('tile-22')
    await user.click(screen.getByRole('button', { name: 'Sort by…' }))
    await user.click(await screen.findByRole('menuitem', { name: 'Filename (A to Z)' }))
    expect(albumStore.get(5)!.imageIds).toEqual([22, 20, 21])

    await user.click(await screen.findByRole('button', { name: 'Sort' }))

    await waitFor(() => expect(albumStore.get(5)!.imageIds).toEqual([20, 21, 22]))
    await waitFor(() => expect(tileIds()).toEqual(['tile-20', 'tile-21', 'tile-22']))
  })

  it('cancelling leaves the order alone', async () => {
    const { user } = renderApp('/albums/5')
    await screen.findByTestId('tile-22')
    await user.click(screen.getByRole('button', { name: 'Sort by…' }))
    await user.click(await screen.findByRole('menuitem', { name: 'Date taken (oldest first)' }))
    await user.click(await screen.findByRole('button', { name: 'Cancel' }))

    expect(albumStore.get(5)!.imageIds).toEqual([22, 20, 21])
  })
})
