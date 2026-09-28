import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import { rootCreateFormErrors, useCreateRoot, type RootCreateFormErrors } from '../api/roots'
import type { RootSummary } from '../api/types'

type Props = {
  onClose: () => void
  onCreated: (root: RootSummary) => void
}

export function RootFormDialog({ onClose, onCreated }: Props) {
  const [name, setName] = useState('')
  const [mountPath, setMountPath] = useState('')
  const [alias, setAlias] = useState('')
  const [errors, setErrors] = useState<RootCreateFormErrors>({})
  const create = useCreateRoot()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    try {
      const root = await create.mutateAsync({
        name: name.trim(),
        mountPath: mountPath.trim(),
        alias: alias.trim() === '' ? null : alias.trim(),
      })
      onCreated(root)
    } catch (error) {
      setErrors(rootCreateFormErrors(error))
    }
  }

  return (
    <Dialog
      open
      onClose={onClose}
      fullWidth
      maxWidth="xs"
      slotProps={{ paper: { className: 'rounded-2xl' } }}
    >
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle sx={{ fontWeight: 600 }}>New root</DialogTitle>
        <DialogContent>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>
              {errors.form}
            </Alert>
          )}
          <TextField
            label="Name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            autoFocus
            fullWidth
            margin="dense"
            error={errors.name !== undefined}
            helperText={errors.name}
            sx={{ '& .MuiOutlinedInput-root': { borderRadius: '8px' } }}
          />
          <TextField
            label="Mount path"
            value={mountPath}
            onChange={(event) => setMountPath(event.target.value)}
            placeholder="e.g. /images/photos"
            fullWidth
            margin="dense"
            error={errors.mountPath !== undefined}
            helperText={errors.mountPath}
            sx={{ '& .MuiOutlinedInput-root': { borderRadius: '8px' } }}
          />
          <TextField
            label="Alias (optional)"
            value={alias}
            onChange={(event) => setAlias(event.target.value)}
            fullWidth
            margin="dense"
            error={errors.alias !== undefined}
            helperText={errors.alias}
            sx={{ '& .MuiOutlinedInput-root': { borderRadius: '8px' } }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} sx={{ borderRadius: '8px', textTransform: 'none' }}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="contained"
            disableElevation
            disabled={create.isPending || name.trim() === '' || mountPath.trim() === ''}
            sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
          >
            Create
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
