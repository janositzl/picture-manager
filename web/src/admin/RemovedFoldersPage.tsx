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
import { useState } from 'react'
import {
  deleteRemovedFolderErrorMessage,
  restoreFolderErrorMessage,
  useDeleteRemovedFolder,
  useRestoreFolder,
} from '../api/removedFolders'
import { useRemovedFolders } from '../api/queries'
import { useNotify } from '../app/notify'
import { ConfirmDialog } from '../shared/ConfirmDialog'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import type { RemovedFolder } from '../api/types'

function RemovedFolderRow({ folder }: { folder: RemovedFolder }) {
  const restore = useRestoreFolder()
  const deleteFolder = useDeleteRemovedFolder()
  const notify = useNotify()
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const confirmDelete = async () => {
    try {
      await deleteFolder.mutateAsync(folder.id)
    } catch (error) {
      notify(deleteRemovedFolderErrorMessage(error))
    }
    setConfirmingDelete(false)
  }

  return (
    <TableRow>
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
        <Button
          size="small"
          color="error"
          aria-label={`Delete ${folder.relativePath}`}
          disabled={deleteFolder.isPending}
          onClick={() => setConfirmingDelete(true)}
          sx={{ textTransform: 'none' }}
        >
          Delete
        </Button>
      </TableCell>
      {confirmingDelete && (
        <ConfirmDialog
          title="Delete folder"
          message={`Permanently delete ${folder.relativePath}? This removes it and everything under it, including any indexed images. This cannot be undone.`}
          confirmLabel="Delete"
          busy={deleteFolder.isPending}
          onConfirm={() => void confirmDelete()}
          onClose={() => setConfirmingDelete(false)}
        />
      )}
    </TableRow>
  )
}

export function RemovedFoldersPage() {
  const removed = useRemovedFolders()

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
              <RemovedFolderRow key={folder.id} folder={folder} />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  )
}
