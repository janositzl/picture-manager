import type { FolderDetail, FolderNode, ImageDetail, ImageListItem } from '../api/types'

export const devRoot: FolderNode = {
  id: 1,
  name: 'dev',
  hasChildren: true,
  imageCount: 0,
  isMissing: false,
}
export const holidays: FolderNode = {
  id: 2,
  name: 'Holidays',
  hasChildren: true,
  imageCount: 2,
  isMissing: false,
}
export const madeira: FolderNode = {
  id: 3,
  name: 'Madeira',
  hasChildren: false,
  imageCount: 3,
  isMissing: false,
}
export const old: FolderNode = {
  id: 4,
  name: 'Old',
  hasChildren: false,
  imageCount: 1,
  isMissing: true,
}

export const rootFolders: FolderNode[] = [devRoot]

export const childrenById: Record<number, FolderNode[]> = {
  1: [holidays],
  2: [madeira, old],
  3: [],
  4: [],
}

const crumb = (node: FolderNode) => ({ id: node.id, name: node.name })

export const folderDetails: Record<number, FolderDetail> = {
  1: {
    id: 1,
    name: 'dev',
    rootId: 1,
    rootName: 'dev',
    relativePath: '',
    imageCount: 0,
    isMissing: false,
    breadcrumb: [crumb(devRoot)],
  },
  2: {
    id: 2,
    name: 'Holidays',
    rootId: 1,
    rootName: 'dev',
    relativePath: 'Holidays',
    imageCount: 2,
    isMissing: false,
    breadcrumb: [crumb(devRoot), crumb(holidays)],
  },
  3: {
    id: 3,
    name: 'Madeira',
    rootId: 1,
    rootName: 'dev',
    relativePath: 'Holidays/Madeira',
    imageCount: 3,
    isMissing: false,
    breadcrumb: [crumb(devRoot), crumb(holidays), crumb(madeira)],
  },
  4: {
    id: 4,
    name: 'Old',
    rootId: 1,
    rootName: 'dev',
    relativePath: 'Holidays/Old',
    imageCount: 1,
    isMissing: true,
    breadcrumb: [crumb(devRoot), crumb(holidays), crumb(old)],
  },
}

const folderPaths: Record<number, string> = {
  1: 'dev',
  2: 'dev/Holidays',
  3: 'dev/Holidays/Madeira',
  4: 'dev/Holidays/Old',
}

export function image(
  id: number,
  folderId: number,
  overrides: Partial<ImageListItem> = {},
): ImageListItem {
  return {
    id,
    folderId,
    fileName: `IMG_${String(id).padStart(4, '0')}`,
    extension: '.jpg',
    width: 1200,
    height: 800,
    dateTaken: '2025-08-14T18:32:05',
    isFavorite: false,
    thumbnailUrl: `/api/images/${id}/thumbnail?v=H${id}`,
    previewUrl: `/api/images/${id}/preview?v=H${id}`,
    folderPath: folderPaths[folderId] ?? 'dev',
    ...overrides,
  }
}

export const holidaysImages: ImageListItem[] = [
  image(10, 2, { fileName: 'IMG_0001 copy' }),
  image(11, 2, { fileName: 'screenshot', extension: '.png' }),
]

export const madeiraImages: ImageListItem[] = [
  image(20, 3, { fileName: 'IMG_0001' }),
  image(21, 3, { fileName: 'IMG_0002', isFavorite: true }),
  image(22, 3, { fileName: 'IMG_0003' }),
]

export function imageDetail(item: ImageListItem): ImageDetail {
  return {
    ...item,
    fileSize: 2_400_000,
    fileModified: '2025-08-15T10:00:00Z',
    orientation: 1,
    cameraMake: 'Canon',
    cameraModel: 'EOS R6',
    lensModel: 'RF 24-105mm',
    latitude: 32.6669,
    longitude: -16.9241,
    rawMetadata: { Exif: { ISO: 100 } },
    albums: [{ id: 5, name: 'Best of 2025' }],
  }
}
