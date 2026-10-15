# PictureManager — Recommended Technology Stack

## 1. Overview

PictureManager is a personal web application for organizing images stored on a Windows NAS.

The NAS is the **source of truth for the physical image files**. The application database stores the image catalog, metadata, albums, favorites, and scan information.

The recommended architecture is a **modular monolith** initially, rather than microservices.

---

## 2. Recommended Technology Stack

| Area | Technology | Purpose |
|---|---|---|
| Backend | **ASP.NET Core / .NET 10** | Main application and REST API |
| API | **ASP.NET Core Web API** | Communication with React |
| Frontend | **React + TypeScript** | Image browser and management UI |
| Frontend build | **Vite** | Fast development/build tooling |
| UI framework | **MUI (Material UI)** | React UI components |
| Database | **PostgreSQL** | Catalog and application data |
| ORM | **EF Core 10** | Database access |
| EXIF/metadata | **MetadataExtractor** | EXIF, IPTC, XMP metadata |
| Image processing | **ImageSharp** | Dimensions, orientation, thumbnails |
| Background processing | **ASP.NET Core BackgroundService** | Image scanning |
| Storage | **Windows NAS mounted filesystem** | Original images |
| Containers | **Docker** | Application deployment |
| Reverse proxy | **Nginx** or ASP.NET Core | HTTP entry point |
| API documentation | **OpenAPI / Swagger** | API development/testing |
| Logging | **Serilog** | Structured application/scanner logging |
| Unit/integration testing | **xUnit + FluentAssertions** | Backend testing |
| E2E testing | **Playwright** | Browser/application testing |
| Source control | **Git + Bitbucket** | Source management |

---

## 3. High-Level Architecture

```text
                         Browser
                            |
                            v
                    +---------------+
                    | React + TS    |
                    | Vite + MUI    |
                    +-------+-------+
                            |
                           HTTP
                            |
                            v
+---------------------------------------------------+
|              ASP.NET Core / .NET 10              |
|                                                   |
|  +-------------+                                  |
|  | REST API    |                                  |
|  +------+------+                                  |
|         |                                         |
|         v                                         |
|  +-------------+    +--------------------------+  |
|  | Application |    | Background Scanner       |  |
|  +------+------+    | BackgroundService        |  |
|         |           +------------+-------------+  |
|         v                        |                |
|  +-------------+                 |                |
|  | Domain      |                 |                |
|  +-------------+                 |                |
|         |                        |                |
|         v                        v                |
|  +---------------------------------------------+  |
|  | Infrastructure                              |  |
|  |                                             |  |
|  | EF Core | SQL Server | NAS | Metadata |     |  |
|  | ImageSharp | File Storage                   |  |
|  +---------------------------------------------+  |
+----------------------+----------------------------+
                       |
             +---------+---------+
             |                   |
             v                   v
      +-------------+     +--------------+
      | SQL Server  |     | Windows NAS  |
      |             |     | /Images      |
      | Catalog     |     |              |
      | Albums      |     | Original     |
      | Favorites   |     | images       |
      | Scans       |     |              |
      +-------------+     +--------------+
```

---

## 4. Backend Architecture

Use a modular-monolith structure:

```text
PictureManager/
|
+-- PictureManager.Api/
|     REST API
|
+-- PictureManager.Application/
|     Use cases
|     DTOs
|     Application services
|
+-- PictureManager.Domain/
|     Entities
|     Domain rules
|
+-- PictureManager.Infrastructure/
|     EF Core
|     SQL Server
|     NAS storage
|     MetadataExtractor
|     ImageSharp
|
+-- picturemanager.client/
|     React + TypeScript
|
+-- tests/
      Unit tests
      Integration tests
      E2E tests
```

### Why modular monolith?

For the first version, microservices would add infrastructure and deployment complexity without providing much benefit.

The application can later extract the scanner or other components if there is a real need.

---

## 5. NAS Storage

The NAS should remain the authoritative storage for original images.

Example:

```text
NAS
└── Images
    ├── 2024
    │   ├── Budapest
    │   └── Paris
    ├── 2025
    │   └── Madeira
    │       └── NewTrip
    └── 2026
```

The Docker container should access this directory through a mounted filesystem:

```text
NAS /Images
       |
       v
Docker container
       |
       +-- /images
```

The database should store **relative paths**, not NAS-specific absolute paths.

For example:

```text
RelativePath = "2025/Madeira/NewTrip"
FileName     = "IMG_1234.JPG"
```

The application can construct:

```text
/images/2025/Madeira/NewTrip/IMG_1234.JPG
```

at runtime.

This avoids coupling the database to a particular Windows UNC path or Docker configuration.

---

## 6. Database

Use SQL Server with EF Core.

### Folder

Every physical directory is represented by a `Folder` record.

Example filesystem:

```text
2025/
└── Madeira/
    └── NewTrip/
        ├── IMG001.jpg
        └── IMG002.jpg
```

Database:

```text
Folder
---------------------------------------------
Id | ParentId | Name    | RelativePath
1  | NULL     | 2025    | 2025
2  | 1        | Madeira | 2025/Madeira
3  | 2        | NewTrip | 2025/Madeira/NewTrip
```

### Image

```text
Image
---------------------------------------
Id
FolderId
FileName
FileSize
FileModifiedUtc
FileHash
Width
Height
Orientation
DateTaken
CameraMake
CameraModel
LensModel
Latitude
Longitude
ExifJson
CreatedUtc
UpdatedUtc
LastSeenScanId
```

The database does not contain the original image binary.

---

## 7. Albums

Use an explicit many-to-many entity:

```text
Album
--------------------------------
Id
Name
Description
CreatedUtc
UpdatedUtc
```

```text
AlbumImage
--------------------------------
AlbumId
ImageId
SortOrder
AddedUtc
```

This allows an image to belong to multiple albums and allows manual album ordering.

Example:

```text
Album: Madeira Trip

    IMG001.jpg
    IMG005.jpg
    IMG021.jpg
```

---

## 8. Favorites

Use a separate entity rather than `Image.IsFavorite`:

```text
Favorite
--------------------------------
ImageId
CreatedUtc
```

This gives a clean one-to-zero/one relationship:

```text
Image
  |
  +-- Favorite
```

For a single-user application this is sufficient.

---

## 9. Scanning

Scanning should be a background operation rather than an HTTP request that blocks until the scan finishes.

The API creates a scan:

```http
POST /api/scans
```

The scanner then processes it asynchronously.

Recommended states:

```text
Pending
Running
Completed
CompletedWithErrors
Failed
Cancelled
```

### Scoped scanning

A scan should explicitly specify one or more folders.

For example:

```text
Scan:
    2025/Madeira
```

means:

```text
2025/Madeira
├── NewTrip
├── Hotel
└── Hiking
```

and all descendants are scanned.

If the user selects:

```text
2025/Madeira
2026/Family
```

both trees are scanned.

Overlapping roots should be normalized:

```text
2025
2025/Madeira
```

becomes:

```text
2025
```

to avoid scanning the same tree twice.

---

## 10. ScanSession

Recommended entity:

```text
ScanSession
--------------------------------
Id
StartedUtc
FinishedUtc
Status
FoldersScanned
FilesScanned
FilesAdded
FilesChanged
FilesDeleted
FilesUnchanged
Errors
ErrorMessage
```

A separate `ScanScope` entity records the selected roots:

```text
ScanScope
--------------------------------
Id
ScanSessionId
FolderId
```

---

## 11. Scan Algorithm

The scanner should work approximately as follows:

```text
1. Create ScanSession
2. Set status = Running

3. Load selected folder roots

4. For each root:
      recursively enumerate directories

5. For each directory:
      find/create Folder record

6. For each supported image:
      find Image by FolderId + FileName

      if new:
          read metadata
          calculate hash
          insert Image

      else:
          compare FileSize + FileModifiedUtc

          if unchanged:
              do not reread EXIF/hash

          if changed:
              reread metadata
              recalculate hash
              update Image

7. After successful enumeration:
      find database images belonging to the scanned
      folder tree that were not discovered

8. Delete those Image records

9. Delete related AlbumImage/Favorite records

10. Set ScanSession = Completed
```

