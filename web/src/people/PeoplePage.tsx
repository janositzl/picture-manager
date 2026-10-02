import { Box, Button, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { faceThumbnailUrl, usePeople, type PersonSummary } from '../api/people'
import { HEADING_SX } from '../design/accent'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { useFolderJobs } from '../tree/FolderJobsContext'

type PersonCardProps = { person: PersonSummary }

function PersonCard({ person }: PersonCardProps) {
  const label = person.name ?? 'Unnamed'
  return (
    <RouterLink
      to={`/people/${person.id}`}
      aria-label={`${label}, ${person.photoCount} photos`}
      className="flex w-32 flex-col items-center gap-2 rounded-xl p-2 no-underline hover:bg-zinc-100 dark:hover:bg-zinc-800"
    >
      {person.coverFaceId !== null ? (
        <img
          src={faceThumbnailUrl(person.coverFaceId)}
          alt=""
          className="h-24 w-24 rounded-full object-cover"
          loading="lazy"
        />
      ) : (
        <div className="h-24 w-24 rounded-full bg-zinc-200 dark:bg-zinc-700" />
      )}
      <span className="truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">{label}</span>
      <span className="text-xs text-zinc-500">{person.photoCount} photos</span>
    </RouterLink>
  )
}

type SectionProps = { title: string; people: PersonSummary[] }

function Section({ title, people }: SectionProps) {
  if (people.length === 0) return null
  return (
    <Box component="section" sx={{ mb: 4 }}>
      <Typography variant="h6" component="h2" sx={{ ...HEADING_SX, mb: 1 }}>
        {title}
      </Typography>
      <div className="flex flex-wrap gap-2">
        {people.map((person) => (
          <PersonCard key={person.id} person={person} />
        ))}
      </div>
    </Box>
  )
}

export function PeoplePage() {
  const people = usePeople()
  const { activeJob, recognizeFaces } = useFolderJobs()

  return (
    <Box sx={{ p: 3, overflow: 'auto', height: '100%' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 3 }}>
        <Typography variant="h5" component="h1" sx={HEADING_SX}>
          People
        </Typography>
        <Button variant="outlined" disabled={activeJob !== null} onClick={() => recognizeFaces(null)}>
          Recognize faces in all libraries
        </Button>
      </Box>
      {people.isError && (
        <QueryErrorAlert message="Couldn't load people." onRetry={() => void people.refetch()} />
      )}
      {people.data?.length === 0 && (
        <EmptyMessage>
          No faces recognized yet. Scan your folders, then run face recognition from a folder&apos;s menu or
          with the button above.
        </EmptyMessage>
      )}
      {people.data && (
        <>
          <Section title="Named people" people={people.data.filter((p) => p.name !== null)} />
          <Section title="Unnamed groups" people={people.data.filter((p) => p.name === null)} />
        </>
      )}
    </Box>
  )
}