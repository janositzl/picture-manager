import AddIcon from '@mui/icons-material/Add'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import { CircularProgress, IconButton, InputAdornment, TextField } from '@mui/material'
import { useState, type FormEvent } from 'react'
import { usePeople, type AssignTarget } from '../api/people'
import { PersonAvatar } from '../people/PersonAvatar'

type Props = {
  title: string
  /** People who cannot be picked, e.g. the person the face is already confirmed as. */
  excludeId?: number | null
  busy?: boolean
  onPick: (target: AssignTarget) => void
  onClose: () => void
}

/**
 * Inline "who is this?" step: filter the named people, or type a name to create a new person.
 * Creating is the first option as soon as something is typed, and Enter picks the exact match or creates.
 */
export function PersonAssign({ title, excludeId = null, busy = false, onPick, onClose }: Props) {
  const people = usePeople()
  const [filter, setFilter] = useState('')
  const typed = filter.trim()
  const needle = typed.toLowerCase()
  const named = (people.data ?? []).filter((p) => p.name !== null && p.id !== excludeId)
  const visible = named.filter((p) => p.name!.toLowerCase().includes(needle))
  const exact = (people.data ?? []).find((p) => p.name?.toLowerCase() === needle)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (typed === '' || busy) return
    if (exact !== undefined && exact.id !== excludeId) onPick({ personId: exact.id })
    else if (exact === undefined) onPick({ name: typed })
  }

  return (
    <section aria-label={title} className="flex flex-col gap-3">
      <div className="flex items-center gap-1">
        <IconButton size="small" aria-label="Back" color="inherit" onClick={onClose}>
          <ArrowBackIcon fontSize="small" />
        </IconButton>
        <h3 className="m-0 text-sm font-semibold tracking-tight">{title}</h3>
      </div>
      <form onSubmit={submit}>
        <TextField
          label="Person name"
          placeholder="Search, or type a new name"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          autoFocus
          fullWidth
          size="small"
          slotProps={{
            input: {
              sx: { borderRadius: '12px', color: 'inherit' },
              endAdornment: people.isPending ? (
                <InputAdornment position="end">
                  <CircularProgress size={16} color="inherit" />
                </InputAdornment>
              ) : undefined,
            },
            inputLabel: { sx: { color: 'rgba(255,255,255,0.6)' } },
          }}
          sx={{ '& fieldset': { borderColor: 'rgba(255,255,255,0.25)' } }}
        />
      </form>
      {typed !== '' && exact === undefined && (
        <button
          type="button"
          disabled={busy}
          onClick={() => onPick({ name: typed })}
          className="flex w-full cursor-pointer items-center gap-3 rounded-xl border border-solid border-indigo-400/40 bg-indigo-500/15 px-3 py-2.5 text-left text-zinc-100 transition-all duration-200 ease-in-out hover:bg-indigo-500/25 disabled:cursor-default disabled:opacity-60"
        >
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-indigo-500 text-white">
            <AddIcon fontSize="small" />
          </span>
          <span className="min-w-0 flex-1 truncate text-sm font-medium">
            Create new person &ldquo;{typed}&rdquo;
          </span>
        </button>
      )}
      {typed === '' && (
        <p className="m-0 text-xs text-zinc-400">
          Pick someone below, or type a name to create a new person.
        </p>
      )}
      <ul aria-label="People" className="m-0 flex max-h-72 list-none flex-col gap-0.5 overflow-y-auto p-0">
        {visible.map((person) => (
          <li key={person.id}>
            <button
              type="button"
              disabled={busy}
              onClick={() => onPick({ personId: person.id })}
              className="flex w-full cursor-pointer items-center gap-3 rounded-xl border-0 bg-transparent px-3 py-2 text-left text-zinc-100 transition-all duration-200 ease-in-out hover:bg-white/10 disabled:cursor-default disabled:opacity-60"
            >
              <PersonAvatar faceId={person.coverFaceId} unknown={false} className="h-8 w-8" />
              <span className="min-w-0 flex-1 truncate text-sm">{person.name}</span>
              <span className="shrink-0 text-xs tabular-nums text-zinc-400">
                {person.confirmedImageCount} {person.confirmedImageCount === 1 ? 'photo' : 'photos'}
              </span>
            </button>
          </li>
        ))}
      </ul>
      {visible.length === 0 && typed === '' && !people.isPending && (
        <p className="m-0 text-sm text-zinc-400">No named people yet. Type a name to add the first one.</p>
      )}
    </section>
  )
}
