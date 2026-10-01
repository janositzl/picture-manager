# PictureManager Face Recognition Architecture

## 1. Purpose

This document describes the proposed face-recognition architecture for PictureManager.

PictureManager is a self-hosted application with:

- ASP.NET Core backend
- React frontend
- PostgreSQL database
- Windows NAS containing the original images
- potentially hundreds of folders and millions of images
- incremental folder scanning rather than scanning the entire NAS at once

The face-recognition subsystem is designed to be:

- asynchronous
- incremental
- local/self-hosted
- independent from filesystem scanning
- replaceable with different face-analysis models
- suitable for large image collections

---

# 2. Core architectural principle

The most important separation is:

```text
Filesystem scanning
        !=
Image processing
```

The folder discovery answers:

> What files exist?

The image-processing subsystem answers:

> What information can be extracted from this image?

Therefore, face recognition must not be performed directly inside the folder discovery or image scanner.

Recommended flow:

```text
NAS
 |
 v
Image Scanner
 |
 v
Image records
 |
 v
Processing Jobs
 |
 +---- Metadata
 |
 +---- Thumbnail
 |
 +---- Face Analysis
              |
              v
       Face Detection
              |
              v
        Face Embedding
              |
              v
          ImageFace
```

---

# 3. High-level architecture

```text
                         +------------------+
                         |    React UI      |
                         +--------+---------+
                                  |
                              REST API
                                  |
                         +--------v---------+
                         |  ASP.NET Core    |
                         |      API         |
                         +--------+---------+
                                  |
                 +----------------+----------------+
                 |                                 |
          +------v------+                    +-----v------+
          | PostgreSQL  |                    |    NAS     |
          |             |                    |  Images    |
          +------+------+
                 |
          +------v------------------+
          | Background Workers      |
          |                         |
          | Folder Scanner          |
          | Image Processing Worker |
          | Face Processing Worker  |
          +------------+------------+
                       |
                       v
              +---------------------+
              | Face AI Processor   |
              |                     |
              | Face Detection      |
              | Face Alignment      |
              | Face Embedding      |
              +---------------------+
```

For the first implementation, these components can run as separate processes/containers, but they do not need to be separate microservices.

A modular monolith plus background workers is sufficient.

---

# 4. Recommended deployment

A Docker-based deployment can look like:

```text
+---------------------------------------------------+
| Docker environment                                |
|                                                   |
|  +----------------+     +---------------------+  |
|  | picturemanager |     | picturemanager       |  |
|  | api            |     | worker               |  |
|  | ASP.NET Core   |     | scan/process jobs    |  |
|  +----------------+     +----------+------------+  |
|                                      |             |
|  +----------------+                  |             |
|  | face-processor |<-----------------+             |
|  | Python         |                                |
|  | InsightFace /  |                                |
|  | ONNX Runtime   |                                |
|  +----------------+                                |
|                                                   |
|  +----------------+                               |
|  | PostgreSQL     |                               |
|  | + pgvector     |                               |
|  +----------------+                               |
+---------------------------------------------------+
                 |
                 |
                 v
          Windows NAS
          Image files
```

The exact AI framework can be changed later. The application should communicate with the processor through an internal interface/API rather than depending directly on a specific model.

---

# 5. Image scanning

The existing folder scanner remains responsible for discovering images.

Example:

```text
NAS
 |
 +-- Images
      |
      +-- 2025
           |
           +-- Madeira
                |
                +-- NewTrip
                     |
                     +-- IMG001.jpg
                     +-- IMG002.jpg
                     +-- IMG003.jpg
```

The scanner creates/updates:

```text
Folder
Image
```

For example:

```text
Image
--------------------------------
Id
FolderId
FileName
FileSize
ModifiedDate
Width
Height
...
```

The scanner does not need to understand faces.

---

# 6. Image processing jobs

Introduce a generic processing-job mechanism.

## ImageProcessingJob

```text
ImageProcessingJob
--------------------------------
Id
ImageId
JobType
Status
Priority
Attempts
CreatedAt
StartedAt
CompletedAt
ErrorMessage
ProcessorVersion
```

Recommended job types:

```text
Metadata
Thumbnail
FaceAnalysis
```

Recommended statuses:

```text
Pending
Processing
Completed
Failed
```

