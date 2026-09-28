import { Alert, CircularProgress, Link } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { useFolder } from '../api/queries'
import { useFolderJobs, type ActiveJob } from './FolderJobsContext'

/** Shared with FolderActionsMenu's per-folder caption. */
export function progressLabel(activeJob: ActiveJob): string {
  if (activeJob.kind === 'discoveries') {
    return `Discovering… ${activeJob.progress?.foldersDiscovered ?? 0} folders found`
  }
  const progress = activeJob.progress
  if (progress?.status === 'Enriching') {
    return `Enriching… ${progress.filesEnriched}/${progress.filesFound} files`
  }
  return `Scanning… ${progress?.foldersScanned ?? 0} folders, ${progress?.filesFound ?? 0} files`
}

/** App-wide banner for the single discovery/scan job the backend allows at a time. */
export function JobStatusBanner() {
  const { activeJob } = useFolderJobs()
  const folder = useFolder(activeJob?.folderId ?? null)

  if (activeJob === null) return null

  return (
    <Alert
      severity="info"
      icon={<CircularProgress size={16} />}
      sx={{ borderRadius: 0 }}
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
