import CloseIcon from '@mui/icons-material/Close'
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  MenuItem,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import { useId, useState } from 'react'
import {
  useAlbumShares,
  useRemoveAlbumShare,
  useSetAlbumShare,
  useUserDirectory,
} from '../api/albumShares'
import { ApiError } from '../api/client'
import type { AlbumDetail, DirectoryUser, SharePermission } from '../api/types'
import { ACCENT } from '../design/accent'
import { UserAvatar } from '../shared/UserAvatar'
import { PERMISSION_LABEL } from './access'

const FIELD_SX = { '& .MuiOutlinedInput-root': { borderRadius: '10px' } } as const
const PERMISSIONS: SharePermission[] = ['Viewer', 'Editor']

type Props = { album: AlbumDetail; onClose: () => void }

function shareError(error: unknown): string {
  if (error instanceof ApiError) {
    const first = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (first) return first
    if (error.problem?.detail) return error.problem.detail
  }
  return "Couldn't update sharing."
}

export function ShareDialog({ album, onClose }: Props) {
  const shares = useAlbumShares(album.id)
  const directory = useUserDirectory()
  const setShare = useSetAlbumShare(album.id)
  const removeShare = useRemoveAlbumShare(album.id)
  const [person, setPerson] = useState<DirectoryUser | null>(null)
  const [permission, setPermission] = useState<SharePermission>('Viewer')
  const [error, setError] = useState<string | null>(null)
  const listId = useId()

  const sharedIds = new Set((shares.data ?? []).map((share) => share.userId))
  const candidates = (directory.data ?? []).filter((user) => !sharedIds.has(user.id))

  const run = async (action: () => Promise<unknown>) => {
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(shareError(e))
    }
  }

  const share = () =>
    run(async () => {
      if (person === null) return
      await setShare.mutateAsync({ userId: person.id, permission })
      setPerson(null)
    })

  return (
    <Dialog
      open
      onClose={onClose}
      fullWidth
      maxWidth="sm"
      aria-label={`Share ${album.name}`}
      slotProps={{ paper: { className: 'rounded-2xl' } }}
    >
      <DialogTitle sx={{ fontWeight: 600 }}>Share {album.name}</DialogTitle>
      <DialogContent>
        {error !== null && (
          <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>
            {error}
          </Alert>
        )}
        <Box
          component="form"
          onSubmit={(event) => {
            event.preventDefault()
            void share()
          }}
          sx={{
            display: 'flex',
            gap: 1,
            alignItems: 'flex-start',
            flexWrap: { xs: 'wrap', sm: 'nowrap' },
            pt: 1,
          }}
        >
          <Autocomplete
            options={candidates}
            value={person}
            onChange={(_event, value) => setPerson(value)}
            getOptionLabel={(option) => option.displayName}
            isOptionEqualToValue={(option, value) => option.id === value.id}
            loading={directory.isPending}
            noOptionsText="Everyone already has access."
            renderOption={({ key, ...props }, option) => (
              <li key={key} {...props}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
                  <UserAvatar displayName={option.displayName} size={28} />
                  {option.displayName}
                </Box>
              </li>
            )}
            renderInput={(params) => (
              <TextField {...params} label="Add people" size="small" sx={FIELD_SX} />
            )}
            sx={{ flex: 1, minWidth: 200 }}
          />
          <TextField
            select
            size="small"
            label="Access"
            value={permission}
            onChange={(event) => setPermission(event.target.value as SharePermission)}
            sx={{ ...FIELD_SX, width: 140 }}
          >
            {PERMISSIONS.map((p) => (
              <MenuItem key={p} value={p}>
                {PERMISSION_LABEL[p]}
              </MenuItem>
            ))}
          </TextField>
          <Button
            type="submit"
            variant="contained"
            disableElevation
            disabled={person === null || setShare.isPending}
            sx={{
              borderRadius: '10px',
              textTransform: 'none',
              fontWeight: 600,
              bgcolor: ACCENT,
              height: 40,
              '&:hover': { bgcolor: '#4b4bc4' },
            }}
          >
            Share
          </Button>
        </Box>

        <Typography
          id={listId}
          variant="subtitle2"
          component="h3"
          sx={{ mt: 3, mb: 0.5, fontWeight: 600 }}
        >
          People with access
        </Typography>
        <List aria-labelledby={listId} disablePadding>
          <ListItem disableGutters sx={{ gap: 1.5, py: 1 }}>
            <UserAvatar displayName={album.ownerDisplayName} size={32} />
            <Typography variant="body2" sx={{ flex: 1, fontWeight: 600 }}>
              {album.ownerDisplayName}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ pr: 1 }}>
              Owner
            </Typography>
          </ListItem>
          {(shares.data ?? []).map((s) => (
            <ListItem key={s.userId} disableGutters sx={{ gap: 1.5, py: 1 }}>
              <UserAvatar displayName={s.displayName} size={32} />
              <Typography variant="body2" sx={{ flex: 1, fontWeight: 600 }} noWrap>
                {s.displayName}
              </Typography>
              <TextField
                select
                size="small"
                value={s.permission}
                onChange={(event) =>
                  void run(() =>
                    setShare.mutateAsync({
                      userId: s.userId,
                      permission: event.target.value as SharePermission,
                    }),
                  )
                }
                slotProps={{ select: { SelectDisplayProps: { 'aria-label': `Access for ${s.displayName}` } } }}
                sx={{ ...FIELD_SX, width: 130 }}
              >
                {PERMISSIONS.map((p) => (
                  <MenuItem key={p} value={p}>
                    {PERMISSION_LABEL[p]}
                  </MenuItem>
                ))}
              </TextField>
              <Tooltip title={`Remove ${s.displayName}`}>
                <IconButton
                  size="small"
                  aria-label={`Remove ${s.displayName}`}
                  onClick={() => void run(() => removeShare.mutateAsync(s.userId))}
                  sx={{
                    color: 'text.secondary',
                    transition: 'all 200ms ease-in-out',
                    '&:hover': { color: 'error.main' },
                  }}
                >
                  <CloseIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </ListItem>
          ))}
        </List>
        {shares.isSuccess && shares.data.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            Only you can see this album.
          </Typography>
        )}
        {shares.isError && (
          <Alert severity="error" sx={{ mt: 1, borderRadius: '10px' }}>
            Couldn't load who has access.
          </Alert>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5, gap: 2, alignItems: 'flex-end' }}>
        <Typography variant="caption" color="text.secondary" sx={{ flex: 1, maxWidth: '60ch' }}>
          They find it under Shared with me. People who can edit add, remove and reorder photos. Only
          you can rename, delete or share it.
        </Typography>
        <Button onClick={onClose} sx={{ borderRadius: '10px', textTransform: 'none', fontWeight: 600 }}>
          Done
        </Button>
      </DialogActions>
    </Dialog>
  )
}