Example:

```text
Image 123

Metadata       Completed
Thumbnail      Completed
FaceAnalysis   Pending
```

This allows expensive processing to happen asynchronously.

---

# 7. Why this is important for millions of images

Suppose PictureManager already contains:

```text
1,000,000 images
```

and face recognition is added later.

The filesystem does not need to be rediscovered.

The application already has:

```text
Image
---------------------
Id
FolderId
FileName
FileSize
ModifiedDate
...
```

The application can simply create:

```text
FaceAnalysis
```

jobs for existing images.

The process becomes:

```text
PostgreSQL
    |
    | existing Image records
    v
FaceAnalysis jobs
    |
    v
Face worker
    |
    v
NAS image
    |
    v
Face detection + embedding
```

The NAS still needs to be read once per image for face analysis, but the directory structure does not need to be scanned again.

---

# 8. Face-analysis pipeline

For every image:

```text
Image
  |
  v
Load image from NAS
  |
  v
Face detector
  |
  +-- no face
  |      |
  |      +--> Completed
  |
  +-- one or more faces
         |
         v
   Face alignment
         |
         v
   Face embedding
         |
         v
      ImageFace
```

Example image:

```text
+-------------------------+
|                         |
|      [Face A]           |
|                         |
|                    [B]  |
|                         |
|             [Face C]    |
|                         |
+-------------------------+
```

Database:

```text
Image
  |
  +-- ImageFace A
  +-- ImageFace B
  +-- ImageFace C
```

---

# 9. Face detection vs embedding vs recognition

These are three different concepts.

## Face detection

Answers:

> Where are the faces?

Stores:

```text
X
Y
Width
Height
DetectionConfidence
```

## Face embedding

Converts the face into a numerical vector:

```text
[0.12, -0.34, 0.87, ...]
```

The embedding does not contain a person's name.

## Recognition / matching

Compares embeddings:

```text
New face embedding
        |
        v
similarity search
        |
        v
known face/person
```

Therefore:

```text
Detection
    ->
Embedding
    ->
Matching
    ->
Person
```

---

# 10. Database model

The core entities are:

```text
Person
FaceModel
ImageFace
ImageProcessingJob
```

Existing entities remain:

```text
Folder
Image
Album
AlbumImage
...
```

Relationship:

```text
Folder
  |
  +-- Image
       |
       +-- ImageFace
       |      |
       |      +-- Person
       |      |
       |      +-- FaceModel
       |
       +-- ImageProcessingJob
```

---

# 11. Person

A Person represents a user-defined identity.

```text
Person
--------------------------------
Id
Name
NickName
CreatedAt
UpdatedAt
```

Example:

```text
1   Anna
2   Peter
3   John
```

A Person should not be created automatically by the face detector.

---

# 12. ImageFace

Recommended conceptual structure:

```text
ImageFace
--------------------------------
Id
ImageId
PersonId             nullable
FaceModelId

X
Y
Width
Height

DetectionConfidence
QualityScore

Embedding

CreatedAt
```

Important:

```text
PersonId = NULL
```

is valid.

It means:

> A face was detected, but PictureManager does not know who it is.

Example:

```text
Image 123
 |
 +-- Face 101 -> Anna
 +-- Face 102 -> NULL
 +-- Face 103 -> Peter
```

---

# 13. PostgreSQL and pgvector

PostgreSQL is particularly suitable for this architecture because the `pgvector` extension can store embeddings and perform vector similarity searches.

Recommended PostgreSQL setup:

```text
PostgreSQL
   +
pgvector
```

Conceptually:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

The embedding column can use:

```sql
vector(n)
```

where `n` is determined by the selected face model.

For example, if the selected model produces 512-dimensional embeddings:

```sql
embedding vector(512)
```

Do not hard-code the dimension into application assumptions. Store the model definition separately.

---

# 14. FaceModel

Create a table describing the model used to generate embeddings.

```text
FaceModel
--------------------------------
Id
Name
Version
EmbeddingDimensions
ModelHash
CreatedAt
```

Example:

```text
1
InsightFace
1.x
512
abc123...
```

This is important because embeddings generated by different models should not be compared directly.

---

# 15. Why FaceModel must be stored

Imagine:

```text
2026:
Model A
```

