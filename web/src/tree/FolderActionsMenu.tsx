import CheckIcon from '@mui/icons-material/Check'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import {
  CircularProgress,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Typography,
} from '@mui/material'
import { useState, type MouseEvent } from 'react'
import { folderExclusionErrorMessage, useSetFolderExcluded } from '../api/folders'
import { useNotify } from '../app/notify'
import { useFolderJobs } from './FolderJobsContext'
import { progressLabel } from './JobStatusBanner'

type Props = {
  folderId: number
  folderName: string
  isExcluded: boolean
  ancestorExcluded: boolean
}

export function FolderActionsMenu({ folderId, folderName, isExcluded, ancestorExcluded }: Props) {
  const { activeJob, refreshFolder, scanFolder } = useFolderJobs()
  const setExcluded = useSetFolderExcluded()
  const notify = useNotify()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const excluded = isExcluded || ancestorExcluded
  const scanDisabled = activeJob !== null || excluded

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
  const toggleExcluded = () => {
    setExcluded.mutate(
      { folderId, isExcluded: !isExcluded },
      { onError: (error) => notify(folderExclusionErrorMessage(error)) },
    )
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
        slotProps={{
          paper: {
            className: 'rounded-xl border border-zinc-100 shadow-lg dark:border-zinc-800',
            sx: { minWidth: 220 },
          },
        }}
      >
        <MenuItem onClick={() => runAction(() => refreshFolder(folderId))} disabled={scanDisabled}>
          Refresh structure
        </MenuItem>
        <MenuItem onClick={() => runAction(() => scanFolder(folderId, false))} disabled={scanDisabled}>
          Scan folder
        </MenuItem>
        <MenuItem
          onClick={() => runAction(() => scanFolder(folderId, true))}
          disabled={scanDisabled}
        >
          Scan folder + subfolders
        </MenuItem>
        <MenuItem onClick={toggleExcluded} disabled={ancestorExcluded}>
          {excluded && (
            <ListItemIcon>
              <CheckIcon fontSize="small" />
            </ListItemIcon>
          )}
          <ListItemText
            inset={!excluded}
            primary="Exclude folder"
            secondary={ancestorExcluded ? 'Excluded via parent folder' : undefined}
          />
        </MenuItem>
      </Menu>
    </>
  )
}
