import { Box, Button, InputAdornment, TextField, Tooltip, Typography } from '@mui/material'
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

function Avatar({ faceId }: { faceId: number | null }) {
  return faceId !== null ? (
    <img src={faceThumbnailUrl(faceId)} alt="" className="h-8 w-8 shrink-0 rounded-full object-cover" loading="lazy" />
  ) : (
    <div className="h-8 w-8 shrink-0 rounded-full bg-zinc-200 dark:bg-zinc-700" />
  )
}

function PersonRow({ person, label }: RowProps) {
  const confirmed = person.name === null ? 0 : person.confirmedImageCount
  const tooltip =
    person.name === null
      ? `${person.suggestedImageCount} suggested`
      : `${confirmed} confirmed · ${person.suggestedImageCount} suggested`
  return (
    <Tooltip title={tooltip} placement="right" enterDelay={600}>
      <NavLink
        to={`/people/${person.id}`}
        aria-label={`${label}, ${tooltip}`}
        className={({ isActive }) =>
          `flex items-center gap-2 rounded-lg px-2 py-1.5 no-underline transition-colors duration-200 hover:bg-zinc-100 dark:hover:bg-zinc-800 ${
            isActive ? 'bg-zinc-100 dark:bg-zinc-800' : ''
          }`
        }
      >
        <Avatar faceId={person.coverFaceId} />
        <span className="min-w-0 flex-1 truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">
          {person.name === null ? `❓ ${label}` : label}
        </span>
        <span className="text-xs text-zinc-500">{countText(person)}</span>
      </NavLink>
    </Tooltip>
  )
}

type SectionProps = { title: string; rows: RowProps[] }

function Section({ title, rows }: SectionProps) {
  if (rows.length === 0) return null
  return (
    <Box component="section" sx={{ mb: 2 }}>
      <Typography variant="overline" component="h2" color="text.secondary" sx={{ px: 1 }}>
        {title}
      </Typography>
      {rows.map((row) => (
        <PersonRow key={row.person.id} {...row} />
      ))}
    </Box>
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

  return (
    <Box
      component="aside"
      aria-label="People"
      sx={{ width: 280, flexShrink: 0, display: 'flex', flexDirection: 'column', borderRight: 1, borderColor: 'divider' }}
    >
      <Box sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <Typography variant="h5" component="h1" sx={HEADING_SX}>
          People
        </Typography>
        <TextField
          size="small"
          placeholder="Search…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
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
        <Button variant="outlined" size="small" disabled={activeJob !== null} onClick={() => recognizeFaces(null)}>
          Recognize faces in all libraries
        </Button>
      </Box>
      <Box sx={{ flex: 1, overflowY: 'auto', px: 1, pb: 2 }}>
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
          <Typography variant="body2" color="text.secondary" sx={{ px: 1 }}>
            No one matches.
          </Typography>
        )}
      </Box>
    </Box>
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
