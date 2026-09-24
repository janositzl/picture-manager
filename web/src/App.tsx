import { CssBaseline, ThemeProvider, createTheme } from '@mui/material'
import { QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'
import { NotifyProvider } from './app/notify'
import { createQueryClient } from './app/queryClient'
import { appRoutes } from './app/routes'

// Follows the OS light/dark preference; there is no manual toggle.
const theme = createTheme({ colorSchemes: { light: true, dark: true } })
const queryClient = createQueryClient()
const router = createBrowserRouter(appRoutes)

function App() {
  return (
    <ThemeProvider theme={theme}>
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
