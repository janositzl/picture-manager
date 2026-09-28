import { Box, CircularProgress, Typography } from '@mui/material'
import { Navigate } from 'react-router'
import { useRootFolders } from '../api/queries'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { readLastFolderId } from './preferences'

/** "/" reopens the last folder that was browsed, or the first root if there's none remembered yet. */
export function RootRedirect() {
  const roots = useRootFolders()

  if (roots.isPending) {
    return (
      <Box className="p-8">
        <CircularProgress size={24} />
      </Box>
    )
  }

  if (roots.isError) {
    return <QueryErrorAlert message="Couldn't load folders." onRetry={() => void roots.refetch()} />
  }

  const first = roots.data[0]
  if (first === undefined) {
    return (
      <Typography className="p-8" color="text.secondary">
        No image roots are configured.
      </Typography>
    )
  }

  const targetId = readLastFolderId() ?? first.id
  return <Navigate to={`/folders/${targetId}`} replace />
}
