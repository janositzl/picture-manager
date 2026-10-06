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
import { useNavigate, useParams } from 'react-router'
import {
  folderExclusionErrorMessage,
  folderRemoveErrorMessage,
  useRemoveFolder,
  useSetFolderExcluded,
} from '../api/folders'
import { useNotify } from '../app/notify'
import { parseId } from '../routing/urlState'
import { ConfirmDialog } from '../shared/ConfirmDialog'
import { useFolderJobs } from './FolderJobsContext'
import type { FaceCoverageState } from './faceCoverage'
import { progressLabel } from './JobStatusBanner'
import { ReanalyseFacesConfirm } from './ReanalyseFacesConfirm'

type Props = {
  folderId: number
  folderName: string
  isExcluded: boolean
  ancestorExcluded: boolean
  /** A root's top folder; it can't be removed from the collection (deactivate the root instead). */
  isRootFolder: boolean
  /** Null only for a root's top folder, which can't be removed. */
  parentId: number | null
  /** Face-detection state of this folder's own photos; once completed, "Recognize faces" becomes "Re-analyse faces". */
  faceState: FaceCoverageState
}

export function FolderActionsMenu({
  folderId,
  folderName,
  isExcluded,
  ancestorExcluded,
  isRootFolder,
  parentId,
  faceState,
}: Props) {
  const { activeJob, refreshFolder, scanFolder, recognizeFaces } = useFolderJobs()
  const setExcluded = useSetFolderExcluded()
  const removeFolder = useRemoveFolder()
  const notify = useNotify()
  const navigate = useNavigate()
  const params = useParams()
  const viewedFolderId = parseId(params.folderId ?? null)
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [confirmingRemove, setConfirmingRemove] = useState(false)
  const [confirmingReanalyse, setConfirmingReanalyse] = useState(false)
  const analysed = faceState === 'completed'
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
  const confirmRemove = async () => {
    try {
      await removeFolder.mutateAsync(folderId)
      // The removed folder's own detail page would otherwise 404; its parent still exists.
      if (viewedFolderId === folderId && parentId !== null) navigate(`/folders/${parentId}`)
    } catch (error) {
      notify(folderRemoveErrorMessage(error))
    } finally {
      setConfirmingRemove(false)
    }
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
        <MenuItem
          onClick={() => runAction(() => scanFolder(folderId, false))}
          disabled={scanDisabled}
        >
          Scan folder
        </MenuItem>
        <MenuItem
          onClick={() => runAction(() => scanFolder(folderId, true))}
          disabled={scanDisabled}
        >
          Scan folder + subfolders
        </MenuItem>
        <MenuItem
          onClick={() =>
            runAction(() => (analysed ? setConfirmingReanalyse(true) : recognizeFaces(folderId)))
          }
          disabled={scanDisabled}
        >
          {analysed ? 'Re-analyse faces' : 'Recognize faces'}
        </MenuItem>
        <MenuItem onClick={toggleExcluded} disabled={ancestorExcluded}>
          {isExcluded && (
            <ListItemIcon>
              <CheckIcon fontSize="small" />
            </ListItemIcon>
          )}
          <ListItemText
            primary="Exclude folder"
            secondary={ancestorExcluded ? 'Excluded via parent folder' : undefined}
          />
        </MenuItem>
        {!isRootFolder && (
          <MenuItem
            onClick={() => runAction(() => setConfirmingRemove(true))}
            disabled={activeJob !== null}
            sx={{ color: 'error.main' }}
          >
            Remove from collection
          </MenuItem>
        )}
      </Menu>
      {confirmingReanalyse && (
        <div onClick={(event: MouseEvent) => event.stopPropagation()}>
          <ReanalyseFacesConfirm
            folderName={folderName}
            recursive
            onConfirm={() => {
              setConfirmingReanalyse(false)
              recognizeFaces(folderId, { reanalyze: true })
            }}
            onClose={() => setConfirmingReanalyse(false)}
          />
        </div>
      )}
      {confirmingRemove && (
        // MUI's Dialog portals its DOM elsewhere, but synthetic events still bubble through the React
        // tree; without this, confirming would also fire the tree row's onClick and navigate to it.
        <div onClick={(event: MouseEvent) => event.stopPropagation()}>
          <ConfirmDialog
            title="Remove folder"
            message={`Remove ${folderName} from the collection? This permanently deletes its images and subfolders from the database (the files on disk are untouched). The folder itself can be restored from Admin > Removed folders, but its deleted images and subfolders cannot.`}
            confirmLabel="Remove"
            busy={removeFolder.isPending}
            onConfirm={confirmRemove}
            onClose={() => setConfirmingRemove(false)}
          />
        </div>
      )}
    </>
  )
}
