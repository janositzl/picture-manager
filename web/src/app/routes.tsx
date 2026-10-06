import { Navigate, type RouteObject } from 'react-router'
import { AdminLayout } from '../admin/AdminLayout'
import { RemovedFoldersPage } from '../admin/RemovedFoldersPage'
import { RootsPage } from '../admin/RootsPage'
import { SettingsPage } from '../admin/SettingsPage'
import { AlbumsPage } from '../albums/AlbumsPage'
import { AlbumView } from '../albums/AlbumView'
import { NoPersonSelected, PeoplePage } from '../people/PeoplePage'
import { PersonView } from '../people/PersonView'
import { DuplicatesView } from '../views/DuplicatesView'
import { FavoritesView } from '../views/FavoritesView'
import { FolderView } from '../views/FolderView'
import { RootRedirect } from '../views/RootRedirect'
import { SearchView } from '../views/SearchView'
import { AppShell } from './AppShell'

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <RootRedirect /> },
      { path: 'folders/:folderId', element: <FolderView /> },
      { path: 'favorites', element: <FavoritesView /> },
      { path: 'albums', element: <AlbumsPage /> },
      { path: 'albums/:albumId', element: <AlbumView /> },
      { path: 'duplicates', element: <DuplicatesView /> },
      {
        path: 'people',
        element: <PeoplePage />,
        children: [
          { index: true, element: <NoPersonSelected /> },
          { path: ':personId', element: <PersonView /> },
        ],
      },
      { path: 'search', element: <SearchView /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
  {
    path: '/admin',
    element: <AdminLayout />,
    children: [
      { index: true, element: <Navigate to="settings" replace /> },
      { path: 'settings', element: <SettingsPage /> },
      { path: 'roots', element: <RootsPage /> },
      { path: 'removed-folders', element: <RemovedFoldersPage /> },
    ],
  },
]
