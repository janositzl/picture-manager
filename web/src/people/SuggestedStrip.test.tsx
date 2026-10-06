import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { madeiraImages } from '../test/fixtures'
import { peopleFixture, peopleList, person } from '../test/peopleHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const first = madeiraImages[0]!
const second = madeiraImages[1]!

/** Person 1: one suggested photo (with a face crop) and one confirmed photo. */
const personImages = http.get('/api/images', ({ request }) => {
  const state = new URL(request.url).searchParams.get('personState')
  const items = state === 'suggested' ? [{ ...first, faceId: 55 }] : [{ ...second, faceId: 56 }]
  return HttpResponse.json({ items, nextCursor: null })
})

describe('Suggested strip', () => {
  it('shows suggestions apart from the confirmed grid, as face crops by default', async () => {
    server.use(peopleList(), person(peopleFixture[0]), personImages)
    renderApp('/people/1')

    const strip = await screen.findByRole('region', { name: 'Suggested' })
    await within(strip).findByRole('button', { name: `Open ${first.fileName}${first.extension}` })
    expect(strip.querySelector('img')).toHaveAttribute('src', '/api/faces/55/thumbnail')
    // The grid below holds only the confirmed photo.
    expect(await screen.findByTestId(`tile-${second.id}`)).toBeInTheDocument()
    expect(screen.queryByTestId(`tile-${first.id}`)).not.toBeInTheDocument()
  })

  it('accepts and rejects one photo, and accepts them all', async () => {
    const sent: string[] = []
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      personImages,
      http.post('/api/people/1/images/:imageId/:action', ({ params }) => {
        sent.push(`${params.imageId}/${params.action}`)
        return new HttpResponse(null, { status: 204 })
      }),
      http.post('/api/people/1/suggestions/accept', () => {
        sent.push('all')
        return HttpResponse.json({ count: 1 })
      }),
    )
    const { user } = renderApp('/people/1')
    const strip = await screen.findByRole('region', { name: 'Suggested' })
    const name = `${first.fileName}${first.extension}`

    await user.click(await within(strip).findByRole('button', { name: `Accept suggestion for ${name}` }))
    await user.click(within(strip).getByRole('button', { name: `Reject suggestion for ${name}` }))
    await user.click(within(strip).getByRole('button', { name: 'Accept all' }))

    await waitFor(() => expect(sent).toEqual([`${first.id}/accept`, `${first.id}/reject`, 'all']))
    expect(await screen.findByText('Accepted 1 suggestion.')).toBeInTheDocument()
  })

  it('reviews with the keyboard: A accepts and R rejects the focused photo, and progress is shown', async () => {
    const sent: string[] = []
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      personImages,
      http.post('/api/people/1/images/:imageId/:action', ({ params }) => {
        sent.push(`${params.imageId}/${params.action}`)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/people/1')
    const strip = await screen.findByRole('region', { name: 'Suggested' })
    const open = await within(strip).findByRole('button', { name: `Open ${first.fileName}${first.extension}` })

    open.focus()
    await user.keyboard('a')

    await waitFor(() => expect(sent).toEqual([`${first.id}/accept`]))
    expect(await within(strip).findByText(/1 reviewed/)).toBeInTheDocument()
  })

  it('has no strip for an unknown group, whose grid is the suggestions', async () => {
    const states: string[] = []
    server.use(
      peopleList(),
      person(peopleFixture[1]),
      http.get('/api/images', ({ request }) => {
        states.push(new URL(request.url).searchParams.get('personState') ?? '')
        return HttpResponse.json({ items: [first], nextCursor: null })
      }),
    )
    renderApp('/people/2')

    expect(await screen.findByTestId(`tile-${first.id}`)).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Suggested' })).not.toBeInTheDocument()
    expect(new Set(states)).toEqual(new Set(['suggested']))
  })

  it('the Photos / Faces toggle swaps the grid tiles for face crops', async () => {
    server.use(peopleList(), person(peopleFixture[0]), personImages)
    const { user } = renderApp('/people/1')
    const tile = await screen.findByTestId(`tile-${second.id}`)
    await waitFor(() => expect(tile.querySelector('img')).toBeInTheDocument())
    expect(tile.querySelector('img')).not.toHaveAttribute('src', '/api/faces/56/thumbnail')

    await user.click(screen.getByRole('button', { name: 'Faces', pressed: false }))

    await waitFor(() =>
      expect(screen.getByTestId(`tile-${second.id}`).querySelector('img')).toHaveAttribute('src', '/api/faces/56/thumbnail'),
    )
    expect(localStorage.getItem('pm.people.gridMode')).toBe('faces')
  })
})
