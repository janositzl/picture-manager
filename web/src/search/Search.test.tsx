import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { holidaysImages, madeiraImages } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const searchBox = () => screen.findByRole('textbox', { name: 'Search file names' })

describe('Search', () => {
  it('navigates to the results after the debounce, not on every keystroke', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.type(await searchBox(), 'IMG')
    expect(router.state.location.pathname).toBe('/folders/3')
    await waitFor(() => expect(router.state.location.pathname).toBe('/search'))
    expect(router.state.location.search).toBe('?q=IMG')
    expect(await screen.findByRole('heading', { name: 'Search: IMG' })).toBeInTheDocument()
  })

  it("shows each result's folder path, and scopes to the folder when the chip is on", async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'In this folder' }))
    await user.type(await searchBox(), 'IMG_0001')
    expect(await screen.findByRole('heading', { name: 'Search: IMG_0001' })).toBeInTheDocument()
    expect(router.state.location.search).toBe('?q=IMG_0001&in=3')
    const tile = await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    expect(within(tile).getByText('dev/Holidays/Madeira')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'IMG_0001 copy.jpg' })).not.toBeInTheDocument()
  })

  it('searches everywhere once the scope chip is removed', async () => {
    const { user, router } = renderApp('/search?q=IMG_0001&in=3')
    const chip = await screen.findByRole('button', { name: 'in: Madeira' })
    await user.click(within(chip).getByTestId('CancelIcon'))
    await waitFor(() => expect(router.state.location.search).toBe('?q=IMG_0001'))
    expect(await screen.findByRole('button', { name: 'IMG_0001 copy.jpg' })).toBeInTheDocument()
  })

  it('lists every photo when the query is empty, without a file-name filter', async () => {
    const sentFileName: boolean[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        sentFileName.push(new URL(request.url).searchParams.has('fileName'))
        return HttpResponse.json({ items: [...holidaysImages, ...madeiraImages], nextCursor: null })
      }),
    )
    renderApp('/search')
    expect(await screen.findByRole('heading', { name: 'All photos' })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'screenshot.png' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
    expect(sentFileName).toEqual([false])
  })

  it('clearing the search box shows every photo again', async () => {
    const { user } = renderApp('/search?q=IMG_0001')
    await screen.findByRole('heading', { name: 'Search: IMG_0001' })
    await user.clear(await searchBox())
    expect(await screen.findByRole('heading', { name: 'All photos' })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'screenshot.png' })).toBeInTheDocument()
  })

  it('says when nothing matches', async () => {
    renderApp('/search?q=zzz')
    expect(await screen.findByText('No photos match “zzz”.')).toBeInTheDocument()
  })

  it('round-trips special characters in the query', async () => {
    const seen: (string | null)[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        seen.push(new URL(request.url).searchParams.get('fileName'))
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    const { user, router } = renderApp('/folders/3')
    await user.type(await searchBox(), 'a&b #1 50%+')
    await waitFor(() => expect(router.state.location.pathname).toBe('/search'))
    expect(new URLSearchParams(router.state.location.search).get('q')).toBe('a&b #1 50%+')
    await waitFor(() => expect(seen).toContain('a&b #1 50%+'))
  })
})
