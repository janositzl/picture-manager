import FaceRetouchingNaturalIcon from '@mui/icons-material/FaceRetouchingNatural'
import SearchIcon from '@mui/icons-material/Search'
import { Box, Button, InputAdornment, TextField, Tooltip, Typography } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router'
import { usePermissions } from '../api/auth'
import { unknownLabels, usePeople, type PersonSummary } from '../api/people'
import { HEADING_SX } from '../design/accent'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { useFolderJobs } from '../tree/FolderJobsContext'
import { countText } from './countText'
import { PeopleDashboard } from './PeopleDashboard'
import { PersonAvatar } from './PersonAvatar'

type RowProps = { person: PersonSummary; label: string }

function PersonRow({ person, label }: RowProps) {
  const confirmed = person.name === null ? 0 : person.confirmedImageCount
  const tooltip =
    person.name === null
      ? `${person.suggestedImageCount} suggested`
      : `${confirmed} confirmed · ${person.suggestedImageCount} suggested`
  const hasSuggestions = person.suggestedImageCount > 0
  return (
    <Tooltip title={tooltip} placement="right" enterDelay={600}>
      <NavLink
        to={`/people/${person.id}`}
        aria-label={`${label}, ${tooltip}`}
        style={{ display: 'flex', alignItems: 'center', gap: 14, textDecoration: 'none', color: 'inherit' }}
        className={({ isActive }) =>
          `w-full rounded-xl px-2.5 py-2 no-underline transition-all duration-200 ease-in-out ${
            isActive
              ? 'bg-white shadow-sm ring-1 ring-zinc-200 dark:bg-zinc-800 dark:ring-zinc-700'
              : 'hover:bg-zinc-100/80 dark:hover:bg-zinc-800/60'
          }`
        }
      >
        <PersonAvatar faceId={person.coverFaceId} unknown={person.name === null} />
        <span
          style={{ textDecoration: 'none' }}
          className="min-w-0 flex-1 truncate text-sm font-medium leading-5 tracking-tight text-zinc-900 dark:text-zinc-100"
        >
          {label}
        </span>
        <span
          style={{ textDecoration: 'none' }}
          className={`shrink-0 rounded-full px-2 py-0.5 text-xs font-medium tabular-nums ${
            hasSuggestions
              ? 'bg-[#ecebfb] text-[#4b46c4] dark:bg-indigo-500/15 dark:text-indigo-300'
              : 'bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400'
          }`}
        >
          {countText(person)}
        </span>
      </NavLink>
    </Tooltip>
  )
}

type ListItem =
  | { kind: 'header'; title: string; count: number; first: boolean }
  | { kind: 'row'; row: RowProps }

const ROW_HEIGHT = 58
const HEADER_HEIGHT = 32
const SECTION_GAP = 20

function SectionHeader({ title, count }: { title: string; count: number }) {
  return (
    <div className="flex h-full items-end gap-2 px-2.5 pb-1.5">
      <h2 className="m-0 text-xs font-semibold uppercase tracking-wider text-zinc-500">{title}</h2>
      <span className="rounded-full bg-zinc-200/70 px-1.5 text-[11px] font-medium tabular-nums text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400">
        {count}
      </span>
    </div>
  )
}

/** People with suggestions waiting come first; otherwise the given order is kept. */
const pendingFirst = (rows: RowProps[]) =>
  [...rows].sort((a, b) => Number(b.person.suggestedImageCount > 0) - Number(a.person.suggestedImageCount > 0))

