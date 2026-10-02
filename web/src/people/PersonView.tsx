import { Box, Button, TextField, Typography } from '@mui/material'
import { useState, type FormEvent } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import { useNamePerson, usePerson } from '../api/people'
import { useNotify } from '../app/notify'
import { parseGridParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from '../views/GridHeader'
import { ImageBrowser } from '../views/ImageBrowser'

type NameFormProps = { personId: number; name: string | null }

function NameForm({ personId, name }: NameFormProps) {
  const [draft, setDraft] = useState(name ?? '')
  const namePerson = useNamePerson()
  const navigate = useNavigate()
  const notify = useNotify()

  const submit = (event: FormEvent) => {
    event.preventDefault()
    namePerson.mutate(
      { id: personId, name: draft },
      {
        onSuccess: (survivor) => {
          setDraft(survivor.name ?? '')
          if (survivor.id !== personId) navigate(`/people/${survivor.id}`, { replace: true })
        },
        onError: () => notify("Couldn't save the name."),
      },
    )
  }

  return (
    <form onSubmit={submit} className="flex items-center gap-2">
      <TextField
        size="small"
        label={name === null ? 'Name this person' : 'Name'}
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
      />
      <Button type="submit" variant="contained" disabled={draft.trim() === '' || namePerson.isPending}>
        Save name
      </Button>
    </form>
  )
}

export function PersonView() {
  const params = useParams()
  const personId = Number(params.personId)
  const person = usePerson(personId)
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)

  return (
    <ImageBrowser
      filter={{ kind: 'person', personId, sort, order }}
      header={
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          {person.data && (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, px: 2, pt: 2 }}>
              <NameForm key={person.data.id} personId={person.data.id} name={person.data.name} />
              <Typography variant="body2" color="text.secondary">
                {person.data.faceCount} faces · {person.data.photoCount} photos
              </Typography>
            </Box>
          )}
          <GridHeader title={person.data?.name ?? 'Unnamed person'} sort={sort} order={order} />
        </Box>
      }
      emptyState={<EmptyMessage>No photos for this person.</EmptyMessage>}
    />
  )
}