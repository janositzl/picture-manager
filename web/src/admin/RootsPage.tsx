import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined'
import {
  Box,
  Button,
  IconButton,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { useState, type KeyboardEvent } from 'react'
import { rootUpdateErrorMessage, useDeleteRoot, useUpdateRoot } from '../api/roots'
import { useRoots } from '../api/queries'
import { useNotify } from '../app/notify'
import { ConfirmDialog } from '../shared/ConfirmDialog'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import type { RootSummary } from '../api/types'
import { RootFormDialog } from './RootFormDialog'

function RootRow({ root }: { root: RootSummary }) {
  const update = useUpdateRoot(root.id)
  const deleteRoot = useDeleteRoot()
  const notify = useNotify()
  const [name, setName] = useState(root.name)
  const [alias, setAlias] = useState(root.alias ?? '')
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const confirmDelete = async () => {
    try {
      await deleteRoot.mutateAsync(root.id)
    } catch {
      notify(`Couldn't delete ${root.name}.`)
    }
    setConfirmingDelete(false)
  }

  const saveName = () => {
    const trimmed = name.trim()
    if (trimmed === root.name || trimmed === '') {
      setName(root.name)
      return
    }
    update.mutate(
      { name: trimmed },
      { onError: (error) => { notify(rootUpdateErrorMessage(error)); setName(root.name) } },
    )
  }

  const saveAlias = () => {
    const trimmed = alias.trim()
    const next = trimmed === '' ? null : trimmed
    if (next === (root.alias ?? null)) return
    update.mutate(
      { alias: next },
      { onError: (error) => { notify(rootUpdateErrorMessage(error)); setAlias(root.alias ?? '') } },
    )
  }

  const onKeyDown = (revert: () => void) => (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') event.currentTarget.blur()
    else if (event.key === 'Escape') revert()
  }

  const toggleActive = () => {
    update.mutate(
      { isActive: !root.isActive },
      { onError: (error) => notify(rootUpdateErrorMessage(error)) },
    )
  }

  return (
    <TableRow>
      <TableCell>
        <TextField
          variant="standard"
          value={name}
          onChange={(event) => setName(event.target.value)}
          onBlur={saveName}
          onKeyDown={onKeyDown(() => setName(root.name))}
          disabled={update.isPending}
          fullWidth
        />
      </TableCell>
      <TableCell>
        <TextField
          variant="standard"
          value={alias}
          onChange={(event) => setAlias(event.target.value)}
          onBlur={saveAlias}
          onKeyDown={onKeyDown(() => setAlias(root.alias ?? ''))}
          disabled={update.isPending}
          placeholder="(none)"
          fullWidth
        />
      </TableCell>
      <TableCell>
        <Typography variant="body2" color="text.secondary" noWrap>
          {root.mountPath}
        </Typography>
      </TableCell>
      <TableCell>
        <Typography variant="body2" color="text.secondary" noWrap>
          {root.exportSegment}
        </Typography>
      </TableCell>
      <TableCell align="center">
        <Switch
          checked={root.isActive}
          onChange={toggleActive}
          disabled={update.isPending}
          slotProps={{ input: { 'aria-label': `${root.name} active` } }}
        />
      </TableCell>
      <TableCell align="right">
        <IconButton
          size="small"
          aria-label={`Delete ${root.name}`}
          onClick={() => setConfirmingDelete(true)}
          disabled={deleteRoot.isPending}
        >
          <DeleteOutlineIcon fontSize="small" />
        </IconButton>
      </TableCell>
      {confirmingDelete && (
        <ConfirmDialog
          title="Delete root"
          message={`Delete root ${root.name}? This permanently removes its folders and every image indexed under it. This cannot be undone.`}
          confirmLabel="Delete"
          busy={deleteRoot.isPending}
          onConfirm={() => void confirmDelete()}
          onClose={() => setConfirmingDelete(false)}
        />
      )}
    </TableRow>
  )
}

export function RootsPage() {
  const roots = useRoots()
  const [creating, setCreating] = useState(false)

  if (roots.isPending) return null
  if (roots.isError) {
    return <QueryErrorAlert message="Couldn't load roots." onRetry={() => void roots.refetch()} />
  }

  return (
    <Box sx={{ p: 3 }}>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 2 }}>
        <Button
          variant="contained"
          size="small"
          disableElevation
          onClick={() => setCreating(true)}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
        >
          New root
        </Button>
      </Box>
      <TableContainer>
        <Table size="small" aria-label="Roots">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Alias</TableCell>
              <TableCell>Mount path</TableCell>
              <TableCell>Export segment</TableCell>
              <TableCell align="center">Active</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {roots.data.map((root) => (
              <RootRow key={root.id} root={root} />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {creating && (
        <RootFormDialog onClose={() => setCreating(false)} onCreated={() => setCreating(false)} />
      )}
    </Box>
  )
}
