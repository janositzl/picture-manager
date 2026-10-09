import { Box, CircularProgress } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { Outlet } from 'react-router'
import { meQueryKey, useCurrentUser } from '../api/auth'
import { setUnauthorizedHandler } from '../api/client'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { ChangePasswordDialog } from './ChangePasswordDialog'
import { LoginPage } from './LoginPage'

/** Every route sits behind this: no session shows the login page, a forced change shows only that dialog. */
export function AuthGate() {
  const me = useCurrentUser()
  const queryClient = useQueryClient()

  useEffect(() => {
    setUnauthorizedHandler(() => queryClient.setQueryData(meQueryKey, null))
    return () => setUnauthorizedHandler(null)
  }, [queryClient])

  if (me.isPending)
    return (
      <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
        <CircularProgress aria-label="Loading" />
      </Box>
    )
  if (me.isError)
    return (
      <QueryErrorAlert message="Couldn't reach the server." onRetry={() => void me.refetch()} />
    )
  if (me.data === null) return <LoginPage />
  if (me.data.mustChangePassword) return <ChangePasswordDialog forced />
  return <Outlet />
}
