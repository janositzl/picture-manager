import ClearIcon from '@mui/icons-material/Clear'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined'
import {
  Badge,
  Button,
  IconButton,
  InputAdornment,
  Skeleton,
  TextField,
  Tooltip,
} from '@mui/material'
import { useState } from 'react'
import { usePersonCompanions, type PersonCompanion } from '../api/people'
import { EmptyMessage } from '../shared/EmptyMessage'
import { foldText } from '../shared/foldText'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PersonAvatar } from './PersonAvatar'

type Props = {
  personId: number
  label: string
  /** Which of the person's photos the list is based on: what the grid beside it shows. */
  state: 'confirmed' | 'suggested'
  /** People the grid is filtered by right now. */
  applied: number[]
  onApply: (personIds: number[]) => void
}

const sameIds = (a: readonly number[], b: readonly number[]) =>
  a.length === b.length && a.every((id) => b.includes(id))

type RowProps = { companion: PersonCompanion; selected: boolean; onToggle: () => void }

function CompanionRow({ companion, selected, onToggle }: RowProps) {
  const photos =
    companion.sharedImageCount === 1 ? '1 photo' : `${companion.sharedImageCount} photos`
  return (
    <button
      type="button"
      aria-pressed={selected}
      aria-label={`${companion.name}, ${photos} together`}
      onClick={onToggle}
      className={`flex w-full cursor-pointer items-center gap-3.5 rounded-xl border-0 px-2.5 py-2 text-left transition-all duration-200 ease-in-out ${
        selected
          ? 'bg-[#ecebfb] ring-1 ring-[#5b5bd6] dark:bg-indigo-500/15 dark:ring-indigo-400'
          : 'bg-transparent hover:bg-zinc-100/80 dark:hover:bg-zinc-800/60'
      }`}
    >
      <PersonAvatar faceId={companion.coverFaceId} unknown={false} />
      <span className="min-w-0 flex-1 truncate text-sm font-medium leading-5 tracking-tight text-zinc-900 dark:text-zinc-100">
        {companion.name}
      </span>
      <span className="shrink-0 rounded-full bg-zinc-100 px-2 py-0.5 text-xs font-medium tabular-nums text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400">
        {companion.sharedImageCount}
      </span>
    </button>
  )
}

/**
 * Collapsible list of the other people who appear in photos with this person. Nothing is requested until it is
 * expanded; ticking people changes nothing until Filter is pressed.
 */
export function CompanionsPanel({ personId, label, state, applied, onApply }: Props) {
  const [expanded, setExpanded] = useState(applied.length > 0)
  const [pending, setPending] = useState<number[]>(applied)
  const [search, setSearch] = useState('')
  const companions = usePersonCompanions(personId, state, expanded)
  // Alphabetical; the name filter only hides rows, so ticked people stay ticked while hidden.
  const needle = foldText(search.trim())
  const visible = [...(companions.data ?? [])]
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) || a.id - b.id)
    .filter((companion) => foldText(companion.name).includes(needle))

  const toggle = (id: number) =>
    setPending((current) =>
      current.includes(id) ? current.filter((other) => other !== id) : [...current, id],
    )
  const canFilter = pending.length > 0 && !sameIds(pending, applied)

  if (!expanded) {
    return (
      <div className="flex w-12 shrink-0 flex-col items-center border-0 border-l border-solid border-zinc-200 pt-3 dark:border-zinc-800">
        <Tooltip title={`Show people together with ${label}`} placement="left">
          <IconButton
            aria-label={`Show people together with ${label}`}
            aria-expanded={false}
            onClick={() => setExpanded(true)}
            className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
          >
            <Badge color="primary" badgeContent={applied.length}>
              <GroupsOutlinedIcon fontSize="small" />
            </Badge>
          </IconButton>
        </Tooltip>
      </div>
    )
  }

  return (
    <aside
      aria-label={`People together with ${label}`}
      className="flex w-72 shrink-0 flex-col border-0 border-l border-solid border-zinc-200 bg-zinc-50/60 dark:border-zinc-800 dark:bg-zinc-950/40"
    >
      <div className="flex items-center gap-1 px-4 pb-2 pt-3">
        <h2 className="m-0 min-w-0 flex-1 truncate text-sm font-semibold tracking-tight">
          Together with {label}
        </h2>
        <Tooltip title="Hide">
          <IconButton
            size="small"
            aria-label="Hide people together with this person"
            aria-expanded
            onClick={() => setExpanded(false)}
            className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
          >
            <ChevronRightIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </div>
      <div className="px-3 pb-2">
        <TextField
          size="small"
          fullWidth
          placeholder="Filter by name…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          slotProps={{
            input: {
              endAdornment: search !== '' && (
                <InputAdornment position="end">
                  <IconButton
                    size="small"
                    edge="end"
                    aria-label="Clear name filter"
                    onClick={() => setSearch('')}
                    sx={{ p: 0.25 }}
                  >
                    <ClearIcon sx={{ fontSize: 16 }} />
                  </IconButton>
                </InputAdornment>
              ),
            },
            htmlInput: { 'aria-label': 'Filter people by name', style: { padding: '4px 10px' } },
          }}
          sx={{
            '& .MuiOutlinedInput-root': {
              borderRadius: '8px',
              fontSize: 13,
              bgcolor: 'background.paper',
            },
          }}
        />
      </div>
      <div className="flex min-h-0 flex-1 flex-col gap-0.5 overflow-y-auto px-3 pb-3">
        {companions.isPending && (
          <>
            {[0, 1, 2, 3].map((n) => (
              <Skeleton key={n} variant="rounded" height={56} sx={{ borderRadius: '12px' }} />
            ))}
          </>
        )}
        {companions.isError && (
          <QueryErrorAlert
            message="Couldn't load people."
            onRetry={() => void companions.refetch()}
          />
        )}
        {companions.data?.length === 0 && (
          <EmptyMessage>No one else appears in these photos.</EmptyMessage>
        )}
        {companions.data && companions.data.length > 0 && visible.length === 0 && (
          <EmptyMessage>No one matches.</EmptyMessage>
        )}
        {visible.map((companion) => (
          <CompanionRow
            key={companion.id}
            companion={companion}
            selected={pending.includes(companion.id)}
            onToggle={() => toggle(companion.id)}
          />
        ))}
      </div>
      <div className="flex items-center gap-2 border-0 border-t border-solid border-zinc-200 p-3 dark:border-zinc-800">
        <Button
          variant="contained"
          size="small"
          disableElevation
          disabled={!canFilter}
          onClick={() => onApply([...pending].sort((a, b) => a - b))}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
        >
          Filter{pending.length > 0 ? ` (${pending.length})` : ''}
        </Button>
        <Button
          size="small"
          disabled={pending.length === 0}
          onClick={() => setPending([])}
          sx={{ borderRadius: '8px', textTransform: 'none' }}
        >
          Clear
        </Button>
        <div className="flex-1" />
        {applied.length > 0 && (
          <Button
            size="small"
            onClick={() => onApply([])}
            sx={{ borderRadius: '8px', textTransform: 'none' }}
          >
            Remove filter
          </Button>
        )}
      </div>
    </aside>
  )
}
