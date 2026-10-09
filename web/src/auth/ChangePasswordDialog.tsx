import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
} from '@mui/material'
import { useActionState } from 'react'
import { ApiError } from '../api/client'
import { useChangePassword, useLogout } from '../api/auth'

type Props = {
  /** Forced after an admin-set password: no Cancel, and Log out is offered instead. */
  forced?: boolean
  onClose?: () => void
}

function changeErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't change the password."
}

export function ChangePasswordDialog({ forced = false, onClose }: Props) {
  const change = useChangePassword()
  const logout = useLogout()
  const [error, submit, pending] = useActionState(
    async (_previous: string | null, form: FormData) => {
      const newPassword = String(form.get('newPassword') ?? '')
      if (newPassword !== String(form.get('repeatPassword') ?? ''))
        return "The new passwords don't match."
      try {
        await change.mutateAsync({
          currentPassword: String(form.get('currentPassword') ?? ''),
          newPassword,
        })
        onClose?.()
        return null
      } catch (caught) {
        return changeErrorMessage(caught)
      }
    },
    null,
  )

  return (
    <Dialog
      open
      onClose={forced ? undefined : onClose}
      aria-labelledby="change-password-title"
      fullWidth
      maxWidth="xs"
    >
      <form action={submit}>
        <DialogTitle id="change-password-title">
          {forced ? 'Choose a new password' : 'Change password'}
        </DialogTitle>
        <DialogContent
          sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}
        >
          {forced && (
            <Alert severity="info">
              Your password was set by an administrator. Choose your own to continue.
            </Alert>
          )}
          {error !== null && <Alert severity="error">{error}</Alert>}
          <TextField
            name="currentPassword"
            label="Current password"
            type="password"
            autoComplete="current-password"
            slotProps={{ htmlInput: { required: true } }}
            size="small"
          />
          <TextField
            name="newPassword"
            label="New password"
            type="password"
            autoComplete="new-password"
            slotProps={{ htmlInput: { required: true } }}
            size="small"
            helperText="At least 8 characters."
          />
          <TextField
            name="repeatPassword"
            label="Repeat new password"
            type="password"
            autoComplete="new-password"
            slotProps={{ htmlInput: { required: true } }}
            size="small"
          />
        </DialogContent>
        <DialogActions>
          {forced ? (
            <Button onClick={() => logout.mutate()}>Log out</Button>
          ) : (
            <Button onClick={onClose}>Cancel</Button>
          )}
          <Button type="submit" variant="contained" disableElevation disabled={pending}>
            Change password
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