generates:

```text
Embedding A
```

Later:

```text
2027:
Model B
```

generates:

```text
Embedding B
```

Even if both are vectors, they may represent completely different embedding spaces.

Therefore:

```text
ImageFace
    |
    +-- FaceModelId
```

allows PictureManager to know which embeddings are compatible.

---

# 16. PostgreSQL vector search

Once pgvector is installed, a future query can perform nearest-neighbor searches.

Conceptually:

```sql
SELECT
    id,
    embedding <=> :query_embedding AS distance
FROM image_face
WHERE face_model_id = :model_id
ORDER BY embedding <=> :query_embedding
LIMIT 20;
```

The exact operator/index depends on the selected distance metric and pgvector configuration.

The important application-level rule is:

```text
Only compare embeddings generated by compatible models.
```

---

# 17. Vector index

When the number of faces becomes large, create an appropriate pgvector index.

Possible approaches include:

```text
HNSW
IVFFlat
```

For a new system, HNSW is a strong candidate because it supports good approximate nearest-neighbor search without requiring a separate vector database.

The actual index configuration should be validated against your expected number of faces and hardware.

Do not introduce a separate vector database initially.

PostgreSQL + pgvector keeps the system significantly simpler.

---

# 18. Recognition model

The AI component should expose a simple abstraction.

In ASP.NET Core:

```csharp
public interface IFaceAnalyzer
{
    Task<FaceAnalysisResult> AnalyzeAsync(
        string imagePath,
        CancellationToken cancellationToken);
}
```

Result:

```csharp
public sealed class FaceAnalysisResult
{
    public IReadOnlyList<DetectedFace> Faces { get; init; }
}
```

A detected face contains:

```text
BoundingBox
DetectionConfidence
QualityScore
Embedding
```

The rest of PictureManager should not depend on InsightFace-specific classes.

---

# 19. AI processor

Recommended initial implementation:

```text
Python
+
ONNX Runtime
+
a suitable face detection/recognition model
```

InsightFace is one candidate.

Important licensing consideration:

The InsightFace code is MIT licensed, but the project documentation states that its distributed pretrained models are intended for non-commercial research use and that commercial use requires appropriate licensing.

Therefore:

```text
PictureManager
      |
      v
IFaceAnalyzer
      |
      +-- InsightFace implementation
      |
      +-- Alternative implementation
```

Keep the model replaceable.

---

# 20. Face clustering

Recognition and clustering are different.

Initially, PictureManager may have:

```text
250,000 faces
```

but no names.

Use embeddings to cluster visually similar faces:

```text
Cluster 1 -> 12,300 faces
Cluster 2 -> 8,421 faces
Cluster 3 -> 5,112 faces
...
```

The user can then assign a cluster:

```text
Cluster 3
     |
     v
Person: Anna
```

This is much more practical than manually naming individual photographs.

---

# 21. Person assignment

Do not make the face engine itself responsible for Person assignment.

Recommended separation:

```text
Face engine
    |
    +--> face location
    +--> embedding
    +--> confidence
```

PictureManager application:

```text
embedding
    |
    v
similarity search
    |
    v
candidate faces
    |
    v
candidate persons
    |
    v
Person assignment
```

This keeps the AI layer independent of PictureManager's domain model.

---

# 22. Recognition states

A useful conceptual state model is:

```text
Unknown
Candidate
Confirmed
Rejected
```

Example:

```text
Face #123

Candidate:
    Anna     0.93
    Maria    0.04
    Peter    0.01
```

The UI can allow:

```text
[ Confirm Anna ]
[ Not Anna ]
```

Do not automatically assign a person solely because it is the closest result unless the similarity and quality are sufficiently strong.

Thresholds should be calibrated using your own image collection and the selected model.

---

# 23. Reference faces

A Person should have multiple reference faces.

Example:

```text
Person: Anna

Face 1 -> embedding
Face 2 -> embedding
Face 3 -> embedding
Face 4 -> embedding
Face 5 -> embedding
```

A new face can be compared against multiple known faces rather than a single reference image.

This handles variation in:

- lighting
- age
- glasses
- facial expression
- camera angle
- image quality

---

# 24. Unknown-person workflow

Recommended UI:

