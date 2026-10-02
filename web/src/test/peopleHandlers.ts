import { http, HttpResponse } from 'msw'

export type PersonDto = {
  id: number
  name: string | null
  faceCount: number
  photoCount: number
  coverFaceId: number | null
}

export const peopleFixture: [PersonDto, PersonDto] = [
  { id: 1, name: 'Anna', faceCount: 12, photoCount: 10, coverFaceId: 101 },
  { id: 2, name: null, faceCount: 5, photoCount: 5, coverFaceId: 102 },
]

export function peopleList(people: PersonDto[] = peopleFixture) {
  return http.get('/api/people', () => HttpResponse.json(people))
}

export function person(dto: PersonDto) {
  return http.get(`/api/people/${dto.id}`, () => HttpResponse.json(dto))
}
