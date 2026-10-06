import { Box, Button, TextField, Typography } from '@mui/material'
import { useState, type FormEvent } from 'react'
import { Link as RouterLink, useLocation, useNavigate, useParams, useSearchParams } from 'react-router'
import { useNamePerson, usePeople, usePerson, type PersonSummary } from '../api/people'
import { useNotify } from '../app/notify'
import { parseGridParams } from '../routing/urlState'
import { ConfirmDialog } from '../shared/ConfirmDialog'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from '../views/GridHeader'
import { ImageBrowser } from '../views/ImageBrowser'
import { FACE_MODES } from './faceModes'
import { FaceModeToggle } from './FaceModeToggle'
import { openedFromStrip } from './openedFromStrip'
import { SuggestedStrip } from './SuggestedStrip'
import { useStoredChoice } from './useStoredChoice'

const imageCount = (person: PersonSummary) => person.confirmedImageCount + person.suggestedImageCount

type NameFormProps = { personId: number; name: string | null }

function NameForm({ personId, name }: NameFormProps) {
  const [draft, setDraft] = useState(name ?? '')
  const [mergeTarget, setMergeTarget] = useState<PersonSummary | null>(null)
  const people = usePeople()
  const namePerson = useNamePerson()
  const navigate = useNavigate()
  const notify = useNotify()

  const save = () => {
    namePerson.mutate(
      { id: personId, name: draft },
      {
        onSuccess: (survivor) => {
          setDraft(survivor.name ?? '')
          if (survivor.id !== personId) navigate(`/people/${survivor.id}`, { replace: true })
        },
        onError: () => notify("Couldn't save the name."),
      },
    )
  }

  // Another person's name merges this one into it for good, so ask first (only when the list is known).
  const submit = (event: FormEvent) => {
    event.preventDefault()
    const typed = draft.trim().toLowerCase()
    const existing = people.data?.find((p) => p.id !== personId && p.name?.trim().toLowerCase() === typed)
    if (existing) setMergeTarget(existing)
    else save()
  }

  return (
    <form onSubmit={submit} className="flex items-center gap-2">
      <TextField
        size="small"
        label={name === null ? 'Name this person' : 'Name'}
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
      />
      <Button type="submit" variant="contained" disabled={draft.trim() === '' || namePerson.isPending}>
        Save name
      </Button>
      {mergeTarget && (
        <ConfirmDialog
          title="Merge people?"
          message={`Merge into existing '${mergeTarget.name}' (${imageCount(mergeTarget)} ${imageCount(mergeTarget) === 1 ? 'photo' : 'photos'})? This can't be undone.`}
          confirmLabel="Merge"
          onConfirm={() => {
            setMergeTarget(null)
            save()
          }}
          onClose={() => setMergeTarget(null)}
        />
      )}
    </form>
  )
}

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
  const [searchParams] = useSearchParams()
  const location = useLocation()
  const { sort, order } = parseGridParams(searchParams)
  const [gridMode, setGridMode] = useStoredChoice('pm.people.gridMode', FACE_MODES, 'photos')
  const [stripMode, setStripMode] = useStoredChoice('pm.people.stripMode', FACE_MODES, 'faces')

  if (person.isError) return <PersonNotFound />
  // The grid's filter depends on whether the person is named, so wait for that.
  if (person.data === undefined) return null

  // A named person's grid holds what the user confirmed; the AI's suggestions wait in the strip above it.
  // An unnamed group has nothing confirmed yet, so its grid is the suggestions themselves.
  const isGroup = person.data.name === null
  const gridState = isGroup ? 'suggested' : 'confirmed'

  return (
    <>
      {person.data && !isGroup && (
        <SuggestedStrip
          personId={personId}
          sort={sort}
          order={order}
          mode={stripMode}
          onModeChange={setStripMode}
        />
      )}
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <ImageBrowser
          filter={{ kind: 'person', personId, state: gridState, sort, order }}
          showFaceCrops={gridMode === 'faces'}
          faceReview={{ personId }}
          viewerEnabled={!openedFromStrip(location.state)}
          header={
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              {person.data && (
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, px: 2, pt: 2, flexWrap: 'wrap' }}>
                  <NameForm key={person.data.id} personId={person.data.id} name={person.data.name} />
                  <Typography variant="body2" color="text.secondary">
                    {person.data.name === null
                      ? `${person.data.suggestedImageCount} suggested`
                      : `${person.data.confirmedImageCount} confirmed · ${person.data.suggestedImageCount} suggested`}
                  </Typography>
                  <Box sx={{ flex: 1 }} />
                  <FaceModeToggle value={gridMode} onChange={setGridMode} label="Grid view" />
                </Box>
              )}
              <GridHeader
                title={person.data?.name ?? 'Unknown person'}
                sort={sort}
                order={order}
              />
            </Box>
          }
          emptyState={
            <EmptyMessage>
              {isGroup ? 'No photos for this person.' : 'No confirmed photos yet.'}
            </EmptyMessage>
          }
        />
      </Box>
    </>
  )
}

export function PersonView() {
  const params = useParams()
  const personId = Number(params.personId)
  if (!Number.isInteger(personId) || personId <= 0) return <PersonNotFound />
  return <PersonContent personId={personId} />
}
