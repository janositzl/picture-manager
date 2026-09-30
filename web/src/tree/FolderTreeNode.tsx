import BlockIcon from '@mui/icons-material/Block'
import CheckCircleOutlinedIcon from '@mui/icons-material/CheckCircleOutlined'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
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
} from '@mui/material'
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import { useFolderChildren } from '../api/queries'
import type { FolderNode } from '../api/types'
import { ACCENT, ACCENT_SOFT, ACCENT_TEXT } from '../design/accent'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { FolderActionsMenu } from './FolderActionsMenu'

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
}

export function FolderTreeNode({
  node,
  depth,
  selectedId,
  isExpanded,
  onToggle,
  ancestorExcluded,
  parentId,
}: Props) {
  const navigate = useNavigate()
  const expanded = node.hasChildren && isExpanded(node.id)
  const children = useFolderChildren(node.id, expanded)
  const selected = node.id === selectedId
  const ref = useRef<HTMLDivElement>(null)
  const excluded = node.isExcluded || ancestorExcluded
  const dimmed = node.isMissing || excluded

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
          {expanded ? <ExpandMoreIcon fontSize="small" /> : <ChevronRightIcon fontSize="small" />}
        </IconButton>
        {excluded ? (
          <Tooltip
            title={
              node.isExcluded
                ? 'Excluded from scan'
                : 'Excluded from scan (parent folder is excluded)'
            }
          >
            <BlockIcon fontSize="small" color="disabled" sx={{ mr: 0.75 }} />
          </Tooltip>
        ) : expanded ? (
          <FolderOpenOutlinedIcon
            fontSize="small"
            sx={{ mr: 0.75, color: selected ? ACCENT : 'text.secondary' }}
          />
        ) : (
          <FolderOutlinedIcon
            fontSize="small"
            sx={{ mr: 0.75, color: selected ? ACCENT : 'text.secondary' }}
          />
        )}
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
        {node.isScanned && !excluded && (
          <Tooltip title="Scanned">
            <CheckCircleOutlinedIcon fontSize="inherit" color="success" sx={{ ml: 0.5, fontSize: 14 }} />
          </Tooltip>
        )}
        {expanded && children.isFetching && <CircularProgress size={14} />}
        <FolderActionsMenu
          folderId={node.id}
          folderName={node.name}
          isExcluded={node.isExcluded}
          ancestorExcluded={ancestorExcluded}
          isRootFolder={depth === 0}
          parentId={parentId}
        />
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
            />
          ))}
        </List>
      )}
    </>
  )
}
