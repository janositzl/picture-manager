import { Box, CircularProgress, Typography } from '@mui/material'
import { Navigate } from 'react-router'
import { useRootFolders } from '../api/queries'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'

/** "/" opens the first root's top folder. */
export function RootRedirect() {
  const roots = useRootFolders()

  if (roots.isPending) {
    return (
      <Box sx={{ p: 3 }}>
        <CircularProgress size={24} />
      </Box>
    )
  }

  if (roots.isError) {
    return <QueryErrorAlert message="Couldn't load folders." onRetry={() => void roots.refetch()} />
  }

  const first = roots.data[0]
  if (first === undefined) {
    return <Typography sx={{ p: 3 }}>No image roots are configured.</Typography>
  }

  return <Navigate to={`/folders/${first.id}`} replace />
}
