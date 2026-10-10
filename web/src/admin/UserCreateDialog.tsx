import {
  Alert,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import { useCreateUser, userFormErrors, type UserFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, FIELD_SX, PRIMARY_BUTTON_SX } from './adminStyles'

const FIELDS = ['username', 'displayName', 'password', 'role']

type Props = { onClose: () => void }

export function UserCreateDialog({ onClose }: Props) {
  const [username, setUsername] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<'Admin' | 'User'>('User')
  const [canRunFolderActions, setCanRunFolderActions] = useState(false)
  const [errors, setErrors] = useState<UserFormErrors>({})
  const create = useCreateUser()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    try {
      await create.mutateAsync({
        username: username.trim(),
        displayName: displayName.trim(),
        password,
        role,
        canRunFolderActions: role === 'Admin' || canRunFolderActions,
      })
      onClose()
    } catch (error) {
      setErrors(userFormErrors(error, FIELDS))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" slotProps={DIALOG_PAPER_SLOT}>
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle sx={{ fontWeight: 600 }}>New user</DialogTitle>
        <DialogContent>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{errors.form}</Alert>
          )}
          <TextField label="Username" value={username} onChange={(e) => setUsername(e.target.value)} autoFocus fullWidth margin="dense" error={errors.username !== undefined} helperText={errors.username} sx={FIELD_SX} />
          <TextField label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} fullWidth margin="dense" error={errors.displayName !== undefined} helperText={errors.displayName} sx={FIELD_SX} />
          <TextField label="Initial password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} fullWidth margin="dense" autoComplete="new-password" error={errors.password !== undefined} helperText={errors.password ?? 'At least 8 characters. They must change it at first login.'} sx={FIELD_SX} />
          <TextField select label="Role" value={role} onChange={(e) => setRole(e.target.value as 'Admin' | 'User')} fullWidth margin="dense" error={errors.role !== undefined} helperText={errors.role} sx={FIELD_SX}>
            <MenuItem value="User">User</MenuItem>
            <MenuItem value="Admin">Admin</MenuItem>
          </TextField>
          <FormControlLabel
            control={
              <Checkbox
                checked={role === 'Admin' || canRunFolderActions}
                disabled={role === 'Admin'}
                onChange={(e) => setCanRunFolderActions(e.target.checked)}
              />
            }
            label="Can run folder actions (scan, refresh, face recognition)"
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
          <Button
            type="submit"
            variant="contained"
            disableElevation
            disabled={create.isPending || username.trim() === '' || displayName.trim() === '' || password === ''}
            sx={PRIMARY_BUTTON_SX}
          >
            Create
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
