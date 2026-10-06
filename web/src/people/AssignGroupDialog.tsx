import CloseIcon from '@mui/icons-material/Close'
import { Dialog, DialogContent, DialogTitle, IconButton, TextField } from '@mui/material'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useAssignGroup, usePeople, type PersonSummary } from '../api/people'
import { useNotify } from '../app/notify'
import { ConfirmDialog } from '../shared/ConfirmDialog'
import { countText } from './countText'
import { PersonAvatar } from './PersonAvatar'

type Props = { group: PersonSummary; label: string; onClose: () => void }

/** Pick a named person to merge an unknown group into; its faces land in that person's confirmed photos. */
export function AssignGroupDialog({ group, label, onClose }: Props) {
  const people = usePeople()
  const assignGroup = useAssignGroup()
  const navigate = useNavigate()
  const notify = useNotify()
  const [filter, setFilter] = useState('')
  const [target, setTarget] = useState<PersonSummary | null>(null)
  const needle = filter.trim().toLowerCase()
  const visible = (people.data ?? [])
    .filter((p) => p.name !== null && p.name.toLowerCase().includes(needle))
    .sort((a, b) => a.name!.localeCompare(b.name!, undefined, { sensitivity: 'base' }))
  const photos = group.suggestedImageCount

  const confirm = () => {
    if (target === null) return
    assignGroup.mutate(
      { id: group.id, personId: target.id },
      {
        onSuccess: (survivor) => {
          notify(`Assigned ${label} to ${survivor.name}.`)
          onClose()
          navigate(`/people/${survivor.id}`, { replace: true })
        },
        onError: () => {
          setTarget(null)
          notify("Couldn't assign the group.")
        },
      },
    )
  }

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth slotProps={{ paper: { sx: { borderRadius: '16px' } } }}>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', fontWeight: 600, letterSpacing: '-0.01em' }}>
        <span className="flex-1">Assign to person</span>
        <IconButton size="small" aria-label="Close" onClick={onClose}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </DialogTitle>
      <DialogContent>
        <TextField
          size="small"
          fullWidth
          autoFocus
          label="Search people"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          sx={{ mb: 1, '& .MuiOutlinedInput-root': { borderRadius: '12px' } }}
        />
        <ul className="m-0 flex max-h-80 list-none flex-col gap-1 overflow-y-auto p-0">
          {visible.map((person) => (
            <li key={person.id}>
              <button
                type="button"
                onClick={() => setTarget(person)}
                className="flex w-full cursor-pointer items-center gap-3 rounded-xl border-0 bg-transparent px-2 py-2 text-left text-inherit transition-all duration-200 ease-in-out hover:bg-zinc-100 dark:hover:bg-zinc-800"
              >
                <PersonAvatar faceId={person.coverFaceId} unknown={false} className="h-8 w-8" />
                <span className="min-w-0 flex-1 truncate text-sm font-medium">{person.name}</span>
                <span className="text-xs text-zinc-500">{countText(person)}</span>
              </button>
            </li>
          ))}
          {visible.length === 0 && <li className="px-2 py-3 text-sm text-zinc-500">No matching people.</li>}
        </ul>
      </DialogContent>
      {target !== null && (
        <ConfirmDialog
          title="Assign to person?"
          message={`Assign ${label} (${photos} ${photos === 1 ? 'photo' : 'photos'}) to '${target.name}' as confirmed? This can't be undone.`}
          confirmLabel="Assign"
          busy={assignGroup.isPending}
          onConfirm={confirm}
          onClose={() => setTarget(null)}
        />
      )}
    </Dialog>
  )
}
