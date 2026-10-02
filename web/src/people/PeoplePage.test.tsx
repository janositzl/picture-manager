import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { peopleList } from '../test/peopleHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('PeoplePage', () => {
  it('lists named people and unnamed groups in separate sections', async () => {
    server.use(peopleList())
    renderApp('/people')

    expect(await screen.findByRole('heading', { name: 'Named people' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Unnamed groups' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Anna.*10 photos/ })).toHaveAttribute('href', '/people/1')
    expect(screen.getByRole('link', { name: /Unnamed.*5 photos/ })).toHaveAttribute('href', '/people/2')
    // alt="" makes the cover decorative, so it has no img role
    expect(document.querySelector('img')).toHaveAttribute('src', '/api/faces/101/thumbnail')
  })

  it('explains what to do when there are no people yet', async () => {
    server.use(peopleList([]))
    renderApp('/people')

    expect(await screen.findByText(/No faces recognized yet/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Recognize faces in all libraries' })).toBeInTheDocument()
  })
})
