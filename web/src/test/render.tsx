import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router'
import { NotifyProvider } from '../app/notify'
import { appRoutes } from '../app/routes'
import { FolderJobsProvider } from '../tree/FolderJobsContext'

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  })
}

export function createWrapper(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <NotifyProvider>
          <FolderJobsProvider>{children}</FolderJobsProvider>
        </NotifyProvider>
      </QueryClientProvider>
    )
  }
}

/** Renders `routes` in a memory router at `path`, with fresh providers. */
export function renderRoutes(routes: RouteObject[], path: string) {
  const queryClient = createTestQueryClient()
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const Wrapper = createWrapper(queryClient)
  const user = userEvent.setup()
  const view = render(
    <Wrapper>
      <RouterProvider router={router} />
    </Wrapper>,
  )
  return { ...view, user, router, queryClient }
}

/** The whole app (shell + routes) at `path`. */
export function renderApp(path: string) {
  return renderRoutes(appRoutes, path)
}