### Important failure rule

Do **not** delete database records merely because a scan sees no files.

For example:

```text
NAS temporarily unavailable
        |
        v
Scanner sees zero files
        |
        v
Scan fails
        |
        v
DO NOT DELETE DATABASE RECORDS
```

Only a successfully completed scan of a scope can conclude that previously catalogued files in that scope no longer exist.

---

## 12. Missing Files

For this application, the NAS can be treated as the source of truth.

Therefore, a file deleted from the NAS does not necessarily need to remain in the database.

Recommended V1 behavior:

```text
NAS file exists
    -> Image exists in DB

NAS file deleted
    -> Image record deleted from DB
```

There is no need for an `Image.IsMissing` property unless the application later needs to preserve information about deleted/unavailable images.

This keeps the database model simpler.

---

## 13. Rename / Move Detection

For V1, a rename or move can initially be treated as:

```text
old Image
    -> no longer found
    -> deleted

new path
    -> new Image
```

However, `FileHash` should still be stored.

Later, the application can use SHA-256 to recognize:

```text
Old:
2025/Madeira/IMG1234.JPG

New:
2025/Madeira/Trip/IMG1234.JPG
```

as the same physical image.

This could allow preservation of:

- Image ID
- album memberships
- favorites
- other application metadata

without requiring that functionality in V1.

---

## 14. Image Metadata

Use **MetadataExtractor** for metadata reading.

Typical fields:

```text
FileName
FileSize
Width
Height
Orientation
DateTaken
CameraMake
CameraModel
LensModel
GPS Latitude
GPS Longitude
```

Less frequently used metadata can be retained as:

```text
ExifJson
```

This avoids adding dozens of rarely-used database columns.

---

## 15. Image Processing

Use **ImageSharp** for:

- image dimensions
- orientation handling
- thumbnail generation
- resizing
- image conversion where required

Metadata extraction and image processing should remain separate responsibilities:

```text
Image
 |
 +-- MetadataExtractor
 |      +-- EXIF
 |      +-- IPTC
 |      +-- XMP
 |
 +-- ImageSharp
        +-- dimensions
        +-- orientation
        +-- thumbnails
```

---

## 16. Thumbnails

Do not generate thumbnails every time the browser requests an image.

Use a thumbnail cache.

Example:

```text
PictureManager data
└── thumbnails
    ├── 200
    ├── 800
    └── 1600
```

The browser can request:

```http
GET /api/images/123/thumbnail?size=800
```

The server:

```text
thumbnail exists
    -> return it

thumbnail does not exist
    -> generate it
    -> cache it
    -> return it
```

Original files remain on the NAS.

---

## 17. REST API

Initial API could contain:

```text
Folders
--------
GET    /api/folders
GET    /api/folders/{id}
GET    /api/folders/{id}/children

Images
--------
GET    /api/images
GET    /api/images/{id}
GET    /api/images/{id}/content
GET    /api/images/{id}/thumbnail

Favorites
--------
GET    /api/favorites
POST   /api/images/{id}/favorite
DELETE /api/images/{id}/favorite

Albums
--------
GET    /api/albums
POST   /api/albums
GET    /api/albums/{id}
PUT    /api/albums/{id}
DELETE /api/albums/{id}

POST   /api/albums/{id}/images
DELETE /api/albums/{id}/images/{imageId}

Scanning
--------
POST   /api/scans
GET    /api/scans/{id}
POST   /api/scans/{id}/cancel
```

The API should not expose the NAS physical path directly.

---

## 18. Background Scanner

Initially use:

```csharp
BackgroundService
```

inside the ASP.NET Core application.

Architecture:

```text
POST /api/scans
      |
      v
ScanSession = Pending
      |
      v
BackgroundService
      |
      v
ScanSession = Running
      |
      v
Scan filesystem
      |
      v
ScanSession = Completed
```

