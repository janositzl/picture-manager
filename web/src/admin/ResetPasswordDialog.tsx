import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField } from '@mui/material'
import { useState, type FormEvent } from 'react'
import type { UserSummary } from '../api/types'
import { useResetPassword, userFormErrors, type UserFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, FIELD_SX, PRIMARY_BUTTON_SX } from './adminStyles'

type Props = { user: UserSummary; onClose: () => void }

export function ResetPasswordDialog({ user, onClose }: Props) {
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<UserFormErrors>({})
  const reset = useResetPassword(user.id)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    try {
      await reset.mutateAsync(password)
      onClose()
    } catch (error) {
      setErrors(userFormErrors(error, ['newPassword']))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-label="Reset password" slotProps={DIALOG_PAPER_SLOT}>
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle sx={{ fontWeight: 600 }}>Reset password</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 1 }}>
            Sets a new password for {user.username}, signs them out everywhere and makes them change it at their next login.
          </DialogContentText>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{errors.form}</Alert>
          )}
          <TextField label="New password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus fullWidth margin="dense" autoComplete="new-password" error={errors.newPassword !== undefined} helperText={errors.newPassword ?? 'At least 8 characters.'} sx={FIELD_SX} />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
          <Button type="submit" variant="contained" disableElevation disabled={reset.isPending || password === ''} sx={PRIMARY_BUTTON_SX}>
            Reset
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
