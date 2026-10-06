import { Box, Button, InputAdornment, TextField, Tooltip, Typography } from '@mui/material'
import FaceRetouchingNaturalIcon from '@mui/icons-material/FaceRetouchingNatural'
import HelpOutlineIcon from '@mui/icons-material/HelpOutlineOutlined'
import PersonOutlineIcon from '@mui/icons-material/PersonOutlined'
import SearchIcon from '@mui/icons-material/Search'
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router'
import { faceThumbnailUrl, unknownLabels, usePeople, type PersonSummary } from '../api/people'
import { HEADING_SX } from '../design/accent'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { countText } from './countText'
import { useFolderJobs } from '../tree/FolderJobsContext'

type RowProps = { person: PersonSummary; label: string }

function Avatar({ faceId, unknown }: { faceId: number | null; unknown: boolean }) {
  return faceId !== null ? (
    <img
      src={faceThumbnailUrl(faceId)}
      alt=""
      className="h-10 w-10 shrink-0 rounded-full object-cover ring-2 ring-white dark:ring-zinc-900"
      loading="lazy"
    />
  ) : (
    <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-zinc-100 text-zinc-400 ring-2 ring-white dark:bg-zinc-800 dark:text-zinc-500 dark:ring-zinc-900">
      {unknown ? <HelpOutlineIcon fontSize="small" /> : <PersonOutlineIcon fontSize="small" />}
    </div>
  )
}

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
        <Avatar faceId={person.coverFaceId} unknown={person.name === null} />
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

type SectionProps = { title: string; rows: RowProps[] }

function Section({ title, rows }: SectionProps) {
  if (rows.length === 0) return null
  return (
    <section className="flex w-full flex-col">
      <div className="mb-1.5 flex items-center gap-2 px-2.5">
        <h2 className="m-0 text-xs font-semibold uppercase tracking-wider text-zinc-500">{title}</h2>
        <span className="rounded-full bg-zinc-200/70 px-1.5 text-[11px] font-medium tabular-nums text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400">
          {rows.length}
        </span>
      </div>
      <div className="flex w-full flex-col gap-0.5">
        {rows.map((row) => (
          <PersonRow key={row.person.id} {...row} />
        ))}
      </div>
    </section>
  )
}

function PeopleList() {
  const people = usePeople()
  const { activeJob, recognizeFaces } = useFolderJobs()
  const [search, setSearch] = useState('')

  const all = people.data ?? []
  const labels = unknownLabels(all)
  const needle = search.trim().toLowerCase()
  const rows = all
    .map((person) => ({ person, label: person.name ?? labels.get(person.id) ?? 'Unknown' }))
    .filter((row) => row.label.toLowerCase().includes(needle))
  const named = rows.filter((row) => row.person.name !== null)
  const unknown = rows.filter((row) => row.person.name === null).sort((a, b) => a.person.id - b.person.id)
  const namedTotal = all.filter((person) => person.name !== null).length

  return (
    <aside
      aria-label="People"
      className="flex w-80 shrink-0 flex-col border-0 border-r border-solid border-zinc-200 bg-zinc-50/60 dark:border-zinc-800 dark:bg-zinc-950/40"
    >
      <div className="flex flex-col gap-4 p-5">
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
      </div>
      <div className="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-3 pb-6">
        {people.isError && (
          <QueryErrorAlert message="Couldn't load people." onRetry={() => void people.refetch()} />
        )}
        {people.data?.length === 0 && (
          <EmptyMessage>
            No faces recognized yet. Scan your folders, then run face recognition from a folder&apos;s menu or
            with the button above.
          </EmptyMessage>
        )}
        <Section title="Named" rows={named} />
        <Section title="Unknown" rows={unknown} />
        {people.data && people.data.length > 0 && rows.length === 0 && (
          <p className="m-0 px-2.5 text-sm text-zinc-500">No one matches.</p>
        )}
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
  return (
    <Box sx={{ p: 3 }}>
      <EmptyMessage>Select a person to see their photos.</EmptyMessage>
    </Box>
  )
}
