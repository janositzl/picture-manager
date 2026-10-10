import {
  Alert, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, MenuItem, Switch, TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import type { UserSummary } from '../api/types'
import { useUpdateUser, userFormErrors, type UserFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, FIELD_SX, PRIMARY_BUTTON_SX } from './adminStyles'

type Props = { user: UserSummary; isSelf: boolean; onClose: () => void }

export function UserEditDialog({ user, isSelf, onClose }: Props) {
  const [displayName, setDisplayName] = useState(user.displayName)
  const [role, setRole] = useState(user.role)
  const [isActive, setIsActive] = useState(user.isActive)
  const [canRunFolderActions, setCanRunFolderActions] = useState(user.canRunFolderActions)
  const [errors, setErrors] = useState<UserFormErrors>({})
  const update = useUpdateUser(user.id)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    try {
      await update.mutateAsync({
        displayName: displayName.trim(),
        role,
        isActive,
        canRunFolderActions: role === 'Admin' || canRunFolderActions,
      })
      onClose()
    } catch (error) {
      setErrors(userFormErrors(error, ['displayName', 'role']))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-label={`Edit ${user.username}`} slotProps={DIALOG_PAPER_SLOT}>
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle sx={{ fontWeight: 600 }}>Edit {user.username}</DialogTitle>
        <DialogContent>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{errors.form}</Alert>
          )}
          <TextField label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} autoFocus fullWidth margin="dense" error={errors.displayName !== undefined} helperText={errors.displayName} sx={FIELD_SX} />
          <TextField select label="Role" value={role} onChange={(e) => setRole(e.target.value as 'Admin' | 'User')} disabled={isSelf} fullWidth margin="dense" error={errors.role !== undefined} helperText={errors.role ?? (isSelf ? "You can't demote yourself." : undefined)} sx={FIELD_SX}>
            <MenuItem value="User">User</MenuItem>
            <MenuItem value="Admin">Admin</MenuItem>
          </TextField>
          <FormControlLabel
            control={<Switch checked={isActive} disabled={isSelf} onChange={(e) => setIsActive(e.target.checked)} slotProps={{ input: { 'aria-label': 'Active' } }} />}
            label="Active"
          />
          <FormControlLabel
            control={<Checkbox checked={role === 'Admin' || canRunFolderActions} disabled={role === 'Admin'} onChange={(e) => setCanRunFolderActions(e.target.checked)} />}
            label="Can run folder actions (scan, refresh, face recognition)"
            sx={{ display: 'flex' }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
          <Button type="submit" variant="contained" disableElevation disabled={update.isPending || displayName.trim() === ''} sx={PRIMARY_BUTTON_SX}>
            Save
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
