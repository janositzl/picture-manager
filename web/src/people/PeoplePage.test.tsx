import { fireEvent, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { peopleList } from '../test/peopleHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('PeoplePage', () => {
  it('lists named people and unknown groups with confirmed (suggested) image counts', async () => {
    server.use(peopleList())
    renderApp('/people')

    expect(await screen.findByRole('heading', { name: 'Named' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Unknown' })).toBeInTheDocument()
    const anna = screen.getByRole('link', { name: 'Anna, 10 confirmed · 3 suggested' })
    expect(anna).toHaveAttribute('href', '/people/1')
    expect(anna).toHaveTextContent('10 (3)')
    const group = screen.getByRole('link', { name: 'Unknown #1, 5 suggested' })
    expect(group).toHaveAttribute('href', '/people/2')
    expect(group).toHaveTextContent('5')
    expect(screen.getByText('Select a person to see their photos.')).toBeInTheDocument()
  })

  it('omits the parentheses when nothing is suggested', async () => {
    server.use(peopleList([{ id: 1, name: 'Peter', confirmedImageCount: 12, suggestedImageCount: 0, coverFaceId: null }]))
    renderApp('/people')

    expect(await screen.findByRole('link', { name: /Peter/ })).toHaveTextContent(/^Peter12$/)
  })

  it('filters by the search text', async () => {
    server.use(peopleList())
    renderApp('/people')
    await screen.findByRole('link', { name: /Anna/ })

    fireEvent.change(screen.getByRole('textbox', { name: 'Search people' }), { target: { value: 'ann' } })

    expect(screen.getByRole('link', { name: /Anna/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Unknown #1/ })).not.toBeInTheDocument()
  })

  it('explains what to do when there are no people yet', async () => {
    server.use(peopleList([]))
    renderApp('/people')

    expect(await screen.findByText(/No faces recognized yet/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Recognize faces in all libraries' })).toBeInTheDocument()
  })

  it('selecting a person shows their photos next to the list', async () => {
    server.use(
      peopleList(),
      http.get('/api/people/1', () => HttpResponse.json({ id: 1, name: 'Anna', confirmedImageCount: 10, suggestedImageCount: 3, coverFaceId: 101 })),
      http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })),
    )
    const { user } = renderApp('/people')

    await user.click(await screen.findByRole('link', { name: /Anna/ }))

    expect(await screen.findByRole('heading', { name: 'Anna' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Anna/ })).toBeInTheDocument()
  })
})
