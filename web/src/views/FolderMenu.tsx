import AutorenewIcon from '@mui/icons-material/Autorenew'
import CheckIcon from '@mui/icons-material/Check'
import CreateNewFolderOutlinedIcon from '@mui/icons-material/CreateNewFolderOutlined'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import VisibilityOffOutlinedIcon from '@mui/icons-material/VisibilityOffOutlined'
import {
  Badge,
  Divider,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Tooltip,
} from '@mui/material'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { BORDER } from '../design/accent'
import { withParams } from '../routing/urlState'

type Props = {
  showHidden: boolean
  /** False for a folder with no photos of its own: nothing to analyse or add. */
  hasPhotos: boolean
  reanalyseDisabled: boolean
  onReanalyse: () => void
  onAddToAlbum: () => void
}

/** The folder header's "more" menu; the Show hidden choice lives in ?hidden=1 so it survives reloads. */
export function FolderMenu({
  showHidden,
  hasPhotos,
  reanalyseDisabled,
  onReanalyse,
  onAddToAlbum,
}: Props) {
  const [searchParams, setSearchParams] = useSearchParams()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const close = () => setAnchor(null)
  const run = (action: () => void) => () => {
    close()
    action()
  }

  return (
    <>
      <Tooltip title="Folder actions">
        <IconButton
          aria-label="Folder actions"
          aria-haspopup="menu"
          aria-expanded={anchor !== null}
          onClick={(event) => setAnchor(event.currentTarget)}
          className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
          sx={{
            border: `1px solid ${BORDER}`,
            borderRadius: '10px',
            bgcolor: 'background.paper',
            '&:hover': { bgcolor: 'action.hover' },
          }}
        >
          {/* A dot keeps "hidden photos are showing" visible while the menu is closed. */}
          <Badge color="primary" variant="dot" invisible={!showHidden}>
            <MoreVertIcon fontSize="small" />
          </Badge>
        </IconButton>
      </Tooltip>
      <Menu
        anchorEl={anchor}
        open={anchor !== null}
        onClose={close}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{
          paper: {
            sx: { mt: 0.5, minWidth: 240, borderRadius: '12px', border: `1px solid ${BORDER}` },
          },
        }}
      >
        <MenuItem
          role="menuitemcheckbox"
          aria-checked={showHidden}
          onClick={run(() =>
            setSearchParams(withParams(searchParams, { hidden: showHidden ? null : 1 }), {
              replace: true,
            }),
          )}
        >
          <ListItemIcon>
            <VisibilityOffOutlinedIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Show hidden photos</ListItemText>
          {showHidden && <CheckIcon fontSize="small" color="primary" />}
        </MenuItem>
        {hasPhotos && <Divider />}
        {hasPhotos && (
          <MenuItem disabled={reanalyseDisabled} onClick={run(onReanalyse)}>
            <ListItemIcon>
              <AutorenewIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Re-analyse faces…</ListItemText>
          </MenuItem>
        )}
        {hasPhotos && (
          <MenuItem onClick={run(onAddToAlbum)}>
            <ListItemIcon>
              <CreateNewFolderOutlinedIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Add folder to album…</ListItemText>
          </MenuItem>
        )}
      </Menu>
    </>
  )
}