```text
People
--------------------------------

Known people

[ Anna ]
[ Peter ]
[ John ]


Unknown groups

[face]  1,234 faces
[face]    842 faces
[face]    511 faces
```

Clicking a group:

```text
Unknown person

[face][face][face][face]
[face][face][face][face]

Appears in 83 photos

[ Name this person ]
```

The user enters:

```text
Anna
```

Then all corresponding faces become associated with Anna.

---

# 25. People UI

Add:

```text
People
```

to the main navigation:

```text
Folders
Albums
Favorites
People
```

Person page:

```text
Anna

Faces: 1,284
Photos: 743

[image][image][image][image]
[image][image][image][image]
...
```

A face can also provide:

```text
Find similar photos
```

using vector similarity search.

---

# 26. Face search

Right-clicking a detected face could provide:

```text
Find similar faces
```

Workflow:

```text
Selected face
      |
      v
existing embedding
      |
      v
pgvector similarity search
      |
      v
top N similar faces
      |
      v
corresponding images
```

This is a natural extension of the same embedding infrastructure.

---

# 27. Processing lifecycle

Recommended lifecycle:

```text
Image discovered
       |
       v
Image record created
       |
       v
ImageProcessingJob
       |
       v
FaceAnalysis Pending
       |
       v
Worker claims job
       |
       v
Face processor reads NAS image
       |
       v
Detection
       |
       v
Embedding
       |
       v
ImageFace records
       |
       v
Job Completed
```

---

# 28. Avoid unnecessary reprocessing

Use the existing image fingerprint information.

For example:

```text
Image
--------------------------------
FileSize
ModifiedDate
```

and optionally a stronger content fingerprint.

Face processing can record:

```text
FaceProcessingState
--------------------------------
ImageId
ImageFingerprint
FaceModelId
Status
ProcessedAt
```

If:

```text
same image fingerprint
+
same FaceModel
+
Completed
```

then the image does not need to be processed again.

---

# 29. Model upgrades

If a new model is introduced:

```text
Old:
FaceModelId = 1

New:
FaceModelId = 2
```

Existing faces do not have to be deleted immediately.

Instead:

```text
ImageFace
  |
  +-- Model 1 embedding
  |
  +-- Model 2 embedding
```

Alternatively, create a new processing job for all images and replace old face-analysis results after successful processing.

The second approach is simpler for an initial implementation.

---

# 30. Recommended job behavior

Workers should claim jobs atomically so multiple workers cannot process the same job.

Conceptually:

```text
Pending
   |
   v
Processing
   |
   +---- Completed
   |
   +---- Failed
```

A failed job should have:

```text
Attempts
ErrorMessage
```

and support retry.

Transient NAS/network errors should be retried.

Corrupt images should eventually become permanently failed and be visible for diagnostics.

---

# 31. Performance considerations

For a million-image library:

Do not:

```text
Application startup
    |
    +-- process all images
```

Do:

```text
Application
    |
    +-- continue normal operation

Worker
    |
    +-- process queued images
```

Allow configurable concurrency:

```text
Face workers = 1
Face workers = 2
Face workers = 4
```

depending on CPU/GPU resources.

A NAS-heavy workload and a CPU-heavy face-analysis workload should be independently tunable.

---

# 32. GPU support

The architecture should not require a GPU.

Initial deployment:

```text
CPU
 |
 v
Face processor
```

Later:

```text
GPU
 |
 v
Face processor
```

The application architecture does not change.

This is another reason to isolate the AI processor.

---

# 33. Security and privacy

Face embeddings are sensitive biometric information.

For a self-hosted PictureManager:

```text
NAS image
    |
    v
local face processor
    |
    v
local PostgreSQL
```

No image or embedding needs to leave the home/server environment.

The face processor should not upload images to a cloud recognition service.

Database backups containing embeddings should be treated as sensitive as the original image metadata.

---

# 34. Recommended PostgreSQL schema

Conceptually:

```text
person
----------------
id
name
nickname
created_at
updated_at


face_model
----------------
id
name
version
embedding_dimensions
model_hash
created_at


image_face
----------------
id
image_id
person_id NULL
face_model_id

x
y
width
height

detection_confidence
quality_score

embedding vector(n)

created_at


image_processing_job
----------------
id
image_id
job_type
status
priority
attempts

created_at
started_at
completed_at

processor_version
error_message
```

