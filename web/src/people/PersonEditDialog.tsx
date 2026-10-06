import CloseIcon from '@mui/icons-material/Close'
import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined'
import HideSourceIcon from '@mui/icons-material/HideSource'
import { Button, Dialog, DialogContent, DialogTitle, IconButton, TextField } from '@mui/material'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useDeletePerson, useIgnoreGroup, useNamePerson, usePeople, type PersonSummary } from '../api/people'
import { useNotify } from '../app/notify'
import { ConfirmDialog } from '../shared/ConfirmDialog'

const imageCount = (person: PersonSummary) => person.confirmedImageCount + person.suggestedImageCount

type Pending = 'merge' | 'delete' | 'ignore' | null

type IgnoreProps = { person: PersonSummary; label: string; onDone: () => void; onClose: () => void }

/** Confirms, then ignores an unnamed group for good and returns to the people list. */
export function IgnoreGroupConfirm({ person, label, onDone, onClose }: IgnoreProps) {
  const ignoreGroup = useIgnoreGroup()
  const navigate = useNavigate()
  const notify = useNotify()
  const faces = person.suggestedImageCount

  return (
    <ConfirmDialog
      title="Ignore this group?"
      message={`Ignore ${label}? Its ${faces} ${faces === 1 ? 'photo is' : 'photos are'} hidden and these faces won't be suggested or grouped again. You can restore single faces from the photo viewer.`}
      confirmLabel="Ignore"
      busy={ignoreGroup.isPending}
      onConfirm={() =>
        ignoreGroup.mutate(person.id, {
          onSuccess: ({ count }) => {
            notify(`Ignored ${count} ${count === 1 ? 'face' : 'faces'}.`)
            onDone()
            navigate('/people', { replace: true })
          },
          onError: () => notify("Couldn't ignore the group."),
        })
      }
      onClose={onClose}
    />
  )
}

type Props = { person: PersonSummary; label: string; onClose: () => void }

/** Rename (or merge into an existing person), forget, or - for unnamed groups - ignore for good. */
export function PersonEditDialog({ person, label, onClose }: Props) {
  const isGroup = person.name === null
  const [draft, setDraft] = useState(person.name ?? '')
  const [mergeTarget, setMergeTarget] = useState<PersonSummary | null>(null)
  const [pending, setPending] = useState<Pending>(null)
  const people = usePeople()
  const namePerson = useNamePerson()
  const deletePerson = useDeletePerson()
  const navigate = useNavigate()
  const notify = useNotify()

  const save = () => {
    namePerson.mutate(
      { id: person.id, name: draft },
      {
        onSuccess: (survivor) => {
          onClose()
          if (survivor.id !== person.id) navigate(`/people/${survivor.id}`, { replace: true })
        },
        onError: () => notify("Couldn't save the name."),
      },
    )
  }

  // Another person's name merges this one into it for good, so ask first (only when the list is known).
  const submit = (event: FormEvent) => {
    event.preventDefault()
    const typed = draft.trim().toLowerCase()
    const existing = people.data?.find((p) => p.id !== person.id && p.name?.trim().toLowerCase() === typed)
    if (existing) {
      setMergeTarget(existing)
      setPending('merge')
    } else save()
  }

  const leave = () => {
    onClose()
    navigate('/people', { replace: true })
  }

  const confirmDelete = () => {
    setPending(null)
    deletePerson.mutate(person.id, {
      onSuccess: () => {
        notify(`Deleted ${label}.`)
        leave()
      },
      onError: () => notify("Couldn't delete the person."),
    })
  }

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth slotProps={{ paper: { sx: { borderRadius: '16px' } } }}>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', fontWeight: 600, letterSpacing: '-0.01em' }}>
        <span className="flex-1">Edit person</span>
        <IconButton size="small" aria-label="Close" onClick={onClose}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </DialogTitle>
      <DialogContent>
        <p className="m-0 mb-4 text-sm text-zinc-500">
          {isGroup
            ? `${person.suggestedImageCount} suggested`
            : `${person.confirmedImageCount} confirmed · ${person.suggestedImageCount} suggested`}
        </p>
        <form onSubmit={submit} className="flex items-center gap-2">
          <TextField
            size="small"
            fullWidth
            autoFocus
            label={isGroup ? 'Name this person' : 'Name'}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            sx={{ '& .MuiOutlinedInput-root': { borderRadius: '12px' } }}
          />
          <Button
            type="submit"
            variant="contained"
            disableElevation
            disabled={draft.trim() === '' || namePerson.isPending}
            sx={{ borderRadius: '12px', textTransform: 'none', fontWeight: 500, flexShrink: 0 }}
          >
            Save name
          </Button>
        </form>
        <div className="mt-6 flex flex-wrap gap-2 border-0 border-t border-solid border-zinc-200 pt-4 dark:border-zinc-800">
          {isGroup && (
            <Button
              color="error"
              variant="outlined"
              startIcon={<HideSourceIcon fontSize="small" />}
              onClick={() => setPending('ignore')}
              sx={{ borderRadius: '12px', textTransform: 'none', fontWeight: 500 }}
            >
              Ignore this group
            </Button>
          )}
          <Button
            color="error"
            variant="outlined"
            startIcon={<DeleteOutlinedIcon fontSize="small" />}
            disabled={deletePerson.isPending}
            onClick={() => setPending('delete')}
            sx={{ borderRadius: '12px', textTransform: 'none', fontWeight: 500 }}
          >
            Delete person
          </Button>
        </div>
      </DialogContent>
      {pending === 'merge' && mergeTarget && (
        <ConfirmDialog
          title="Merge people?"
          message={`Merge into existing '${mergeTarget.name}' (${imageCount(mergeTarget)} ${imageCount(mergeTarget) === 1 ? 'photo' : 'photos'})? This can't be undone.`}
          confirmLabel="Merge"
          onConfirm={() => {
            setPending(null)
            save()
          }}
          onClose={() => setPending(null)}
        />
      )}
      {pending === 'delete' && (
        <ConfirmDialog
          title="Delete person?"
          message={`Delete ${label}? Their faces become unassigned and may show up again as an unknown group after the next face recognition.`}
          confirmLabel="Delete"
          onConfirm={confirmDelete}
          onClose={() => setPending(null)}
        />
      )}
      {pending === 'ignore' && (
        <IgnoreGroupConfirm person={person} label={label} onDone={onClose} onClose={() => setPending(null)} />
      )}
    </Dialog>
  )
}
