import { Alert, Box, Button, Chip, CircularProgress, Link, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import type { ImageDetail } from '../api/types'
import { formatBytes, formatDateTaken, openStreetMapUrl } from './format'

type Props = {
  detail: ImageDetail | undefined
  isLoading: boolean
  isError: boolean
  onRetry: () => void
  /** The People section, shown above the details whatever their loading state. */
  peopleSlot?: ReactNode
}

export function InfoPanel({ detail, isLoading, isError, onRetry, peopleSlot }: Props) {
  let content: ReactNode
  if (isError) {
    content = (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={onRetry}>
            Retry
          </Button>
        }
      >
        Couldn't load details.
      </Alert>
    )
  } else if (isLoading || detail === undefined) {
    content = <CircularProgress size={20} color="inherit" />
  } else {
    content = <Details detail={detail} />
  }

  return (
    <Box
      component="aside"
      aria-label="Photo info"
      sx={{
        width: 320,
        flexShrink: 0,
        overflowY: 'auto',
        p: 2.5,
        bgcolor: 'grey.900',
        color: 'grey.100',
        borderLeft: '1px solid',
        borderColor: 'rgba(255,255,255,0.08)',
      }}
    >
      {peopleSlot}
      {content}
    </Box>
  )
}

function Details({ detail }: { detail: ImageDetail }) {
  const camera = [detail.cameraMake, detail.cameraModel].filter(Boolean).join(' ')
  const rows: Array<[string, ReactNode]> = [
    ['Date taken', detail.dateTaken === null ? 'Unknown' : formatDateTaken(detail.dateTaken)],
    [
      'Dimensions',
      detail.width !== null && detail.height !== null
        ? `${detail.width} × ${detail.height}`
        : 'Unknown',
    ],
    ['File size', formatBytes(detail.fileSize)],
  ]
  if (camera !== '') rows.push(['Camera', camera])
  if (detail.lensModel) rows.push(['Lens', detail.lensModel])
  if (detail.latitude !== null && detail.longitude !== null) {
    rows.push([
      'Location',
      <Link
        href={openStreetMapUrl(detail.latitude, detail.longitude)}
        target="_blank"
        rel="noreferrer"
        color="inherit"
      >
        {`${detail.latitude.toFixed(5)}, ${detail.longitude.toFixed(5)} (OpenStreetMap)`}
      </Link>,
    ])
  }

  return (
    <Stack spacing={2}>
      <div>
        <Typography variant="subtitle1" sx={{ wordBreak: 'break-all', fontWeight: 600 }}>
          {detail.fileName}
          {detail.extension}
        </Typography>
        <Typography variant="body2" sx={{ color: 'grey.400', wordBreak: 'break-all' }}>
          {detail.folderPath}
        </Typography>
        <Link
          component={RouterLink}
          to={`/folders/${detail.folderId}`}
          color="inherit"
          variant="body2"
        >
          Go to folder
        </Link>
      </div>
      <Box component="dl" sx={{ m: 0 }}>
        {rows.map(([label, value]) => (
          <Box key={label} sx={{ mb: 1.25 }}>
            <Typography
              component="dt"
              variant="caption"
              sx={{ color: 'grey.400', textTransform: 'uppercase', letterSpacing: '0.04em' }}
            >
              {label}
            </Typography>
            <Typography component="dd" variant="body2" sx={{ m: 0 }}>
              {value}
            </Typography>
          </Box>
        ))}
      </Box>
      <div>
        <Typography variant="caption" sx={{ color: 'grey.400' }}>
          Albums
        </Typography>
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mt: 0.5 }}>
          {detail.albums.length === 0 ? (
            <Typography variant="body2">Not in any album</Typography>
          ) : (
            detail.albums.map((album) => (
              <Chip
                key={album.id}
                size="small"
                label={album.name}
                sx={{ color: 'inherit', borderRadius: '6px', borderColor: 'rgba(255,255,255,0.2)' }}
                variant="outlined"
              />
            ))
          )}
        </Box>
      </div>
      <details>
        <summary>Raw metadata</summary>
        <pre className="mt-2 overflow-x-auto text-xs">
          {JSON.stringify(detail.rawMetadata, null, 2)}
        </pre>
      </details>
    </Stack>
  )
}
