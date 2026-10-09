import { fireEvent, screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { signInAs } from '../test/authHandlers'
import { userMe } from '../test/fixtures'
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

    const list = within(screen.getByRole('complementary', { name: 'People' }))
    expect(list.getByRole('link', { name: /Anna/ })).toBeInTheDocument()
    expect(list.queryByRole('link', { name: /Unknown #1/ })).not.toBeInTheDocument()
  })

  it('filters to people needing review, and lists those with suggestions first', async () => {
    server.use(
      peopleList([
        { id: 1, name: 'Peter', confirmedImageCount: 12, suggestedImageCount: 0, coverFaceId: null },
        { id: 2, name: 'Anna', confirmedImageCount: 3, suggestedImageCount: 4, coverFaceId: null },
      ]),
    )
    const { user } = renderApp('/people')
    const list = within(await screen.findByRole('complementary', { name: 'People' }))
    await list.findByRole('link', { name: /Peter/ })

    const links = list.getAllByRole('link').map((link) => link.getAttribute('aria-label'))
    expect(links[0]).toMatch(/^Anna/)

    await user.click(list.getByRole('button', { name: /Needs review/ }))

    expect(list.getByRole('link', { name: /Anna/ })).toBeInTheDocument()
    expect(list.queryByRole('link', { name: /Peter/ })).not.toBeInTheDocument()
  })

  it('shows a review dashboard when nobody is selected', async () => {
    server.use(peopleList())
    renderApp('/people')

    const next = await screen.findByRole('link', { name: /Review next: Unknown #1 \(5\)/ })
    expect(next).toHaveAttribute('href', '/people/2')
    expect(screen.getByText('Suggestions waiting')).toBeInTheDocument()
    expect(screen.getByText('Biggest unknown groups')).toBeInTheDocument()
  })

  it('explains what to do when there are no people yet', async () => {
    server.use(peopleList([]))
    renderApp('/people')

    expect(await screen.findByText(/No faces recognized yet/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Recognize faces in all libraries' })).toBeInTheDocument()
  })

  it('hides Recognize faces from a user without folder actions, and shows it with them', async () => {
    server.use(peopleList([]))
    signInAs(userMe)
    const first = renderApp('/people')
    expect(await screen.findByText(/No faces recognized yet/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Recognize faces in all libraries' })).not.toBeInTheDocument()
    first.unmount()

    signInAs({ ...userMe, canRunFolderActions: true })
    renderApp('/people')
    expect(await screen.findByRole('button', { name: 'Recognize faces in all libraries' })).toBeInTheDocument()
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
