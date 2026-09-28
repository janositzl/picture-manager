import CreateNewFolderOutlinedIcon from '@mui/icons-material/CreateNewFolderOutlined'
import { Alert, Box, Button, Link, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { Link as RouterLink, useParams, useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { isNotFound } from '../api/client'
import { useFolder } from '../api/queries'
import { BORDER } from '../design/accent'
import { parseGridParams, parseId } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { GridHeader } from './GridHeader'
import { GridSkeleton } from './GridSkeleton'
import { ImageBrowser } from './ImageBrowser'
import { writeLastFolderId } from './preferences'

export function FolderView() {
  const params = useParams()
  const folderId = parseId(params.folderId ?? null)
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)
  const folder = useFolder(folderId)
  const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)

  // Only once the folder is confirmed to exist, so a dead/typo'd id in the URL isn't remembered.
  useEffect(() => {
    if (folderId !== null && folder.data !== undefined) writeLastFolderId(folderId)
  }, [folderId, folder.data])

  if (folderId === null || isNotFound(folder.error)) {
    return (
      <Box className="p-8">
        <Typography gutterBottom sx={{ fontWeight: 600 }}>
          Folder not found.
        </Typography>
        <Link component={RouterLink} to="/" className="transition-colors duration-200 ease-in-out">
          Go to the first folder
        </Link>
      </Box>
    )
  }

  if (folder.isPending) return <GridSkeleton />

  if (folder.isError) {
    return (
      <QueryErrorAlert message="Couldn't load this folder." onRetry={() => void folder.refetch()} />
    )
  }

  const detail = folder.data
  let banner = null
  if (detail.isMissing) {
    banner = (
      <Alert severity="warning" sx={{ m: 2, mb: 0, borderRadius: '10px' }}>
        This folder is missing on disk. Its photos are hidden until it's back. Rescan or remove it
        (Admin).
      </Alert>
    )
  } else if (detail.imageCount === 0) {
    banner = (
      <Alert severity="info" sx={{ m: 2, mb: 0, borderRadius: '10px' }}>
        No photos directly in this folder. Pick a subfolder in the tree.
      </Alert>
    )
  }

  return (
    <>
      <ImageBrowser
        filter={{ kind: 'folder', folderId, sort, order }}
        header={
          <GridHeader
            title={detail.name}
            path={detail.breadcrumb}
            // A missing folder's photos are hidden, so its count would contradict the empty grid.
            count={detail.isMissing ? undefined : detail.imageCount}
            sort={sort}
            order={order}
            actions={
              !detail.isMissing && detail.imageCount > 0 ? (
                <Button
                  size="small"
                  startIcon={<CreateNewFolderOutlinedIcon fontSize="small" />}
                  onClick={() => setPickerTarget({ folderId })}
                  sx={{
                    color: 'text.primary',
                    border: `1px solid ${BORDER}`,
                    borderRadius: '10px',
                    textTransform: 'none',
                    fontWeight: 700,
                    px: 1.5,
                    py: '7px',
                    '&:hover': { bgcolor: 'action.hover', borderColor: BORDER },
                  }}
                >
                  Add folder to album…
                </Button>
              ) : undefined
            }
          />
        }
        banner={banner}
        emptyState={null}
      />
      {pickerTarget !== null && (
        <AlbumPicker target={pickerTarget} onClose={() => setPickerTarget(null)} />
      )}
    </>
  )
}
