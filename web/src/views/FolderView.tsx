import { Alert, Box, Link, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { Link as RouterLink, useParams, useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { isNotFound } from '../api/client'
import { useFolder } from '../api/queries'
import { parseFacesParam, parseGridParams, parseHiddenParam, parseId } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { useFolderJobs } from '../tree/FolderJobsContext'
import { ReanalyseFacesConfirm } from '../tree/ReanalyseFacesConfirm'
import { GridHeader } from './GridHeader'
import { EmptyMessage } from '../shared/EmptyMessage'
import { FacesFilterToggle } from './FacesFilterToggle'
import { GridSkeleton } from './GridSkeleton'
import { FolderMenu } from './FolderMenu'
import { ImageBrowser } from './ImageBrowser'
import { writeLastFolderId } from './preferences'

export function FolderView() {
  const params = useParams()
  const folderId = parseId(params.folderId ?? null)
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)
  const faces = parseFacesParam(searchParams)
  const showHidden = parseHiddenParam(searchParams)
  const folder = useFolder(folderId)
  const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)
  const [confirmingReanalyse, setConfirmingReanalyse] = useState(false)
  const { activeJob, recognizeFaces } = useFolderJobs()

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
        hideable
        rotatable
        filter={{
          kind: 'folder',
          folderId,
          sort,
          order,
          ...(faces !== undefined && { faces }),
          ...(showHidden && { includeHidden: true }),
        }}
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
                <FacesFilterToggle value={faces} />
              ) : undefined
            }
            menu={
              !detail.isMissing ? (
                <FolderMenu
                  showHidden={showHidden}
                  hasPhotos={detail.imageCount > 0}
                  reanalyseDisabled={activeJob !== null}
                  onReanalyse={() => setConfirmingReanalyse(true)}
                  onAddToAlbum={() => setPickerTarget({ folderId })}
                />
              ) : undefined
            }
          />
        }
        banner={banner}
        emptyState={
          faces === undefined ? null : (
            <EmptyMessage>
              {faces === 'with' ? 'No photos with a face here.' : 'No photos without a face here.'}
            </EmptyMessage>
          )
        }
      />
      {confirmingReanalyse && (
        <ReanalyseFacesConfirm
          folderName={detail.name}
          recursive={false}
          onConfirm={(preset) => {
            setConfirmingReanalyse(false)
            recognizeFaces(folderId, { isRecursive: false, reanalyze: true, preset })
          }}
          onClose={() => setConfirmingReanalyse(false)}
        />
      )}
      {pickerTarget !== null && (
        <AlbumPicker target={pickerTarget} onClose={() => setPickerTarget(null)} />
      )}
    </>
  )
}
