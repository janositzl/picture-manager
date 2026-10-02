import { fireEvent, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { peopleFixture, person } from '../test/peopleHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('PersonView', () => {
  it('names an unnamed group and follows a merge to the surviving person', async () => {
    let sentName: unknown = null
    server.use(
      person(peopleFixture[1]),
      person(peopleFixture[0]),
      http.patch('/api/people/2', async ({ request }) => {
        sentName = ((await request.json()) as { name: string }).name
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    renderApp('/people/2')

    fireEvent.change(await screen.findByLabelText('Name this person'), { target: { value: 'Anna' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))

    await waitFor(() => expect(sentName).toBe('Anna'))
    expect(await screen.findByDisplayValue('Anna')).toBeInTheDocument()
  })

  it('shows the person photo grid filtered by person', async () => {
    let requested: string | null = null
    server.use(
      person(peopleFixture[0]),
      http.get('/api/images', ({ request }) => {
        requested = new URL(request.url).searchParams.get('personId')
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    renderApp('/people/1')

    await waitFor(() => expect(requested).toBe('1'))
    expect(await screen.findByText('12 faces · 10 photos')).toBeInTheDocument()
  })
})