Foreign keys:

```text
image_face.image_id
    -> image.id

image_face.person_id
    -> person.id

image_face.face_model_id
    -> face_model.id

image_processing_job.image_id
    -> image.id
```

---

# 35. Suggested indexes

At minimum:

```text
image_face(image_id)
image_face(person_id)
image_face(face_model_id)
image_processing_job(status, priority)
image_processing_job(image_id, job_type)
```

For vector search:

```text
HNSW / IVFFlat
```

on:

```text
image_face.embedding
```

with the appropriate model/distance configuration.

The exact vector index strategy should be benchmarked after the expected embedding count is known.

---

# 36. What should NOT be stored

Do not create duplicated copies of photographs for face recognition.

Do not create:

```text
Faces/
   Person1/
      IMG001.jpg
      IMG002.jpg
```

Instead:

```text
Original image
      |
      +-- ImageFace
              |
              +-- Person
```

The original NAS organization remains untouched.

---

# 37. Recommended implementation phases

## Phase 1 - Detection

Implement:

```text
Image
  |
  v
Face detection
  |
  v
ImageFace
```

Store bounding boxes and confidence.

UI:

```text
Image viewer
    |
    +-- draw rectangles around faces
```

This validates the entire pipeline.

## Phase 2 - Embeddings

Add:

```text
FaceModel
ImageFace.embedding
```

Generate embeddings for existing detected faces.

## Phase 3 - Clustering

Generate unknown face groups:

```text
Cluster A
Cluster B
Cluster C
```

Allow users to name them.

## Phase 4 - Person matching

Compare new embeddings against known person faces.

Provide:

```text
Candidate
Confirm
Reject
```

## Phase 5 - Automatic recognition

Once enough verified reference faces exist, allow high-confidence matches to be automatically associated.

---

# 38. Recommended final architecture

```text
                         React
                           |
                           v
                    ASP.NET Core API
                           |
          +----------------+----------------+
          |                                 |
          v                                 v
     PostgreSQL                         NAS Images
       + pgvector
          |
          |
   +------+-----------------------------+
   |                                    |
   v                                    v
Image records                    Face/processing data
   |
   v
ImageProcessingJob
   |
   +------------------+
   |                  |
   v                  v
Metadata           FaceAnalysis
                       |
                       v
                Face AI Processor
                       |
               +-------+-------+
               |               |
               v               v
          Detection        Embedding
               |               |
               +-------+-------+
                       |
                       v
                   ImageFace
                       |
             +---------+---------+
             |                   |
             v                   v
          Matching           Clustering
             |                   |
             v                   v
          Person             Unknown Groups
```

---

# 39. Key design decisions

| Area | Recommendation |
|---|---|
| Database | PostgreSQL |
| Vector storage | pgvector |
| Vector DB | Do not introduce one initially |
| API | ASP.NET Core |
| UI | React |
| AI processor | Separate local process/container |
| AI ecosystem | Python + ONNX Runtime |
| Candidate model | InsightFace or another pluggable model |
| Face detection | AI processor |
| Embedding generation | AI processor |
| Person matching | PictureManager application |
| Clustering | PictureManager background job |
| Image scanning | Separate from face processing |
| Processing | Asynchronous background jobs |
| Original images | Remain on NAS |
| Face data | PostgreSQL |
| Embeddings | PostgreSQL + pgvector |
| Cloud recognition | Not required |
| GPU | Optional |
| Model versioning | Required |

---

# 40. Main architectural rule

The most important rule for the PictureManager implementation is:

```text
                DISCOVERY
                   |
                   v
                 Image
                   |
                   v
            PROCESSING JOBS
                   |
        +----------+----------+
        |          |          |
        v          v          v
     Metadata  Thumbnail   FaceAnalysis
                              |
                              v
                         Face Detection
                              |
                              v
                          Embedding
                              |
                              v
                          ImageFace
                              |
                 +------------+------------+
                 |                         |
                 v                         v
              Matching                 Clustering
                 |                         |
                 v                         v
              Person                 Unknown Group
```

This allows PictureManager to evolve from an image organizer into an image-analysis system without coupling the folder scanner to increasingly expensive AI operations.
