import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '../test/render'
import { server } from '../test/server'
import { SystemPage } from './SystemPage'

function renderPage() {
  return renderRoutes([{ path: '/admin/system', element: <SystemPage /> }], '/admin/system')
}

describe('SystemPage', () => {
  it('shows the deployed version and runtime details', async () => {
    renderPage()
    expect(await screen.findByText('1.2.3')).toBeInTheDocument()
    expect(screen.getByText('Production')).toBeInTheDocument()
    expect(screen.getByText('.NET 10.0.0')).toBeInTheDocument()
  })

  it('marks errors and warnings in the log, and expands the exception of an error', async () => {
    renderPage()
    const entries = await screen.findAllByTestId('log-entry')
    expect(entries.map((e) => e.getAttribute('data-level'))).toEqual(['Information', 'Warning', 'Error'])
    expect(within(entries[2]!).getByText(/System.IO.IOException: disk gone/)).toBeVisible()
  })

  it('filters to warnings and errors when the switch is on', async () => {
    const { user } = renderPage()
    await screen.findAllByTestId('log-entry')
    await user.click(screen.getByRole('switch', { name: 'Warnings & errors only' }))
    await waitFor(() => expect(screen.getAllByTestId('log-entry')).toHaveLength(2))
  })

  it('says so when there is no log file', async () => {
    server.use(
      http.get('/api/system/logs', () =>
        HttpResponse.json({ fileName: null, entries: [], truncated: false }),
      ),
    )
    renderPage()
    expect(await screen.findByText('No log file found on the server.')).toBeInTheDocument()
  })
})
