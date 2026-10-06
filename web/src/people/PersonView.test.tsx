import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { peopleKeys } from '../api/people'
import { peopleFixture, peopleList, person, type PersonDto } from '../test/peopleHandlers'
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

  async function saveName(name: string) {
    fireEvent.change(await screen.findByLabelText('Name this person'), { target: { value: name } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
  }

  it('asks before naming a group after an existing person, then merges into the survivor', async () => {
    const sent = await openGroup(peopleFixture[0])

    await saveName(' anna ')

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent("Merge into existing 'Anna' (13 photos)? This can't be undone.")
    expect(sent).toEqual([])

    fireEvent.click(within(dialog).getByRole('button', { name: 'Merge' }))

    await waitFor(() => expect(sent).toEqual([' anna ']))
    expect(await screen.findByDisplayValue('Anna')).toBeInTheDocument()
  })

  it('sends nothing when the merge is cancelled', async () => {
    const sent = await openGroup(peopleFixture[0])

    await saveName('Anna')
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(sent).toEqual([])
  })

  it('saves a brand-new name at once, without asking', async () => {
    const sent = await openGroup({ ...peopleFixture[1], name: 'Bela' })

    await saveName('Bela')

    await waitFor(() => expect(sent).toEqual(['Bela']))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
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

    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'ANNA' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))

    await waitFor(() => expect(sentName).toBe('ANNA'))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
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
    expect(await screen.findByText('10 confirmed · 3 suggested')).toBeInTheDocument()
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
  })})
