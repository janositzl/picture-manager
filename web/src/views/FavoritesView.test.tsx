import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('FavoritesView', () => {
  it('lists only favorites, without a count', async () => {
    renderApp('/favorites')
    expect(await screen.findByRole('heading', { name: 'Favorites' })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'IMG_0001.jpg' })).not.toBeInTheDocument()
    expect(screen.queryByText(/photos?$/)).not.toBeInTheDocument()
  })

  it('keeps an unstarred photo in view, dimmed, until the view is left', async () => {
    const { user } = renderApp('/favorites')
    await user.click(
      await screen.findByRole('button', { name: 'Remove IMG_0002.jpg from favorites' }),
    )
    expect(
      await screen.findByRole('button', { name: 'Add IMG_0002.jpg to favorites' }),
    ).toBeInTheDocument()
    expect(screen.getByTestId('tile-21')).toHaveAttribute('data-dimmed', 'true')
  })

  it('says when there are no favorites', async () => {
    server.use(http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })))
    renderApp('/favorites')
    expect(
      await screen.findByText('No favorites yet. Star a photo with ★ or F in the viewer.'),
    ).toBeInTheDocument()
  })
})
