import { Alert, Box, Button, Chip, CircularProgress, FormControlLabel, Stack, Switch, Typography } from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import { useAssignFace, useFaceAction, useImageFaces, type FaceAction, type ImageFace } from '../api/people'
import { useNotify } from '../app/notify'
import { faceLabel } from './faceLabel'
import { PersonPicker } from './PersonPicker'
import type { FaceReview } from './useFaceReview'

const ICON: Record<ImageFace['state'], string> = { confirmed: '✓', suggested: '?', unknown: '❓', ignored: '⊘' }

/** The normal viewer's read-only view: only the people the user has confirmed in this photo. */
export function ConfirmedPeople({ imageId }: { imageId: number }) {
  const faces = useImageFaces(imageId, { confirmedOnly: true })
  const people = new Map<number, string>()
  for (const face of faces.data ?? [])
    if (face.personId !== null && face.personName !== null) people.set(face.personId, face.personName)
  if (people.size === 0) return null

  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="caption" sx={{ color: 'grey.400', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        People
      </Typography>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mt: 0.5 }}>
        {[...people].map(([id, name]) => (
          <Chip
            key={id}
            size="small"
            component={RouterLink}
            to={`/people/${id}`}
            clickable
            label={name}
            variant="outlined"
            sx={{ color: 'inherit', borderRadius: '6px', borderColor: 'rgba(255,255,255,0.2)' }}
          />
        ))}
      </Box>
    </Box>
  )
}

type Props = { review: FaceReview }

/** Face review: every detected face of the photo, with the decisions that can be made about the selected one. */
export function PeopleInPhoto({ review }: Props) {
  const faceAction = useFaceAction()
  const assign = useAssignFace()
  const notify = useNotify()
  const [picker, setPicker] = useState<'assign' | 'change' | null>(null)
  const selected = review.faces.find((f) => f.id === review.selectedId) ?? null

  const run = (face: ImageFace, action: FaceAction) =>
    faceAction.mutate({ faceId: face.id, action }, { onError: () => notify("Couldn't save that decision.") })

  let body
  if (review.isError) body = <Alert severity="error">Couldn't load faces.</Alert>
  else if (review.isLoading) body = <CircularProgress size={20} color="inherit" />
  else if (review.faces.length === 0) body = <Typography variant="body2">No faces detected.</Typography>
  else
    body = (
      <Stack spacing={0.5} component="ul" sx={{ m: 0, p: 0, listStyle: 'none' }}>
        {review.faces.map((face) => (
          <li key={face.id}>
            <Box
              component="button"
              type="button"
              aria-pressed={face.id === review.selectedId}
              onClick={() => review.select(face.id)}
              onMouseEnter={() => review.setHoveredId(face.id)}
              onMouseLeave={() => review.setHoveredId(null)}
              onFocus={() => review.setHoveredId(face.id)}
              onBlur={() => review.setHoveredId(null)}
              sx={{
                width: '100%',
                textAlign: 'left',
                color: 'inherit',
                font: 'inherit',
                px: 1,
                py: 0.5,
                border: 0,
                borderRadius: '6px',
                cursor: 'pointer',
                bgcolor:
                  face.id === review.selectedId
                    ? 'rgba(255,255,255,0.18)'
                    : face.id === review.hoveredId
                      ? 'rgba(255,255,255,0.1)'
                      : 'transparent',
              }}
            >
              {ICON[face.state]} {faceLabel(face)}
            </Box>
          </li>
        ))}
      </Stack>
    )

  return (
    <Box component="section" aria-label="People in photo" sx={{ mb: 2.5 }}>
      <Typography variant="caption" sx={{ color: 'grey.400', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        People in photo
      </Typography>
      <Box sx={{ mt: 0.5 }}>{body}</Box>
      {selected && (
        <Box sx={{ mt: 1.5 }}>
          <Typography variant="body2" sx={{ mb: 0.75 }}>
            {selected.state === 'ignored' ? 'Selected face: ignored' : `Selected face: ${faceLabel(selected)}`}
          </Typography>
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75 }}>
            {selected.state === 'suggested' && (
              <>
                <Button size="small" variant="contained" onClick={() => run(selected, 'accept')}>
                  Accept
                </Button>
                <Button size="small" variant="outlined" color="inherit" onClick={() => run(selected, 'reject')}>
                  Reject
                </Button>
              </>
            )}
            {(selected.state === 'suggested' || selected.state === 'confirmed') && (
              <Button size="small" variant="outlined" color="inherit" onClick={() => setPicker('change')}>
                Change person
              </Button>
            )}
            {selected.state === 'confirmed' && (
              <Button size="small" variant="outlined" color="inherit" onClick={() => run(selected, 'unknown')}>
                Mark as unknown
              </Button>
            )}
            {selected.state === 'unknown' && (
              <Button size="small" variant="contained" onClick={() => setPicker('assign')}>
                Assign person
              </Button>
            )}
            {selected.state !== 'ignored' && (
              <Button size="small" variant="outlined" color="inherit" onClick={() => run(selected, 'ignore')}>
                Ignore face
              </Button>
            )}
            {selected.state === 'ignored' && (
              <Button size="small" variant="contained" onClick={() => run(selected, 'restore')}>
                Restore face
              </Button>
            )}
          </Box>
        </Box>
      )}
      <FormControlLabel
        sx={{ mt: 1 }}
        control={
          <Switch size="small" checked={review.showIgnored} onChange={(event) => review.setShowIgnored(event.target.checked)} />
        }
        label={<Typography variant="body2">Show ignored faces</Typography>}
      />
      {picker !== null && selected && (
        <PersonPicker
          title={picker === 'assign' ? 'Assign person' : 'Change person'}
          excludeId={selected.state === 'confirmed' ? selected.personId : null}
          busy={assign.isPending}
          onClose={() => setPicker(null)}
          onPick={(target) =>
            assign.mutate(
              { faceId: selected.id, target },
              {
                onSuccess: () => setPicker(null),
                onError: () => notify("Couldn't assign the person."),
              },
            )
          }
        />
      )}
    </Box>
  )
}
