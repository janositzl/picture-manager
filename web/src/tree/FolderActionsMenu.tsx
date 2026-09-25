import MoreVertIcon from '@mui/icons-material/MoreVert'
import { CircularProgress, IconButton, Menu, MenuItem, Typography } from '@mui/material'
import { useState, type MouseEvent } from 'react'
import type { ActiveJob } from './FolderJobsContext'
import { useFolderJobs } from './FolderJobsContext'

type Props = {
  folderId: number
  folderName: string
}

function progressLabel(activeJob: ActiveJob): string {
  if (activeJob.kind === 'discoveries') {
    return `Discovering… ${activeJob.progress?.foldersDiscovered ?? 0} folders found`
  }
  const progress = activeJob.progress
  if (progress?.status === 'Enriching') {
    return `Enriching… ${progress.filesEnriched}/${progress.filesFound} files`
  }
  return `Scanning… ${progress?.foldersScanned ?? 0} folders, ${progress?.filesFound ?? 0} files`
}

export function FolderActionsMenu({ folderId, folderName }: Props) {
  const { activeJob, refreshFolder, scanFolder } = useFolderJobs()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)

  if (activeJob?.folderId === folderId) {
    return (
      <>
        <CircularProgress size={14} sx={{ ml: 0.5 }} />
        <Typography variant="caption" color="text.secondary" noWrap sx={{ ml: 0.5 }}>
          {progressLabel(activeJob)}
        </Typography>
      </>
    )
  }

  const close = () => setAnchorEl(null)
  const runAction = (action: () => void) => {
    action()
    close()
  }

  return (
    <>
      <IconButton
        size="small"
        aria-label={`Actions for ${folderName}`}
        disabled={activeJob !== null}
        onClick={(event: MouseEvent<HTMLElement>) => {
          event.stopPropagation()
          setAnchorEl(event.currentTarget)
        }}
      >
        <MoreVertIcon fontSize="small" />
      </IconButton>
      <Menu
        anchorEl={anchorEl}
        open={anchorEl !== null}
        onClose={close}
        onClick={(event: MouseEvent) => event.stopPropagation()}
      >
        <MenuItem onClick={() => runAction(() => refreshFolder(folderId))}>
          Refresh structure
        </MenuItem>
        <MenuItem onClick={() => runAction(() => scanFolder(folderId, false))}>
          Scan folder
        </MenuItem>
        <MenuItem onClick={() => runAction(() => scanFolder(folderId, true))}>
          Scan folder + subfolders
        </MenuItem>
      </Menu>
    </>
  )
}
