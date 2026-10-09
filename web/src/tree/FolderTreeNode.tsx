import BlockIcon from '@mui/icons-material/Block'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import FolderIcon from '@mui/icons-material/Folder'
import FolderOpenIcon from '@mui/icons-material/FolderOpen'
import FolderOpenOutlinedIcon from '@mui/icons-material/FolderOpenOutlined'
import FolderOutlinedIcon from '@mui/icons-material/FolderOutlined'
import WarningAmberIcon from '@mui/icons-material/WarningAmber'
import {
  CircularProgress,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Tooltip,
  Typography,
} from '@mui/material'
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import { usePermissions } from '../api/auth'
import { useFolderChildren, usePrefetchFolderChildren } from '../api/queries'
import type { FolderFaceCoverage, FolderNode } from '../api/types'
import { ACCENT_SOFT, ACCENT_TEXT, SCANNED_BLUE } from '../design/accent'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { FaceStatusIcon } from './FaceStatusIcon'
import { faceCoverageLabel, faceCoverageState } from './faceCoverage'
import { FolderActionsMenu } from './FolderActionsMenu'
import { useFolderJobs } from './FolderJobsContext'
import { progressLabel } from './JobStatusBanner'

type Props = {
  node: FolderNode
  depth: number
  selectedId: number | null
  isExpanded: (id: number) => boolean
  onToggle: (id: number) => void
  /** True when an ancestor (not this folder itself) is excluded from scans. */
  ancestorExcluded: boolean
  /** Null for a root's top folder (depth 0), which has no parent. */
  parentId: number | null
  /** Face-detection coverage by folder id, for this node and its descendants. */
  faceCoverage: ReadonlyMap<number, FolderFaceCoverage>
}

/** The running job's caption on its folder's row; shown to everyone, unlike the actions menu. */
function FolderJobProgress({ folderId }: { folderId: number }) {
  const { activeJob } = useFolderJobs()
  if (activeJob?.folderId !== folderId) return null
  return (
    <>
      <CircularProgress size={14} sx={{ ml: 0.5 }} />
      <Typography variant="caption" color="text.secondary" noWrap sx={{ ml: 0.5 }}>
        {progressLabel(activeJob)}
      </Typography>
    </>
  )
}

