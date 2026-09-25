import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { apiFetchText } from '../api/client'
import { queryKeys } from '../api/queries'
import type { AlbumDetail } from '../api/types'
import { useNotify } from '../app/notify'
import { useDebouncedValue } from '../shared/useDebouncedValue'
import { downloadText, exportFileName } from './exportFile'
import { readExportPrefix, writeExportPrefix } from './preferences'

const PREVIEW_LINES = 5

type Props = { album: AlbumDetail; missingCount: number; onClose: () => void }

export function ExportDialog({ album, missingCount, onClose }: Props) {
  const notify = useNotify()
  const [prefix, setPrefix] = useState(readExportPrefix)
  const debounced = useDebouncedValue(prefix, 300)
  const exported = useQuery({
    queryKey: queryKeys.albumExport(album.id, debounced),
    queryFn: ({ signal }) => {
      const params = new URLSearchParams()
      if (debounced !== '') params.set('prefix', debounced)
      return apiFetchText(`/api/albums/${album.id}/export?${params}`, { signal })
    },
  })
  // Download/Copy only once the text matches what's typed in the prefix box.
  const ready = exported.data !== undefined && prefix === debounced && !exported.isFetching
  const lines = (exported.data ?? '').split('\n').filter((line) => line !== '')

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(exported.data ?? '')
      notify('Copied to clipboard.')
    } catch {
      notify("Couldn't copy.")
    }
  }

  let preview: ReactNode
  if (exported.isError) {
    preview = <Alert severity="error">Couldn't load the export.</Alert>
  } else if (exported.data === undefined) {
    preview = <CircularProgress size={24} />
  } else {
    preview = (
      <>
        <Typography variant="body2" color="text.secondary">
          {lines.length === 1 ? '1 line' : `${lines.length} lines`}
        </Typography>
        <Box
          component="pre"
          aria-label="Export preview"
          sx={{ m: 0, p: 1, overflowX: 'auto', fontSize: 12, bgcolor: 'action.hover' }}
        >
          {lines.slice(0, PREVIEW_LINES).join('\n')}
          {lines.length > PREVIEW_LINES ? '\n…' : ''}
        </Box>
      </>
    )
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Export album</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <TextField
          label="Path prefix"
          placeholder="/mnt/frame"
          value={prefix}
          onChange={(event) => {
            setPrefix(event.target.value)
            writeExportPrefix(event.target.value)
          }}
          fullWidth
          size="small"
          margin="dense"
        />
        {missingCount > 0 && (
          <Alert severity="warning">
            {missingCount === 1 ? '1 photo is' : `${missingCount} photos are`} missing on disk.
          </Alert>
        )}
        {preview}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
        <Button disabled={!ready} onClick={() => void copy()}>
          Copy
        </Button>
        <Button
          variant="contained"
          disabled={!ready}
          onClick={() => downloadText(exportFileName(album.name), exported.data ?? '')}
        >
          Download
        </Button>
      </DialogActions>
    </Dialog>
  )
}
