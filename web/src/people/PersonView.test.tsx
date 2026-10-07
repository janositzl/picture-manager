import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { peopleKeys } from '../api/people'
import { peopleFixture, peopleList, person, type PersonDto } from '../test/peopleHandlers'
import { image } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('PersonView', () => {
  /** Group 2 (unnamed) is open; the PATCH answers with `survivor`. Resolves once the people list is cached. */
  async function openGroup(survivor: PersonDto) {
    const sent: string[] = []
    server.use(
      peopleList(),
      person(peopleFixture[1]),
      person(peopleFixture[0]),
      http.patch('/api/people/2', async ({ request }) => {
        sent.push(((await request.json()) as { name: string }).name)
        return HttpResponse.json(survivor)
      }),
    )
    const { queryClient } = renderApp('/people/2')
    await waitFor(() => expect(queryClient.getQueryData(peopleKeys.all())).toBeDefined())
    return sent
  }

  async function openEditor() {
    fireEvent.click(await screen.findByRole('button', { name: 'Edit person' }))
    return screen.findByRole('dialog', { name: 'Edit person' })
  }

  async function saveName(name: string) {
    await openEditor()
    fireEvent.change(await screen.findByLabelText('Name this person'), { target: { value: name } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
  }

  it('asks before naming a group after an existing person, then merges into the survivor', async () => {
    const sent = await openGroup(peopleFixture[0])

    await saveName(' anna ')

    const dialog = await screen.findByRole('dialog', { name: 'Merge people?' })
    expect(dialog).toHaveTextContent("Merge into existing 'Anna' (13 photos)? This can't be undone.")
    expect(sent).toEqual([])

    fireEvent.click(within(dialog).getByRole('button', { name: 'Merge' }))

    await waitFor(() => expect(sent).toEqual([' anna ']))
    expect(await screen.findByRole('heading', { name: 'Anna' })).toBeInTheDocument()
  })

  it('sends nothing when the merge is cancelled', async () => {
    const sent = await openGroup(peopleFixture[0])

    await saveName('Anna')
    fireEvent.click(within(await screen.findByRole('dialog', { name: 'Merge people?' })).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Merge people?' })).not.toBeInTheDocument())
    expect(sent).toEqual([])
  })

  it('saves a brand-new name at once, without asking', async () => {
    const sent = await openGroup({ ...peopleFixture[1], name: 'Bela' })

    await saveName('Bela')

    await waitFor(() => expect(sent).toEqual(['Bela']))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('does not treat renaming a person to its own name as a merge', async () => {
    let sentName: unknown = null
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      http.patch('/api/people/1', async ({ request }) => {
        sentName = ((await request.json()) as { name: string }).name
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    const { queryClient } = renderApp('/people/1')
    await waitFor(() => expect(queryClient.getQueryData(peopleKeys.all())).toBeDefined())

    await openEditor()
    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'ANNA' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))

    await waitFor(() => expect(sentName).toBe('ANNA'))
    expect(screen.queryByRole('dialog', { name: 'Merge people?' })).not.toBeInTheDocument()
  })

  it('shows the confirmed photo grid filtered by person', async () => {
    let requested: string | null = null
    server.use(
      person(peopleFixture[0]),
      http.get('/api/images', ({ request }) => {
        const params = new URL(request.url).searchParams
        if (params.get('personState') === 'confirmed') requested = params.get('personId')
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    renderApp('/people/1')

    await waitFor(() => expect(requested).toBe('1'))
    expect(await screen.findByRole('heading', { name: 'Anna' })).toBeInTheDocument()
    expect(screen.getByText('10 photos')).toBeInTheDocument()
    await openEditor()
    expect(screen.getByText('10 confirmed · 3 suggested')).toBeInTheDocument()
  })

  it('deletes a person after confirming, then returns to the people list', async () => {
    let deleted = false
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })),
      http.delete('/api/people/1', () => {
        deleted = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderApp('/people/1')

    await openEditor()
    fireEvent.click(screen.getByRole('button', { name: 'Delete person' }))
    expect(deleted).toBe(false)
    fireEvent.click(within(await screen.findByRole('dialog', { name: 'Delete person?' })).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(deleted).toBe(true))
    expect(await screen.findByText('Select a person to see their photos.')).toBeInTheDocument()
  })

  it('ignores an unknown group after confirming; named people have no ignore button', async () => {
    let ignored = false
    server.use(
      peopleList(),
      person(peopleFixture[1]),
      http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })),
      http.post('/api/people/2/ignore', () => {
        ignored = true
        return HttpResponse.json({ count: 5 })
      }),
    )
    renderApp('/people/2')

    await openEditor()
    fireEvent.click(screen.getByRole('button', { name: 'Ignore this group' }))
    fireEvent.click(within(await screen.findByRole('dialog', { name: 'Ignore this group?' })).getByRole('button', { name: 'Ignore' }))

    await waitFor(() => expect(ignored).toBe(true))
    expect(await screen.findByText('Select a person to see their photos.')).toBeInTheDocument()
  })

  it('ignores an unknown group straight from the header button', async () => {
    let ignored = false
    server.use(
      peopleList(),
      person(peopleFixture[1]),
      http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })),
      http.post('/api/people/2/ignore', () => {
        ignored = true
        return HttpResponse.json({ count: 5 })
      }),
    )
    renderApp('/people/2')

    fireEvent.click(await screen.findByRole('button', { name: 'Ignore group' }))
    fireEvent.click(within(await screen.findByRole('dialog', { name: 'Ignore this group?' })).getByRole('button', { name: 'Ignore' }))

    await waitFor(() => expect(ignored).toBe(true))
    expect(await screen.findByText('Select a person to see their photos.')).toBeInTheDocument()
  })

  it('assigns an unknown group to a named person after confirming, then opens that person', async () => {
    let body: unknown = null
    server.use(
      peopleList(),
      person(peopleFixture[1]),
      person(peopleFixture[0]),
      http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })),
      http.post('/api/people/2/assign', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    renderApp('/people/2')

    fireEvent.click(await screen.findByRole('button', { name: 'Assign to person' }))
    const picker = await screen.findByRole('dialog', { name: 'Assign to person' })
    fireEvent.click(await within(picker).findByRole('button', { name: /Anna/ }))
    const confirm = await screen.findByRole('dialog', { name: 'Assign to person?' })
    expect(body).toBeNull()
    fireEvent.click(within(confirm).getByRole('button', { name: 'Assign' }))

    await waitFor(() => expect(body).toEqual({ personId: peopleFixture[0].id }))
    expect(await screen.findByRole('heading', { name: 'Anna' })).toBeInTheDocument()
  })

  it('assigns only the selected photos of an unknown group, staying on the group', async () => {
    let body: unknown = null
    server.use(
      peopleList(),
      person(peopleFixture[1]),
      person(peopleFixture[0]),
      http.get('/api/images', () =>
        HttpResponse.json({ items: [image(101, 2), image(102, 2)], nextCursor: null }),
      ),
      http.post('/api/people/2/assign', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    renderApp('/people/2')

    fireEvent.click(await screen.findByRole('checkbox', { name: /Select IMG_0101/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Assign to person…' }))
    const picker = await screen.findByRole('dialog', { name: 'Assign to person' })
    fireEvent.click(await within(picker).findByRole('button', { name: /Anna/ }))
    const confirm = await screen.findByRole('dialog', { name: 'Assign to person?' })
    expect(confirm).toHaveTextContent('Assign 1 of 5 photos')
    fireEvent.click(within(confirm).getByRole('button', { name: 'Assign' }))

    await waitFor(() => expect(body).toEqual({ personId: peopleFixture[0].id, imageIds: [101] }))
    await waitFor(() => expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument())
  })

  it('offers no assign for a named person', async () => {
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })),
    )
    renderApp('/people/1')

    await screen.findByRole('button', { name: 'Edit person' })
    expect(screen.queryByRole('button', { name: 'Assign to person' })).not.toBeInTheDocument()
  })

  it('offers no ignore for a named person', async () => {
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })),
    )
    renderApp('/people/1')

    await openEditor()
    expect(screen.getByRole('button', { name: 'Delete person' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ignore this group' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ignore group' })).not.toBeInTheDocument()
  })

  it('shows not found, without the grid, when the person does not exist', async () => {
    server.use(
      http.get('/api/people/9999', () => HttpResponse.json({ title: 'Not found' }, { status: 404 })),
      http.get('/api/images', () => {
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    renderApp('/people/9999')

    expect(await screen.findByText('Person not found')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to People' })).toHaveAttribute('href', '/people')
    // the grid is not rendered (it may have started loading while the person query was pending)
    expect(screen.queryByText('No photos for this person.')).not.toBeInTheDocument()
  })

  it('shows not found without any request for a non-numeric id', async () => {
    const requested: string[] = []
    server.use(
      http.get('/api/people/:id', ({ request }) => {
        requested.push(request.url)
        return HttpResponse.json({}, { status: 404 })
      }),
      http.get('/api/images', ({ request }) => {
        requested.push(request.url)
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    renderApp('/people/abc')

    expect(await screen.findByText('Person not found')).toBeInTheDocument()
    expect(requested).toEqual([])
  })

  it("sets the single selected photo's face as the person's cover", async () => {
    const sent: unknown[] = []
    server.use(
      peopleList(),
      person(peopleFixture[0]),
      http.get('/api/images', () =>
        HttpResponse.json({
          items: [image(20, 3, { faceId: 301 }), image(21, 3, { faceId: 302 })],
          nextCursor: null,
        }),
      ),
      http.put('/api/people/1/cover', async ({ request }) => {
        sent.push(await request.json())
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    const { user } = renderApp('/people/1')

    await user.click((await screen.findAllByRole('checkbox', { name: 'Select IMG_0020.jpg' }))[0]!)
    await user.click(screen.getByRole('button', { name: 'Set as cover' }))

    await waitFor(() => expect(sent).toEqual([{ faceId: 301 }]))
    expect(await screen.findByText('Cover updated.')).toBeInTheDocument()
  })
})
