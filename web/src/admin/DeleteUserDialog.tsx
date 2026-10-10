import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle,
  FormControl, FormControlLabel, Radio, RadioGroup,
} from '@mui/material'
import { useState } from 'react'
import type { UserSummary } from '../api/types'
import { useDeleteUser, userFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, PRIMARY_BUTTON_SX } from './adminStyles'

type Props = { user: UserSummary; onClose: () => void }

export function DeleteUserDialog({ user, onClose }: Props) {
  const [albums, setAlbums] = useState<'transfer' | 'delete'>('transfer')
  const [error, setError] = useState<string | null>(null)
  const remove = useDeleteUser()

  const confirm = async () => {
    setError(null)
    try {
      await remove.mutateAsync({ id: user.id, albums })
      onClose()
    } catch (e) {
      setError(userFormErrors(e, []).form ?? "Couldn't delete the user.")
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-label="Delete user" slotProps={DIALOG_PAPER_SLOT}>
      <DialogTitle sx={{ fontWeight: 600 }}>Delete {user.username}?</DialogTitle>
      <DialogContent>
        {error !== null && <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{error}</Alert>}
        <DialogContentText sx={{ mb: user.albumCount > 0 ? 1 : 0 }}>
          This permanently removes the account. Disable it instead if you may need it again.
          {user.albumCount > 0 && ` ${user.displayName} owns ${user.albumCount} ${user.albumCount === 1 ? 'album' : 'albums'}.`}
        </DialogContentText>
        {user.albumCount > 0 && (
          <FormControl>
            <RadioGroup value={albums} onChange={(e) => setAlbums(e.target.value as 'transfer' | 'delete')}>
              <FormControlLabel value="transfer" control={<Radio />} label="Transfer their albums to me" />
              <FormControlLabel value="delete" control={<Radio />} label="Delete their albums" />
            </RadioGroup>
          </FormControl>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
        <Button color="error" variant="contained" disableElevation disabled={remove.isPending} onClick={() => void confirm()} sx={PRIMARY_BUTTON_SX}>
          Delete user
        </Button>
      </DialogActions>
    </Dialog>
  )
}
