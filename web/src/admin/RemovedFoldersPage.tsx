import {
  Box,
  Button,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
} from '@mui/material'
import { restoreFolderErrorMessage, useRestoreFolder } from '../api/removedFolders'
import { useRemovedFolders } from '../api/queries'
import { useNotify } from '../app/notify'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'

export function RemovedFoldersPage() {
  const removed = useRemovedFolders()
  const restore = useRestoreFolder()
  const notify = useNotify()

  if (removed.isPending) return null
  if (removed.isError) {
    return <QueryErrorAlert message="Couldn't load removed folders." onRetry={() => void removed.refetch()} />
  }
  if (removed.data.length === 0) {
    return <EmptyMessage>No removed folders.</EmptyMessage>
  }

  return (
    <Box sx={{ p: 3 }}>
      <TableContainer>
        <Table size="small" aria-label="Removed folders">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Root</TableCell>
              <TableCell>Relative path</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {removed.data.map((folder) => (
              <TableRow key={folder.id}>
                <TableCell>{folder.name}</TableCell>
                <TableCell>{folder.rootName}</TableCell>
                <TableCell>{folder.relativePath}</TableCell>
                <TableCell align="right">
                  <Button
                    size="small"
                    disabled={restore.isPending}
                    onClick={() =>
                      restore.mutate(folder.id, {
                        onError: (error) => notify(restoreFolderErrorMessage(error)),
                      })
                    }
                    sx={{ textTransform: 'none' }}
                  >
                    Restore
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  )
}