/** Section headers and rows in one virtualized list, so hundreds of groups stay cheap. */
function VirtualPeople({ named, unknown }: { named: RowProps[]; unknown: RowProps[] }) {
  const [scrollElement, setScrollElement] = useState<HTMLDivElement | null>(null)
  const items: ListItem[] = []
  for (const [title, rows] of [
    ['Named', named],
    ['Unknown', unknown],
  ] as const) {
    if (rows.length === 0) continue
    items.push({ kind: 'header', title, count: rows.length, first: items.length === 0 })
    for (const row of rows) items.push({ kind: 'row', row })
  }

  const itemAt = (index: number) => items[index] as ListItem

  const virtualizer = useVirtualizer({
    count: items.length,
    getScrollElement: () => scrollElement,
    estimateSize: (index) => {
      const item = itemAt(index)
      if (item.kind === 'row') return ROW_HEIGHT
      return HEADER_HEIGHT + (item.first ? 0 : SECTION_GAP)
    },
    getItemKey: (index) => {
      const item = itemAt(index)
      return item.kind === 'row' ? `p${item.row.person.id}` : `h${item.title}`
    },
    overscan: 8,
  })

  return (
    <div ref={setScrollElement} className="min-h-0 flex-1 overflow-y-auto px-3 pb-6">
      <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
        {virtualizer.getVirtualItems().map((virtualItem) => {
          const item = itemAt(virtualItem.index)
          return (
            <div
              key={virtualItem.key}
              style={{
                position: 'absolute',
                top: 0,
                left: 0,
                right: 0,
                height: virtualItem.size,
                transform: `translateY(${virtualItem.start}px)`,
              }}
            >
              {item.kind === 'header' ? (
                <div style={{ height: '100%', paddingTop: item.first ? 0 : SECTION_GAP }}>
                  <SectionHeader title={item.title} count={item.count} />
                </div>
              ) : (
                <PersonRow {...item.row} />
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}

function PeopleList() {
  const people = usePeople()
  const { activeJob, recognizeFaces } = useFolderJobs()
  const { canRunFolderActions } = usePermissions()
  const [search, setSearch] = useState('')
  const [needsReviewOnly, setNeedsReviewOnly] = useState(false)

  const all = people.data ?? []
  const labels = unknownLabels(all)
  const needle = search.trim().toLowerCase()
  const needsReview = all.filter((person) => person.suggestedImageCount > 0).length
  const rows = all
    .map((person) => ({ person, label: person.name ?? labels.get(person.id) ?? 'Unknown' }))
    .filter((row) => row.label.toLowerCase().includes(needle))
    .filter((row) => !needsReviewOnly || row.person.suggestedImageCount > 0)
  const named = pendingFirst(rows.filter((row) => row.person.name !== null))
  const unknown = pendingFirst(rows.filter((row) => row.person.name === null).sort((a, b) => a.person.id - b.person.id))
  const namedTotal = all.filter((person) => person.name !== null).length

  return (
    <aside
      aria-label="People"
      className="flex w-80 shrink-0 flex-col border-0 border-r border-solid border-zinc-200 bg-zinc-50/60 dark:border-zinc-800 dark:bg-zinc-950/40"
    >
      <div className="flex flex-col gap-4 p-5 pb-3">
        <div className="flex flex-col gap-0.5">
          <Typography variant="h5" component="h1" sx={HEADING_SX}>
            People
          </Typography>
          {people.data && people.data.length > 0 && (
            <p className="m-0 text-sm text-zinc-500">
              {namedTotal} named · {all.length - namedTotal} unknown
            </p>
          )}
        </div>
        <TextField
          size="small"
          placeholder="Search people…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          sx={{
            '& .MuiOutlinedInput-root': {
              borderRadius: '12px',
              bgcolor: 'background.paper',
              transition: 'all 200ms ease-in-out',
            },
          }}
          slotProps={{
            htmlInput: { 'aria-label': 'Search people' },
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
        />
        {(needsReview > 0 || needsReviewOnly) && (
          <button
            type="button"
            aria-pressed={needsReviewOnly}
            onClick={() => setNeedsReviewOnly(!needsReviewOnly)}
            className={`flex w-fit cursor-pointer items-center gap-1.5 rounded-full border border-solid px-3 py-1 text-xs font-medium transition-all duration-200 ease-in-out ${
              needsReviewOnly
                ? 'border-[#5b5bd6] bg-[#5b5bd6] text-white'
                : 'border-zinc-200 bg-white text-[#4b46c4] hover:bg-[#ecebfb] dark:border-zinc-700 dark:bg-zinc-900 dark:text-indigo-300'
            }`}
          >
            Needs review
            <span className="tabular-nums opacity-80">{needsReview}</span>
          </button>
        )}
        {canRunFolderActions && (
          <Button
            variant="outlined"
            size="small"
            startIcon={<FaceRetouchingNaturalIcon fontSize="small" />}
            disabled={activeJob !== null}
            onClick={() => recognizeFaces(null)}
            sx={{ borderRadius: '12px', textTransform: 'none', fontWeight: 500, py: 1 }}
          >
            Recognize faces in all libraries
          </Button>
        )}
      </div>
      <div className="flex min-h-0 flex-1 flex-col">
        {people.isError && (
          <div className="px-3">
            <QueryErrorAlert message="Couldn't load people." onRetry={() => void people.refetch()} />
          </div>
        )}
        {people.data?.length === 0 && (
          <div className="px-3">
            <EmptyMessage>
              No faces recognized yet. Scan your folders, then run face recognition from a folder&apos;s menu or
              with the button above.
            </EmptyMessage>
          </div>
        )}
        {people.data && people.data.length > 0 && rows.length === 0 && (
          <p className="m-0 px-5 text-sm text-zinc-500">No one matches.</p>
        )}
        <VirtualPeople named={named} unknown={unknown} />
      </div>
    </aside>
  )
}

/** People list on the left, the selected person (or a hint) on the right. */
export function PeoplePage() {
  return (
    <Box sx={{ display: 'flex', height: '100%' }}>
      <PeopleList />
      <Box sx={{ flex: 1, minWidth: 0, height: '100%', display: 'flex', flexDirection: 'column' }}>
        <Outlet />
      </Box>
    </Box>
  )
}

export function NoPersonSelected() {
  return <PeopleDashboard />
}
