import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { image } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const aug14 = (id: number) => image(id, 3, { sortDate: '2025-08-14T10:00:00Z' })
const aug15 = (id: number) => image(id, 3, { sortDate: '2025-08-15T10:00:00Z' })

function serveTwoDays(requests: URLSearchParams[] = []) {
  server.use(
    http.get('/api/images', ({ request }) => {
      requests.push(new URL(request.url).searchParams)
      return HttpResponse.json({ items: [aug14(30), aug14(31), aug15(32)], nextCursor: null })
    }),
    http.get('/api/images/days', () =>
      HttpResponse.json([
        { day: '2025-08-14', count: 2 },
        { day: '2025-08-15', count: 1 },
      ]),
    ),
  )
}

describe('FolderView by day', () => {
  it('lists by date and shows a day bar with the first day selected', async () => {
    const requests: URLSearchParams[] = []
    serveTwoDays(requests)
    renderApp('/folders/3?group=day')

    expect(await screen.findByRole('button', { name: 'Choose a day' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous day' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next day' })).toBeEnabled()
    expect(requests[0]?.get('sort')).toBe('date')
  })

  it('lists the days with their counts in the day menu', async () => {
    serveTwoDays()
    const { user } = renderApp('/folders/3?group=day')

    await user.click(await screen.findByRole('button', { name: 'Choose a day' }))

    const items = await screen.findAllByRole('menuitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('2 photos')
    expect(items[1]).toHaveTextContent('1 photo')
  })

  it('locks the sort to date while grouped', async () => {
    serveTwoDays()
    renderApp('/folders/3?group=day&sort=name')

    expect(await screen.findByRole('button', { name: 'Choose a day' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: /Sort/ })).toHaveAttribute('aria-disabled', 'true')
  })

  it('has no day bar in All photos mode, and the toggle switches into By day', async () => {
    serveTwoDays()
    const { user, router } = renderApp('/folders/3')
    await screen.findByRole('button', { name: 'IMG_0030.jpg' })
    expect(screen.queryByRole('button', { name: 'Choose a day' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Group: By day' }))

    expect(await screen.findByRole('button', { name: 'Choose a day' })).toBeInTheDocument()
    await waitFor(() => expect(router.state.location.search).toContain('group=day'))
  })
})
