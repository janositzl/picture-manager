import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const tileIds = () => screen.getAllByTestId(/^tile-/).map((tile) => tile.dataset.testid)

beforeEach(() => {
  albumStore.get(5)!.imageIds = [20, 21, 22]
  const original = HTMLElement.prototype.getBoundingClientRect
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    const index = this.closest<HTMLElement>('[data-sort-index]')?.dataset.sortIndex
    if (index === undefined) return original.call(this)
    const left = Number(index) * 200
    return {
      x: left,
      y: 0,
      left,
      top: 0,
      right: left + 180,
      bottom: 180,
      width: 180,
      height: 180,
      toJSON: () => ({}),
    } as DOMRect
  })
})

afterEach(() => vi.restoreAllMocks())

async function pickUp(name: string) {
  const tile = await screen.findByRole('button', { name })
  tile.focus()
  return tile
}

describe('reordering an album', () => {
  it('keyboard drag moves a photo and saves its new place', async () => {
    const { user } = renderApp('/albums/5')
    await pickUp('IMG_0001.jpg')
    await user.keyboard('[Space]')
    await user.keyboard('[ArrowRight]')
    await user.keyboard('[Space]')
    await waitFor(() => expect(tileIds()).toEqual(['tile-21', 'tile-20', 'tile-22']))
    await waitFor(() => expect(albumStore.get(5)!.imageIds).toEqual([21, 20, 22]))
  })

  it('moving to the front saves it there', async () => {
    const { user } = renderApp('/albums/5')
    await pickUp('IMG_0003.jpg')
    await user.keyboard('[Space]')
    await user.keyboard('[ArrowLeft]')
    await user.keyboard('[ArrowLeft]')
    await user.keyboard('[Space]')
    await waitFor(() => expect(albumStore.get(5)!.imageIds).toEqual([22, 20, 21]))
  })

  it('a failed save puts the order back', async () => {
    server.use(
      http.post('/api/albums/:id/images/:imageId/move', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    const { user } = renderApp('/albums/5')
    await pickUp('IMG_0001.jpg')
    await user.keyboard('[Space]')
    await user.keyboard('[ArrowRight]')
    await user.keyboard('[Space]')
    expect(await screen.findByText("Couldn't save the new order.")).toBeInTheDocument()
    expect(tileIds()).toEqual(['tile-20', 'tile-21', 'tile-22'])
  })

  it('a click still opens the viewer, and Enter too', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'IMG_0002.jpg' }))
    expect(await screen.findByRole('img', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
  })

  it('dragging is off while photos are selected', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0003.jpg' }))
    await pickUp('IMG_0001.jpg')
    await user.keyboard('[Space]')
    expect(await screen.findByRole('toolbar', { name: 'Selection' })).toHaveTextContent(
      '2 selected',
    )
    expect(albumStore.get(5)!.imageIds).toEqual([20, 21, 22])
  })
})
