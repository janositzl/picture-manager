import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp, renderRoutes } from '../test/render'
import { server } from '../test/server'
import { AlbumsPage } from './AlbumsPage'

function renderPage() {
  return renderRoutes(
    [
      { path: '/albums', element: <AlbumsPage /> },
      { path: '/albums/:albumId', element: <p>Album page</p> },
    ],
    '/albums',
  )
}

describe('AlbumsPage', () => {
  it('shows album cards, most recently updated first', async () => {
    renderPage()
    const cards = await screen.findAllByRole('link')
    expect(cards.map((c) => c.getAttribute('aria-label'))).toEqual(['Best of 2025', 'Empty'])
    expect(screen.getByText('2 photos · Updated 2026-09-20')).toBeInTheDocument()
  })

  it('says how to start when there are no albums', async () => {
    server.use(http.get('/api/albums', () => HttpResponse.json([])))
    renderPage()
    expect(
      await screen.findByText(
        'No albums yet. Create one, or select photos anywhere and choose Add to album.',
      ),
    ).toBeInTheDocument()
  })

  it('creates an album and opens it', async () => {
    const { user, router } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Trip')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums/100'))
  })

  it('shows the name conflict on the field and stays open', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Empty')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    expect(await screen.findByText('An album with this name already exists.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'New album' })).toBeInTheDocument()
  })

  it('is reachable from the app bar', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('link', { name: 'Albums' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums'))
    expect(await screen.findByRole('heading', { name: 'Albums' })).toBeInTheDocument()
  })
})
