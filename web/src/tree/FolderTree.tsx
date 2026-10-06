import { Box, CircularProgress, List } from '@mui/material'
import { useState } from 'react'
import { useMatch } from 'react-router'
import { useFaceCoverage, useFolder, useRootFolders } from '../api/queries'
import type { FolderFaceCoverage } from '../api/types'
import { parseId } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { useFolderJobs } from './FolderJobsContext'
import { FolderTreeNode } from './FolderTreeNode'

const NO_COVERAGE: ReadonlyMap<number, FolderFaceCoverage> = new Map()

export function FolderTree() {
  const match = useMatch('/folders/:folderId')
  const selectedId = parseId(match?.params.folderId ?? null)
  const roots = useRootFolders()
  const selected = useFolder(selectedId)
  const { activeJob } = useFolderJobs()
  // Failures and loading just mean no face status; the tree itself never waits for this.
  const faceCoverage = useFaceCoverage(activeJob?.kind === 'face-recognitions').data ?? NO_COVERAGE
  // Explicit user toggles win; otherwise the selected folder and its ancestors start expanded,
  // so a deep link opens the tree down to the folder.
  const [toggled, setToggled] = useState<ReadonlyMap<number, boolean>>(new Map())
  const pathIds = new Set((selected.data?.breadcrumb ?? []).map((crumb) => crumb.id))

  const isExpanded = (id: number) => toggled.get(id) ?? pathIds.has(id)
  const toggle = (id: number) =>
    setToggled((prev) => new Map(prev).set(id, !(prev.get(id) ?? pathIds.has(id))))

  if (roots.isPending) {
    return (
      <Box className="p-3">
        <CircularProgress size={20} />
      </Box>
    )
  }

  if (roots.isError) {
    return <QueryErrorAlert message="Couldn't load folders." onRetry={() => void roots.refetch()} />
  }

  return (
    <List dense role="tree" aria-label="Folder tree" className="px-1.5 py-2">
      {roots.data.map((node) => (
        <FolderTreeNode
          key={node.id}
          node={node}
          depth={0}
          selectedId={selectedId}
          isExpanded={isExpanded}
          onToggle={toggle}
          ancestorExcluded={false}
          parentId={null}
          faceCoverage={faceCoverage}
        />
      ))}
    </List>
  )
}
