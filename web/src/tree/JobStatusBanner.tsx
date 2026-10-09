import { Alert, Button, CircularProgress, Link } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { usePermissions } from '../api/auth'
import { useFolder } from '../api/queries'
import { useFolderJobs, type ActiveJob } from './FolderJobsContext'

/** Shared with FolderActionsMenu's per-folder caption. */
export function progressLabel(activeJob: ActiveJob): string {
  if (activeJob.kind === 'discoveries') {
    return `Discovering… ${activeJob.progress?.foldersDiscovered ?? 0} folders found`
  }
  if (activeJob.kind === 'face-recognitions') {
    const progress = activeJob.progress
    if (progress !== null && progress.imagesFound > 0 && progress.imagesProcessed >= progress.imagesFound) {
      return 'Grouping faces…'
    }
    return `Recognizing faces… ${progress?.imagesProcessed ?? 0}/${progress?.imagesFound ?? 0} images, ${progress?.facesFound ?? 0} faces`
  }
  const progress = activeJob.progress
  if (progress?.status === 'Enriching') {
    return `Enriching… ${progress.filesEnriched}/${progress.filesFound} files`
  }
  return `Scanning… ${progress?.foldersScanned ?? 0} folders, ${progress?.filesFound ?? 0} files`
}

/** App-wide banner for the single discovery/scan job the backend allows at a time. */
export function JobStatusBanner() {
  const { activeJob, cancelActiveJob } = useFolderJobs()
  const { canRunFolderActions } = usePermissions()
  const folder = useFolder(activeJob?.folderId ?? null)

  if (activeJob === null) return null

  return (
    <Alert
      severity="info"
      icon={<CircularProgress size={16} />}
      sx={{ borderRadius: 0 }}
      action={
        activeJob.kind === 'face-recognitions' && canRunFolderActions ? (
          <Button color="inherit" size="small" onClick={cancelActiveJob}>
            Cancel
          </Button>
        ) : undefined
      }
    >
      {progressLabel(activeJob)}
      {folder.data && (
        <>
          {' — '}
          <Link component={RouterLink} to={`/folders/${activeJob.folderId}`}>
            {folder.data.name}
          </Link>
        </>
      )}
    </Alert>
  )
}
