import DriveFileMoveOutlinedIcon from '@mui/icons-material/DriveFileMoveOutlined'
import EditOutlinedIcon from '@mui/icons-material/EditOutlined'
import HideSourceIcon from '@mui/icons-material/HideSource'
import { Box, Button, IconButton, Tooltip } from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink, useLocation, useParams, useSearchParams } from 'react-router'
import { usePermissions } from '../api/auth'
import { unknownLabels, usePeople, usePerson, useSetPersonCover } from '../api/people'
import { useNotify } from '../app/notify'
import { parseGridParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from '../views/GridHeader'
import { ImageBrowser } from '../views/ImageBrowser'
import { AssignGroupDialog } from './AssignGroupDialog'
import { FACE_MODES } from './faceModes'
import { FaceModeToggle } from './FaceModeToggle'
import { openedFromStrip } from './openedFromStrip'
import { IgnoreGroupConfirm, PersonEditDialog } from './PersonEditDialog'
import { SuggestedStrip } from './SuggestedStrip'
import { useStoredChoice } from './useStoredChoice'

function PersonNotFound() {
  return (
    <Box sx={{ p: 3 }}>
      <EmptyMessage>Person not found</EmptyMessage>
      <Button component={RouterLink} to="/people" sx={{ ml: 3 }}>
        Back to People
      </Button>
    </Box>
  )
}

type PersonContentProps = { personId: number }

function PersonContent({ personId }: PersonContentProps) {
  const person = usePerson(personId)
  const people = usePeople()
  const { isAdmin } = usePermissions()
  const setCover = useSetPersonCover()
  const notify = useNotify()
  const [searchParams] = useSearchParams()
  const location = useLocation()
  const { sort, order } = parseGridParams(searchParams)
  const [gridMode, setGridMode] = useStoredChoice('pm.people.gridMode', FACE_MODES, 'photos')
  const [stripMode, setStripMode] = useStoredChoice('pm.people.stripMode', FACE_MODES, 'faces')
  const [editing, setEditing] = useState(false)
  const [ignoring, setIgnoring] = useState(false)
  const [assigning, setAssigning] = useState<{ imageIds?: number[]; clear?: () => void } | null>(
    null,
  )

  if (person.isError) return <PersonNotFound />
  // The grid's filter depends on whether the person is named, so wait for that.
  if (person.data === undefined) return null

  // A named person's grid holds what the user confirmed; the AI's suggestions wait in the strip above it.
  // An unnamed group has nothing confirmed yet, so its grid is the suggestions themselves.
  const isGroup = person.data.name === null
  const gridState = isGroup ? 'suggested' : 'confirmed'
  const label =
    person.data.name ?? unknownLabels(people.data ?? []).get(personId) ?? 'Unknown person'

  return (
    <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
      <ImageBrowser
        filter={{ kind: 'person', personId, state: gridState, sort, order }}
        showFaceCrops={gridMode === 'faces'}
        faceReview={isAdmin ? { personId } : undefined}
        onAssignSelected={
          isAdmin && isGroup ? (imageIds, clear) => setAssigning({ imageIds, clear }) : undefined
        }
        onSetCoverSelected={
          isAdmin
            ? (faceId, clearSelection) =>
                setCover.mutate(
                  { id: personId, faceId },
                  {
                    onSuccess: () => {
                      notify('Cover updated.')
                      clearSelection()
                    },
                    onError: () => notify("Couldn't change the cover."),
                  },
                )
            : undefined
        }
        viewerEnabled={!openedFromStrip(location.state)}
        banner={
          isAdmin &&
          !isGroup && (
            <SuggestedStrip
              key={personId}
              personId={personId}
              sort={sort}
              order={order}
              mode={stripMode}
              onModeChange={setStripMode}
            />
          )
        }
        header={
          <GridHeader
            title={label}
            count={isGroup ? person.data.suggestedImageCount : person.data.confirmedImageCount}
            sort={sort}
            order={order}
            titleAdornment={
              isAdmin ? (
                <>
                  <Tooltip title="Edit person">
                    <IconButton
                      size="small"
                      aria-label="Edit person"
                      onClick={() => setEditing(true)}
                      className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
                    >
                      <EditOutlinedIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  {isGroup && (
                    <Tooltip title="Assign to person">
                      <IconButton
                        size="small"
                        aria-label="Assign to person"
                        onClick={() => setAssigning({})}
                        className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
                      >
                        <DriveFileMoveOutlinedIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                  {isGroup && (
                    <Tooltip title="Ignore group">
                      <IconButton
                        size="small"
                        aria-label="Ignore group"
                        onClick={() => setIgnoring(true)}
                        className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
                      >
                        <HideSourceIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                </>
              ) : undefined
            }
            actions={<FaceModeToggle value={gridMode} onChange={setGridMode} label="Grid view" />}
          />
        }
        emptyState={
          <EmptyMessage>
            {isGroup ? 'No photos for this person.' : 'No confirmed photos yet.'}
          </EmptyMessage>
        }
      />
      {ignoring && (
        <IgnoreGroupConfirm
          person={person.data}
          label={label}
          onDone={() => setIgnoring(false)}
          onClose={() => setIgnoring(false)}
        />
      )}
      {assigning && (
        <AssignGroupDialog
          group={person.data}
          label={label}
          imageIds={assigning.imageIds}
          onAssigned={assigning.clear}
          onClose={() => setAssigning(null)}
        />
      )}
      {editing && (
        <PersonEditDialog
          key={person.data.id}
          person={person.data}
          label={label}
          onClose={() => setEditing(false)}
        />
      )}
    </Box>
  )
}

export function PersonView() {
  const params = useParams()
  const personId = Number(params.personId)
  if (!Number.isInteger(personId) || personId <= 0) return <PersonNotFound />
  return <PersonContent personId={personId} />
}