A separate `PictureManager.Worker` container can be introduced later if scanning becomes sufficiently large or resource-intensive.

---

## 19. Concurrency and Cancellation

Scanning should support:

```csharp
CancellationToken
```

If the user cancels a scan:

```text
Running
   |
   v
Cancelled
```

A cancelled or failed scan must not perform the final deletion of database records.

This prevents an interrupted scan from incorrectly deleting valid catalog entries.

---

## 20. Database Indexes

Recommended indexes:

### Folder

```text
UNIQUE (ParentId, Name)
UNIQUE (RelativePath)
```

### Image

```text
UNIQUE (FolderId, FileName)

INDEX (FolderId)
INDEX (FileHash)
INDEX (DateTaken)
INDEX (LastSeenScanId)
```

### AlbumImage

```text
PRIMARY KEY (AlbumId, ImageId)

INDEX (ImageId, AlbumId)
```

### Favorite

```text
PRIMARY KEY (ImageId)
```

### ScanScope

```text
UNIQUE (ScanSessionId, FolderId)
```

### ScanError

```text
INDEX (ScanSessionId)
```

---

## 21. Testing

### Backend

Use:

```text
xUnit
FluentAssertions
```

Test:

- folder hierarchy
- scan behavior
- changed/unchanged files
- deleted files
- scan cancellation
- NAS errors
- EXIF parsing
- album operations
- favorite operations

### Integration tests

Use a real test SQL Server instance/container where practical.

Test:

```text
API
  -> Application
  -> EF Core
  -> SQL Server
```

### End-to-end

Use:

```text
Playwright
```

for scenarios such as:

```text
Open folder
    -> browse images
    -> open image
    -> add to album
    -> mark favorite
    -> start scan
    -> observe scan status
```

---

## 22. Docker

A simple initial deployment can consist of:

```text
docker-compose.yml

services:
  picturemanager
  sqlserver
```

The NAS directory is mounted into the PictureManager container:

```text
NAS /Images
     |
     v
/app/images
```

The application should receive the path through configuration:

```text
PictureManager__ImageRoot=/images
```

SQL Server connection string should also come from configuration/environment variables.

---

## 23. Things Not Needed in V1

Avoid adding infrastructure without a concrete requirement.

Initially, there is no need for:

```text
Redis
RabbitMQ
Kafka
Elasticsearch
Kubernetes
Microservices
Message brokers
Cloud object storage
Distributed caching
```

The application is primarily:

```text
React
   |
ASP.NET Core
   |
SQL Server
   |
NAS
```

That is sufficient for a personal photo-management application.

---

## 24. Recommended V1 Stack Summary

```text
Frontend
--------
React
TypeScript
Vite
MUI

Backend
-------
.NET 10
ASP.NET Core
EF Core 10

Image
-----
MetadataExtractor
ImageSharp

Database
--------
SQL Server

Storage
-------
Windows NAS
mounted filesystem

Background processing
---------------------
ASP.NET Core BackgroundService

Testing
-------
xUnit
FluentAssertions
Playwright

Deployment
----------
Docker
Docker Compose

Source control
--------------
Git
Bitbucket
```

## 25. Recommended Development Approach

Build the application incrementally:

### Phase 1 — Foundation

```text
.NET 10
React
SQL Server
Docker
EF Core
```

Implement:

- project structure
- database
- folder model
- basic API
- React application

### Phase 2 — Scanner

Implement:

- scan sessions
- scan scopes
- recursive folder discovery
- image discovery
- incremental scanning
- EXIF extraction
- hash calculation
- deleted-file handling

### Phase 3 — Image Browser

Implement:

- folder navigation
- image grid
- thumbnails
- image viewer
- EXIF display

### Phase 4 — Organization

Implement:

- albums
- album ordering
- favorites

### Phase 5 — Advanced Features

Potential future functionality:

- duplicate detection
- rename/move detection
- advanced search
- GPS/map view
- date-based timeline
- face recognition
- similar-image detection
- AI image tagging
- mobile/PWA support

The architecture should keep these future features possible without requiring them in the initial implementation.