export function FolderTreeNode({
  node,
  depth,
  selectedId,
  isExpanded,
  onToggle,
  ancestorExcluded,
  parentId,
  faceCoverage,
}: Props) {
  const navigate = useNavigate()
  const { canRunFolderActions } = usePermissions()
  const { activeJob } = useFolderJobs()
  const expanded = node.hasChildren && isExpanded(node.id)
  const children = useFolderChildren(node.id, expanded)
  const prefetchChildren = usePrefetchFolderChildren()
  // Hover/focus warms the cache, so by the time the chevron is clicked the subfolders are usually already there.
  const warmChildren = () => {
    if (node.hasChildren && !expanded) prefetchChildren(node.id)
  }
  const selected = node.id === selectedId
  const ref = useRef<HTMLDivElement>(null)
  const excluded = node.isExcluded || ancestorExcluded
  const dimmed = node.isMissing || excluded
  const faceState = faceCoverageState(faceCoverage.get(node.id))
  const faceLabel = faceCoverageLabel(faceCoverage.get(node.id))

  useEffect(() => {
    if (selected) ref.current?.scrollIntoView({ block: 'nearest' })
  }, [selected])

  const statusLabels = [node.isMissing && 'missing', excluded && 'excluded'].filter(
    (label): label is string => label !== false,
  )
  const ariaLabel =
    statusLabels.length > 0 ? `${node.name} (${statusLabels.join(', ')})` : node.name

  return (
    <>
      <ListItemButton
        ref={ref}
        role="treeitem"
        aria-label={ariaLabel}
        aria-level={depth + 1}
        aria-selected={selected}
        aria-expanded={node.hasChildren ? expanded : undefined}
        selected={selected}
        onClick={() => navigate(`/folders/${node.id}`)}
        onPointerEnter={warmChildren}
        onFocus={warmChildren}
        className="transition-colors duration-200 ease-in-out"
        sx={{
          pl: 1 + depth * 2,
          py: 0.4,
          borderRadius: '8px',
          mx: 0.5,
          '&.Mui-selected': { bgcolor: ACCENT_SOFT, color: ACCENT_TEXT },
          '&.Mui-selected:hover': { bgcolor: ACCENT_SOFT },
        }}
      >
        <IconButton
          size="small"
          aria-label={expanded ? `Collapse ${node.name}` : `Expand ${node.name}`}
          tabIndex={node.hasChildren ? 0 : -1}
          onClick={(event) => {
            event.stopPropagation()
            onToggle(node.id)
          }}
          className="transition-transform duration-200 ease-in-out"
          sx={{ mr: 0.5, visibility: node.hasChildren ? 'visible' : 'hidden' }}
        >
          {expanded && children.isPending ? (
            <CircularProgress size={18} />
          ) : (
            <ChevronRightIcon
              fontSize="small"
              sx={{
                transform: expanded ? 'rotate(90deg)' : 'none',
                transition: 'transform 150ms ease-in-out',
              }}
            />
          )}
        </IconButton>
        <Tooltip
          title={
            excluded
              ? node.isExcluded
                ? 'Excluded from scan'
                : 'Excluded from scan (parent folder is excluded)'
              : node.isScanned
                ? 'Scanned'
                : ''
          }
        >
          {excluded ? (
            <BlockIcon fontSize="small" color="disabled" sx={{ mr: 0.75 }} />
          ) : node.isScanned ? (
            expanded ? (
              <FolderOpenIcon fontSize="small" sx={{ mr: 0.75, color: SCANNED_BLUE }} />
            ) : (
              <FolderIcon fontSize="small" sx={{ mr: 0.75, color: SCANNED_BLUE }} />
            )
          ) : expanded ? (
            <FolderOpenOutlinedIcon fontSize="small" sx={{ mr: 0.75, color: 'text.primary' }} />
          ) : (
            <FolderOutlinedIcon fontSize="small" sx={{ mr: 0.75, color: 'text.primary' }} />
          )}
        </Tooltip>
        {node.isMissing && <WarningAmberIcon fontSize="small" color="warning" sx={{ mr: 0.5 }} />}
        <Tooltip title={node.name} enterDelay={500} disableInteractive>
          <ListItemText
            primary={node.name}
            slotProps={{
              primary: {
                noWrap: true,
                sx: {
                  fontWeight: selected ? 600 : 400,
                  color: dimmed ? 'text.secondary' : selected ? ACCENT_TEXT : 'text.primary',
                },
              },
            }}
          />
        </Tooltip>
        {!excluded && faceState !== 'none' && faceLabel && (
          <FaceStatusIcon state={faceState} label={faceLabel} />
        )}
        <FolderJobProgress folderId={node.id} />
        {canRunFolderActions && activeJob?.folderId !== node.id && (
          <FolderActionsMenu
            folderId={node.id}
            folderName={node.name}
            isExcluded={node.isExcluded}
            ancestorExcluded={ancestorExcluded}
            isRootFolder={depth === 0}
            parentId={parentId}
            faceState={faceState}
          />
        )}
      </ListItemButton>
      {expanded && children.isError && (
        <QueryErrorAlert
          message="Couldn't load subfolders."
          onRetry={() => void children.refetch()}
        />
      )}
      {expanded && children.data && children.data.length > 0 && (
        <List dense disablePadding role="group">
          {children.data.map((child) => (
            <FolderTreeNode
              key={child.id}
              node={child}
              depth={depth + 1}
              selectedId={selectedId}
              isExpanded={isExpanded}
              onToggle={onToggle}
              ancestorExcluded={excluded}
              parentId={node.id}
              faceCoverage={faceCoverage}
            />
          ))}
        </List>
      )}
    </>
  )
}
