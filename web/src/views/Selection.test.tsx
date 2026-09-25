import { screen, waitFor } from '@testing-library/react'
import { act } from 'react'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

const selectionBar = () => screen.findByRole('toolbar', { name: 'Selection' })

describe('selecting photos', () => {
  it('a ticked checkbox shows the selection bar, and clicks then toggle instead of opening', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    expect(await selectionBar()).toHaveTextContent('1 selected')
    await user.click(screen.getByRole('button', { name: 'IMG_0002.jpg' }))
    expect(await selectionBar()).toHaveTextContent('2 selected')
    expect(router.state.location.search).toBe('')
  })

  it('Shift-click selects a range, Esc clears, Ctrl+A selects all', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.keyboard('{Shift>}')
    await user.click(screen.getByRole('button', { name: 'IMG_0003.jpg' }))
    await user.keyboard('{/Shift}')
    expect(await selectionBar()).toHaveTextContent('3 selected')
    await user.keyboard('{Escape}')
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0002.jpg' }))
    await user.keyboard('{Control>}a{/Control}')
    expect(await selectionBar()).toHaveTextContent('3 selected')
  })

  it('Ctrl-click starts a selection', async () => {
    const { user } = renderApp('/folders/3')
    const tile = await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    await user.keyboard('{Control>}')
    await user.click(tile)
    await user.keyboard('{/Control}')
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('the selection clears when moving to another folder', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await selectionBar()
    await act(() => router.navigate('/folders/2'))
    expect(await screen.findByRole('heading', { name: 'Holidays' })).toBeInTheDocument()
    expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument()
  })

  it('adds the selection to an album and clears it', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0003.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 2 photos to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([20, 22])
    await waitFor(() =>
      expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument(),
    )
  })

  it('Esc closes the picker but keeps the selection', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await screen.findByRole('dialog', { name: 'Add to album' })
    await user.keyboard('{Escape}')
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Add to album' })).not.toBeInTheDocument(),
    )
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('Ctrl+A in the search box does not select photos', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    screen.getByRole('textbox', { name: 'Search file names' }).focus()
    await user.keyboard('{Control>}a{/Control}')
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('adds a whole folder from its header', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'Add folder to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 3 photos to Empty.')).toBeInTheDocument()
  })

  it('offers no folder add when the folder has no photos of its own', async () => {
    renderApp('/folders/1')
    expect(await screen.findByRole('heading', { name: 'dev' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add folder to album…' })).not.toBeInTheDocument()
  })
})
