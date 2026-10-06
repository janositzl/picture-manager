import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { ImageFace } from '../api/people'
import { image, madeiraImages } from '../test/fixtures'
import { peopleFixture, peopleList, person } from '../test/peopleHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const confirmed: ImageFace = { id: 1, x: 0.1, y: 0.1, width: 0.2, height: 0.2, state: 'confirmed', personId: 1, personName: 'Anna' }
const suggested: ImageFace = { id: 2, x: 0.5, y: 0.1, width: 0.2, height: 0.2, state: 'suggested', personId: 1, personName: 'Anna' }
const unknown: ImageFace = { id: 3, x: 0.7, y: 0.5, width: 0.1, height: 0.1, state: 'unknown', personId: null, personName: null }

/** Serves a photo's faces: confirmed-only requests get just the confirmed ones, like the real API. */
function faces(all: ImageFace[]) {
  return http.get('/api/images/:id/faces', ({ request }) => {
    const params = new URL(request.url).searchParams
    const list = params.get('confirmedOnly') === 'true' ? all.filter((f) => f.state === 'confirmed') : all
    return HttpResponse.json(list)
  })
}

const personImages = http.get('/api/images', () => HttpResponse.json({ items: madeiraImages, nextCursor: null }))

describe('viewer faces', () => {
  it('the normal viewer lists only confirmed people, with no boxes or actions', async () => {
    server.use(faces([confirmed, suggested, unknown]))
    renderApp('/folders/3?image=20')

    const panel = await screen.findByRole('complementary', { name: 'Photo info' })
    expect(await within(panel).findByRole('link', { name: 'Anna' })).toHaveAttribute('href', '/people/1')
    expect(screen.queryByRole('region', { name: 'People in photo' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Detected faces')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Accept' })).not.toBeInTheDocument()
  })

  it('opened from a person, it shows every face and the decisions for the selected one', async () => {
    server.use(peopleList(), person(peopleFixture[0]), personImages, faces([confirmed, suggested, unknown]))
    renderApp('/people/1?image=20')

    const section = await screen.findByRole('region', { name: 'People in photo' })
    expect(await within(section).findByRole('button', { name: 'Anna' })).toBeInTheDocument()
    expect(within(section).getByRole('button', { name: 'Suggested: Anna' })).toBeInTheDocument()
    expect(within(section).getByRole('button', { name: 'Unknown' })).toBeInTheDocument()
    // Starts on the opened person's face, which is confirmed.
    expect(within(section).getByText('Confirmed')).toBeInTheDocument()
    expect(within(section).getByRole('button', { name: 'Mark as unknown' })).toBeInTheDocument()
  })

  it('is its own panel: the photo name and path, with the other details left to the Info panel', async () => {
    server.use(peopleList(), person(peopleFixture[0]), personImages, faces([confirmed]))
    renderApp('/people/1?image=20')

    const panel = await screen.findByRole('complementary', { name: 'Face review' })
    expect(within(panel).getByText('IMG_0001.jpg')).toBeInTheDocument()
    expect(within(panel).queryByText('Date taken')).not.toBeInTheDocument()
    expect(within(panel).queryByText('Dimensions')).not.toBeInTheDocument()
    expect(screen.queryByRole('complementary', { name: 'Photo info' })).not.toBeInTheDocument()
  })

  it('accepting a suggested face sends the decision for that face only', async () => {
    const sent: string[] = []
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      personImages,
      faces([confirmed, suggested, unknown]),
      http.post('/api/faces/:id/:action', ({ params }) => {
        sent.push(`${params.id}/${params.action}`)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/people/1?image=20')

    const section = await screen.findByRole('region', { name: 'People in photo' })
    await user.click(await within(section).findByRole('button', { name: 'Suggested: Anna' }))
    await user.click(within(section).getByRole('button', { name: 'Accept' }))

    await waitFor(() => expect(sent).toEqual(['2/accept']))
  })

  it('assigning an unknown face to a new name creates that person', async () => {
    let body: unknown = null
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      personImages,
      faces([confirmed, unknown]),
      http.post('/api/faces/3/assign', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    const { user } = renderApp('/people/1?image=20')

    const section = await screen.findByRole('region', { name: 'People in photo' })
    await user.click(await within(section).findByRole('button', { name: 'Unknown' }))
    await user.click(within(section).getByRole('button', { name: 'Assign person' }))
    await user.type(await screen.findByLabelText('Person name'), 'Bela')
    await user.click(screen.getByRole('button', { name: /Create new person .*Bela/ }))

    await waitFor(() => expect(body).toEqual({ name: 'Bela' }))
  })

  it('assigning picks an existing person from the list, and Enter creates the typed name', async () => {
    const bodies: unknown[] = []
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      personImages,
      faces([confirmed, unknown]),
      http.post('/api/faces/3/assign', async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    const { user } = renderApp('/people/1?image=20')

    const section = await screen.findByRole('region', { name: 'People in photo' })
    await user.click(await within(section).findByRole('button', { name: 'Unknown' }))
    await user.click(within(section).getByRole('button', { name: 'Assign person' }))
    await user.click(await screen.findByRole('button', { name: /Anna/ }))
    await waitFor(() => expect(bodies).toEqual([{ personId: 1 }]))

    await user.click(await within(section).findByRole('button', { name: 'Unknown' }))
    await user.click(within(section).getByRole('button', { name: 'Assign person' }))
    await user.type(await screen.findByLabelText('Person name'), 'Cili{Enter}')
    await waitFor(() => expect(bodies).toEqual([{ personId: 1 }, { name: 'Cili' }]))
  })
})

describe('viewer navigation in an unknown group', () => {
  it('keeps the arrows when the photo leaves the group after its people are assigned', async () => {
    let items = [image(20, 3), image(21, 3), image(22, 3)]
    server.use(
      peopleList(),
      person(peopleFixture[1]),
      faces([suggested]),
      http.get('/api/images', () => HttpResponse.json({ items, nextCursor: null })),
    )
    const { queryClient } = renderApp('/people/2?image=21')

    expect(await screen.findByRole('button', { name: 'Previous photo' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next photo' })).toBeInTheDocument()

    // The photo no longer belongs to the group, so the refreshed list drops it.
    items = items.filter((item) => item.id !== 21)
    await queryClient.invalidateQueries({ predicate: (q) => q.queryKey[0] === 'images' })

    await waitFor(() => expect(screen.queryByRole('img', { name: /IMG_0021/ })).toBeDefined())
    expect(await screen.findByRole('button', { name: 'Previous photo' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next photo' })).toBeInTheDocument()
  })
})
