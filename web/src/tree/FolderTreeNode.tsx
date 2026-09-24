import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import WarningAmberIcon from '@mui/icons-material/WarningAmber'
import { CircularProgress, IconButton, List, ListItemButton, ListItemText } from '@mui/material'
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import { useFolderChildren } from '../api/queries'
import type { FolderNode } from '../api/types'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'

type Props = {
  node: FolderNode
  depth: number
  selectedId: number | null
  isExpanded: (id: number) => boolean
  onToggle: (id: number) => void
}

export function FolderTreeNode({ node, depth, selectedId, isExpanded, onToggle }: Props) {
  const navigate = useNavigate()
  const expanded = node.hasChildren && isExpanded(node.id)
  const children = useFolderChildren(node.id, expanded)
  const selected = node.id === selectedId
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (selected) ref.current?.scrollIntoView({ block: 'nearest' })
  }, [selected])

  return (
    <>
      <ListItemButton
        ref={ref}
        role="treeitem"
        aria-label={node.isMissing ? `${node.name} (missing)` : node.name}
        aria-level={depth + 1}
        aria-selected={selected}
        aria-expanded={node.hasChildren ? expanded : undefined}
        selected={selected}
        onClick={() => navigate(`/folders/${node.id}`)}
        sx={{ pl: 1 + depth * 2, py: 0.25 }}
      >
        <IconButton
          size="small"
          aria-label={expanded ? `Collapse ${node.name}` : `Expand ${node.name}`}
          tabIndex={node.hasChildren ? 0 : -1}
          onClick={(event) => {
            event.stopPropagation()
            onToggle(node.id)
          }}
          sx={{ mr: 0.5, visibility: node.hasChildren ? 'visible' : 'hidden' }}
        >
          {expanded ? <ExpandMoreIcon fontSize="small" /> : <ChevronRightIcon fontSize="small" />}
        </IconButton>
        {node.isMissing && <WarningAmberIcon fontSize="small" color="warning" sx={{ mr: 0.5 }} />}
        <ListItemText
          primary={node.name}
          slotProps={{
            primary: { noWrap: true, color: node.isMissing ? 'text.secondary' : undefined },
          }}
        />
        {expanded && children.isFetching && <CircularProgress size={14} />}
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
            />
          ))}
        </List>
      )}
    </>
  )
}
