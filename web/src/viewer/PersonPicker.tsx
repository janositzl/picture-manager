import {
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItemButton,
  ListItemText,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { faceThumbnailUrl, usePeople, type AssignTarget } from '../api/people'

type Props = {
  title: string
  /** People who cannot be picked, e.g. the person the face is already confirmed as. */
  excludeId?: number | null
  busy?: boolean
  onPick: (target: AssignTarget) => void
  onClose: () => void
}

/** Pick an existing named person, or type a new name to create one. */
export function PersonPicker({ title, excludeId = null, busy = false, onPick, onClose }: Props) {
  const people = usePeople()
  const [filter, setFilter] = useState('')
  const typed = filter.trim()
  const needle = typed.toLowerCase()
  const named = (people.data ?? []).filter((p) => p.name !== null && p.id !== excludeId)
  const visible = named.filter((p) => p.name!.toLowerCase().includes(needle))
  const exact = (people.data ?? []).some((p) => p.name?.toLowerCase() === needle)

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" slotProps={{ paper: { className: 'rounded-2xl' } }}>
      <DialogTitle sx={{ fontWeight: 600 }}>{title}</DialogTitle>
      <DialogContent>
        <TextField
          label="Person name"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          autoFocus
          fullWidth
          size="small"
          margin="dense"
        />
        {people.isPending ? (
          <CircularProgress size={24} sx={{ my: 2 }} />
        ) : (
          <List dense aria-label="People" sx={{ maxHeight: 320, overflowY: 'auto' }}>
            {visible.map((person) => (
              <ListItemButton
                key={person.id}
                disabled={busy}
                onClick={() => onPick({ personId: person.id })}
                sx={{ borderRadius: '8px', mb: 0.25, gap: 1.5 }}
              >
                {person.coverFaceId !== null && (
                  <img src={faceThumbnailUrl(person.coverFaceId)} alt="" className="h-8 w-8 rounded-full object-cover" />
                )}
                <ListItemText primary={person.name} />
              </ListItemButton>
            ))}
            {visible.length === 0 && typed === '' && (
              <Typography color="text.secondary" sx={{ my: 2 }}>
                No named people yet. Type a name to add one.
              </Typography>
            )}
          </List>
        )}
        {typed !== '' && !exact && (
          <Box sx={{ mt: 1 }}>
            <Button variant="outlined" disabled={busy} onClick={() => onPick({ name: typed })}>
              {`Create '${typed}'`}
            </Button>
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
      </DialogActions>
    </Dialog>
  )
}
