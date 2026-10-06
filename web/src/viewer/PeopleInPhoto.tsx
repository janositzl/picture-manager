import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  FormControlLabel,
  Switch,
  Tooltip,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import {
  faceThumbnailUrl,
  useAssignFace,
  useFaceAction,
  useImageFaces,
  useRecheckFaces,
  type FaceAction,
  type ImageFace,
} from '../api/people'
import type { ImageListItem } from '../api/types'
import { useNotify } from '../app/notify'
import { faceLabel } from './faceLabel'
import { PersonAssign } from './PersonAssign'
import type { FaceReview } from './useFaceReview'

/** The normal viewer's read-only view: only the people the user has confirmed in this photo. */
export function ConfirmedPeople({ imageId }: { imageId: number }) {
  const faces = useImageFaces(imageId, { confirmedOnly: true })
  const people = new Map<number, string>()
  for (const face of faces.data ?? [])
    if (face.personId !== null && face.personName !== null)
      people.set(face.personId, face.personName)
  if (people.size === 0) return null

  return (
    <Box sx={{ mb: 2 }}>
      <Typography
        variant="caption"
        sx={{ color: 'grey.400', textTransform: 'uppercase', letterSpacing: '0.04em' }}
      >
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

type Props = {
  review: FaceReview
  item: Pick<ImageListItem, 'id' | 'fileName' | 'extension' | 'folderPath'>
}

const DOT: Record<ImageFace['state'], string> = {
  confirmed: 'bg-green-500',
  suggested: 'bg-amber-400',
  unknown: 'bg-red-400',
  ignored: 'bg-zinc-500',
}

const STATE_TEXT: Record<ImageFace['state'], string> = {
  confirmed: 'Confirmed',
  suggested: 'Suggested by AI',
  unknown: 'Not identified yet',
  ignored: 'Ignored',
}

function FaceCrop({ face, className }: { face: ImageFace; className: string }) {
  return (
    <img
      src={faceThumbnailUrl(face.id)}
      alt=""
      loading="lazy"
      className={`${className} shrink-0 rounded-full bg-zinc-800 object-cover`}
    />
  )
}

/** What this photo is: its name and full path. Everything else lives in the Info panel. */
function PhotoHeader({ item }: { item: Props['item'] }) {
  return (
    <div className="min-w-0">
      <p className="m-0 break-all text-sm font-semibold tracking-tight">
        {item.fileName}
        {item.extension}
      </p>
      <p className="m-0 mt-0.5 break-all text-xs text-zinc-400">{item.folderPath}</p>
    </div>
  )
}

const ghostButtonSx = { textTransform: 'none', fontWeight: 500, borderRadius: '10px' } as const

/**
 * The face-review side panel (opened from a person): the photo's name and path, a row per detected face,
 * and the decisions that can be made about the selected one. It is its own panel, apart from Info.
 */
export function PeopleInPhoto({ review, item }: Props) {
  const faceAction = useFaceAction()
  const assign = useAssignFace()
  const recheck = useRecheckFaces()
  const notify = useNotify()
  const [picker, setPicker] = useState<'assign' | 'change' | null>(null)
  const selected = review.faces.find((f) => f.id === review.selectedId) ?? null
  const who = selected?.personName ?? 'this person'

  // After a decision, move on to the next face that still needs one.
  const advance = (face: ImageFace) => {
    const index = review.faces.findIndex((f) => f.id === face.id)
    const next = review.faces
      .slice(index + 1)
      .find((f) => f.state === 'suggested' || f.state === 'unknown')
    if (next !== undefined) review.select(next.id)
  }

  const run = (face: ImageFace, action: FaceAction) =>
    faceAction.mutate(
      { faceId: face.id, action },
      {
        onSuccess: () => {
          if (action === 'accept' || action === 'reject' || action === 'ignore') advance(face)
        },
        onError: () => notify("Couldn't save that decision."),
      },
    )

  let list
  if (review.isError) list = <Alert severity="error">Couldn't load faces.</Alert>
  else if (review.isLoading) list = <CircularProgress size={20} color="inherit" />
  else if (review.faces.length === 0)
    list = <p className="m-0 text-sm text-zinc-400">No faces detected.</p>
  else
    list = (
      <ul className="m-0 flex list-none flex-col gap-1 p-0">
        {review.faces.map((face) => {
          const active = face.id === review.selectedId
          return (
            <li key={face.id}>
              <button
                type="button"
                aria-pressed={active}
                onClick={() => review.select(face.id)}
                onMouseEnter={() => review.setHoveredId(face.id)}
                onMouseLeave={() => review.setHoveredId(null)}
                onFocus={() => review.setHoveredId(face.id)}
                onBlur={() => review.setHoveredId(null)}
                className={`flex w-full cursor-pointer items-center gap-3 rounded-xl border border-solid px-2.5 py-2 text-left text-zinc-100 transition-all duration-200 ease-in-out ${
                  active
                    ? 'border-white/25 bg-white/15'
                    : face.id === review.hoveredId
                      ? 'border-transparent bg-white/10'
                      : 'border-transparent bg-transparent hover:bg-white/10'
                }`}
              >
                <FaceCrop face={face} className="h-9 w-9" />
                <span className="min-w-0 flex-1 truncate text-sm">{faceLabel(face)}</span>
                <span
                  aria-hidden
                  className={`h-2.5 w-2.5 shrink-0 rounded-full ${DOT[face.state]}`}
                />
              </button>
            </li>
          )
        })}
      </ul>
    )

  return (
    <Box
      component="aside"
      aria-label="Face review"
      sx={{
        width: 340,
        flexShrink: 0,
        overflowY: 'auto',
        p: 2.5,
        bgcolor: 'grey.900',
        color: 'grey.100',
        borderLeft: '1px solid',
        borderColor: 'rgba(255,255,255,0.08)',
      }}
    >
      <section aria-label="People in photo" className="flex flex-col gap-5">
        <PhotoHeader item={item} />
        {picker !== null && selected ? (
          <PersonAssign
            title={picker === 'assign' ? 'Assign person' : 'Change person'}
            excludeId={selected.state === 'confirmed' ? selected.personId : null}
            busy={assign.isPending}
            onClose={() => setPicker(null)}
            onPick={(target) =>
              assign.mutate(
                { faceId: selected.id, target },
                {
                  onSuccess: () => {
                    setPicker(null)
                    advance(selected)
                  },
                  onError: () => notify("Couldn't assign the person."),
                },
              )
            }
          />
        ) : (
          <>
            <div className="flex flex-col gap-2">
              <h2 className="m-0 flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-zinc-400">
                People in photo
                {review.faces.length > 0 && (
                  <span className="rounded-full bg-white/10 px-1.5 text-[11px] tabular-nums">
                    {review.faces.length}
                  </span>
                )}
              </h2>
              {list}
              {review.faces.some((f) => f.state === 'unknown') && (
                <Tooltip
                  describeChild
                  title="Try again to match the unidentified faces in this photo to people you have confirmed since."
                >
                  <span className="self-start">
                    <Button
                      size="small"
                      variant="outlined"
                      color="inherit"
                      disabled={recheck.isPending}
                      sx={ghostButtonSx}
                      onClick={() =>
                        recheck.mutate(item.id, {
                          onSuccess: ({ count }) =>
                            notify(
                              count === 0
                                ? 'No new matches.'
                                : `${count} ${count === 1 ? 'face' : 'faces'} now suggested.`,
                            ),
                          onError: () => notify("Couldn't re-check the faces."),
                        })
                      }
                    >
                      Re-check faces
                    </Button>
                  </span>
                </Tooltip>
              )}
            </div>
            {selected && (
              <div className="flex flex-col gap-3 rounded-2xl border border-solid border-white/10 bg-white/5 p-3">
                <div className="flex items-center gap-3">
                  <FaceCrop face={selected} className="h-14 w-14" />
                  <div className="min-w-0">
                    <p className="m-0 truncate text-sm font-semibold">{faceLabel(selected)}</p>
                    <p className="m-0 text-xs text-zinc-400">{STATE_TEXT[selected.state]}</p>
                  </div>
                </div>
                <div className="flex flex-wrap gap-2">
                  {selected.state === 'suggested' && (
                    <>
                      <Tooltip describeChild title={`Confirm this face is ${who}.`}>
                        <Button
                          size="small"
                          variant="contained"
                          disableElevation
                          sx={ghostButtonSx}
                          onClick={() => run(selected, 'accept')}
                        >
                          Accept
                        </Button>
                      </Tooltip>
                      <Tooltip
                        describeChild
                        title={`Not ${who}. The face goes back to Unknown, and ${who} won't be suggested for it again. Other faces and photos are unchanged.`}
                      >
                        <Button
                          size="small"
                          variant="outlined"
                          color="inherit"
                          sx={ghostButtonSx}
                          onClick={() => run(selected, 'reject')}
                        >
                          Reject
                        </Button>
                      </Tooltip>
                    </>
                  )}
                  {selected.state === 'unknown' && (
                    <Tooltip describeChild title={'Choose who this is, or create a new person.'}>
                      <Button
                        size="small"
                        variant="contained"
                        disableElevation
                        sx={ghostButtonSx}
                        onClick={() => setPicker('assign')}
                      >
                        Assign person
                      </Button>
                    </Tooltip>
                  )}
                  {selected.state === 'ignored' && (
                    <Tooltip
                      describeChild
                      title={'Bring this face back as Unknown so it can be named or grouped again.'}
                    >
                      <Button
                        size="small"
                        variant="contained"
                        disableElevation
                        sx={ghostButtonSx}
                        onClick={() => run(selected, 'restore')}
                      >
                        Restore face
                      </Button>
                    </Tooltip>
                  )}
                  {(selected.state === 'suggested' || selected.state === 'confirmed') && (
                    <Tooltip describeChild title={'Pick a different person for this face.'}>
                      <Button
                        size="small"
                        variant="outlined"
                        color="inherit"
                        sx={ghostButtonSx}
                        onClick={() => setPicker('change')}
                      >
                        Change person
                      </Button>
                    </Tooltip>
                  )}
                  {selected.state === 'confirmed' && (
                    <Tooltip describeChild title={'Remove the name from this face. It goes back to Unknown.'}>
                      <Button
                        size="small"
                        variant="outlined"
                        color="inherit"
                        sx={ghostButtonSx}
                        onClick={() => run(selected, 'unknown')}
                      >
                        Mark as unknown
                      </Button>
                    </Tooltip>
                  )}
                  {selected.state !== 'ignored' && (
                    <Tooltip
                      describeChild
                      title={
                        'Not a person to track (poster, framed picture, stranger). Ignores this face only; other faces and photos are unchanged. Undo with Show ignored faces, then Restore.'
                      }
                    >
                      <Button
                        size="small"
                        color="inherit"
                        sx={{ ...ghostButtonSx, opacity: 0.75 }}
                        onClick={() => run(selected, 'ignore')}
                      >
                        Ignore face
                      </Button>
                    </Tooltip>
                  )}
                </div>
              </div>
            )}
            <FormControlLabel
              sx={{ m: 0 }}
              control={
                <Switch
                  size="small"
                  checked={review.showIgnored}
                  onChange={(event) => review.setShowIgnored(event.target.checked)}
                />
              }
              label={<Typography variant="body2">Show ignored faces</Typography>}
            />
          </>
        )}
      </section>
    </Box>
  )
}
