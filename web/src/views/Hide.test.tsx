import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'

const selectionBar = () => screen.findByRole('toolbar', { name: 'Selection' })

describe('hiding photos', () => {
  it('Hide in the selection bar removes the selected photos from the folder grid', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(within(await selectionBar()).getByRole('button', { name: 'Hide' }))
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'IMG_0001.jpg' })).not.toBeInTheDocument(),
    )
    expect(screen.getByRole('button', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
  })

  it('"Show hidden" lists hidden photos, and selecting them offers Unhide instead of Hide', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(within(await selectionBar()).getByRole('button', { name: 'Hide' }))
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'IMG_0001.jpg' })).not.toBeInTheDocument(),
    )

    await user.click(screen.getByRole('switch', { name: 'Show hidden' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    const bar = await selectionBar()
    expect(within(bar).queryByRole('button', { name: 'Hide' })).not.toBeInTheDocument()
    await user.click(within(bar).getByRole('button', { name: 'Unhide' }))

    await user.click(screen.getByRole('switch', { name: 'Show hidden' }))
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
  })

  it('a mixed selection (hidden and visible) offers Hide, not Unhide', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(within(await selectionBar()).getByRole('button', { name: 'Hide' }))
    await user.click(screen.getByRole('switch', { name: 'Show hidden' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0002.jpg' }))
    const bar = await selectionBar()
    expect(within(bar).getByRole('button', { name: 'Hide' })).toBeInTheDocument()
    expect(within(bar).queryByRole('button', { name: 'Unhide' })).not.toBeInTheDocument()
  })

  it('only the folder view offers Hide or Show hidden', async () => {
    const { user } = renderApp('/favorites')
    await user.click(await screen.findByRole('checkbox', { name: /^Select / }))
    expect(
      within(await selectionBar()).queryByRole('button', { name: 'Hide' }),
    ).not.toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Show hidden' })).not.toBeInTheDocument()
  })
})
