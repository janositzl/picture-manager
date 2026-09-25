import { CssBaseline, ThemeProvider } from '@mui/material'
import { QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'
import { NotifyProvider } from './app/notify'
import { createQueryClient } from './app/queryClient'
import { appRoutes } from './app/routes'
import { theme } from './theme'

const queryClient = createQueryClient()
const router = createBrowserRouter(appRoutes)

function App() {
  // Follows the OS light/dark preference; there is no manual toggle.
  return (
    <ThemeProvider theme={theme} defaultMode="system">
      <CssBaseline />
      <QueryClientProvider client={queryClient}>
        <NotifyProvider>
          <RouterProvider router={router} />
        </NotifyProvider>
      </QueryClientProvider>
    </ThemeProvider>
  )
}

export default App
