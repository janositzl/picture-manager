# PictureManager – People Panel & Face Recognition Design

## 1. Objective

The **People** feature allows users to browse images by person and manage AI-based face recognition.

The design is intentionally **image-centric**:

- A person is associated with images.
- The same image appears only once for a person.
- Face detection details are technical data used to identify and highlight faces in an image.
- AI recognition is a suggestion mechanism; it does not automatically create a confirmed person assignment.
- Users can confirm, reject, correct, or permanently ignore detected faces.

## 2. People Page

The People page is divided into two areas:

```text
┌──────────────────-┬──────────────────────────────────────────────┐
│ PEOPLE            │ Selected person / images                     │
│                   │                                              │
│ Search...         │                                              │
│                   │                                              │
│ Named         	│ Confirmed                                    │
│ 👤 John   20 (5) │ [img] [img] [img] [img]                       │
│ 👤 Mary   35 (2) │ [img] [img] [img] [img]                       │
│ 👤 Peter  12     │                                               │
│                  │ Suggested                                     │
│ Unknown          │ [img] [img] [img] [img] [img]                 │
│ ❓ Unknown #1  8 │                                              │
│ ❓ Unknown #2  4 │                                              │
└──────────────────┴──────────────────────────────────────────────┘
```

### 2.1 People list

The left side contains named people, unknown people, and search.

Example:

```text
👤 John       20 (5)
👤 Mary       35 (2)
👤 Peter      12
```

The numbers represent **images**, not detected faces:

- `20` = 20 confirmed images
- `(5)` = 5 suggested images awaiting confirmation

Thus `John 20 (5)` means John is confirmed in 20 images and AI has suggested John for 5 additional images.

If there are no suggestions, omit the parentheses:

```text
Peter 12
```

A tooltip/legend can explain: `20 confirmed · 5 suggested`.

### 2.2 Unknown people

Unknown faces can be grouped for review:

```text
Unknown
  ❓ Unknown #1   8
  ❓ Unknown #2   4
```

The user can assign an unknown person to an existing person, create a new person, leave it unknown, or ignore individual detected faces.

## 3. Selected Person

When a person is selected, the right side displays two sections.

### Confirmed

Images where the person assignment has been explicitly confirmed.

```text
JOHN 20 (5)

Confirmed

[image] [image] [image] [image]
[image] [image] [image] [image]
```

### Suggested

Images where AI predicts the selected person, but the user has not confirmed the assignment.

```text
Suggested

[image] [image] [image] [image]
[image]
```

Suggested images support:

- Accept
- Reject
- Change person
- Accept all suggestions

## 4. AI Recognition Workflow

AI recognition is separate from user confirmation.

```text
Face detected
      │
      ▼
AI predicts a person
      │
      ▼
Suggested
      │
      ├── Accept ───────► Confirmed
      ├── Reject ───────► Unknown
      └── Change person ► Confirmed for selected person
```

AI recognition must not silently create a confirmed assignment.

Recognition confidence should be stored internally and can later be used for sorting or low-confidence review, but does not need to clutter the normal UI.

## 5. Image Viewer Integration

The existing PictureManager image viewer should be reused.

Detected faces are highlighted with rectangles.

```text
┌────────────────────────────────────┬──────────────────────────┐
│              PHOTO                 │ PEOPLE IN PHOTO          │
│                                    │                          │
│        ┌──────────┐                │ ✓ John                   │
│        │   John   │                │ ✓ Mary                   │
│        └──────────┘                │ ❓ Unknown                │
│                                    │                          │
│             ┌──────┐               │ Selected face: John      │
│             │  ?   │               │                          │
│             └──────┘               │ [Change person]           │
│                                    │ [Mark as unknown]         │
│                                    │ [Ignore face]             │
└────────────────────────────────────┴──────────────────────────┘
```

Interactions:

- Hover a person → highlight their face rectangle.
- Hover a face rectangle → highlight the corresponding person.
- Click a face rectangle → select that detected face.
- Click a person → select/highlight the corresponding face.

## 6. People in Photo

The viewer should show:

```text
PEOPLE IN PHOTO

✓ John
✓ Mary
❓ Unknown
```

Suggested recognition can be shown explicitly:

```text
Suggested: John
```

with:

```text
[Accept] [Reject] [Change person]
```

The same image may contain several people; each detected face can have its own assignment state.

## 7. Assigning an Unknown Face

For a selected unknown face:

```text
[Assign person ▼]
```

The user can:

1. Select an existing person.
2. Create a new person.
3. Leave the face unknown.
4. Ignore the face.

Assigning an existing or newly created person makes the assignment confirmed.

## 8. Correcting a Wrong Assignment

A confirmed assignment can be corrected directly from the viewer:

```text
[Change person]
[Mark as unknown]
[Ignore face]
```

- **Change person**: assigns the face to another person.
- **Mark as unknown**: removes the person assignment.
- **Ignore face**: excludes the face from PictureManager's face-recognition tracking.

## 9. Ignored Faces

Some detected faces should never become part of the People system:

- faces on TV screens;
- faces in posters;
- statues or paintings;
- false face detections;
- irrelevant faces the user does not want to identify.

Use **Ignore face**, rather than "Remove face", because the actual photograph is not modified.

The internal state is:

```text
Ignored
```

Meaning:

> This detected face should not participate in PictureManager's people recognition and should not be presented again for identification.

Ignoring must be persistent. Simply deleting a temporary detection record is insufficient because a later scan could recreate it.

The implementation should persist enough information to recognize the ignored face again, for example:

- ImageId
- face location/bounding box
- face embedding or another stable detection fingerprint

The exact matching strategy can be refined during implementation.

## 10. Recognition States

The conceptual states are:

```text
Suggested
Confirmed
Unknown
Ignored
```

### Suggested
AI predicts a person, but the user has not confirmed it.

### Confirmed
The user explicitly accepted the person assignment.

### Unknown
A face was detected, but no person is assigned.

### Ignored
The user explicitly excluded the face from people recognition.

State transitions:

```text
                  ┌───────────────┐
                  │    Detected   │
                  └───────┬───────┘
                          │
                 AI prediction
                          │
                          ▼
                    ┌───────────┐
                    │ Suggested │
                    └─────┬─────┘
                     ┌────┼─────┐
                     │    │     │
                  Accept Reject Change
                     │    │     │
                     ▼    ▼     ▼
                Confirmed Unknown Confirmed

Unknown ──────► Assign person ──────► Confirmed
   │
   └──────────► Ignore ─────────────► Ignored

Confirmed ────► Mark unknown ───────► Unknown
Confirmed ────► Change person ──────► Confirmed
Confirmed ────► Ignore ─────────────► Ignored
```

## 11. Data Model

The model lives in `Faces` and `People` (see `Database-Schema.md`).

- `Face.AssignmentState` holds the four states of §10: `Unknown` (0), `Suggested` (1), `Confirmed` (2), `Ignored` (4); 3 was the old `Rejected`.
- `Face.PersonId` is set for Suggested and Confirmed faces only.
- `Face.RejectedPersonId` implements the §14 follow-up cheaply: rejecting a suggestion (or marking a confirmed face unknown) remembers the person, and clustering never suggests them for that face again.
- `Face.MatchDistance` stores the recognition confidence (§4) for later sorting.
- Ignored survives rescans because re-processing carries the state over to the new face with an overlapping box (IoU ≥ 0.5), the same mechanism that keeps Confirmed.
- Counts (§12) are distinct images: *confirmed* = images with a Confirmed face of the person; *suggested* = images with a Suggested face of the person and no Confirmed face of that person.
- Naming an unknown group, or merging people, only changes the label or owner; it never confirms Suggested faces.

Viewer behaviour: the normal viewer shows only confirmed people; face rectangles, the People-in-photo actions and "Show ignored faces" appear only when the viewer is opened from a person in the People panel.


## 12. Image Counts

People counts must be based on **distinct images**.

If John has multiple technical face detections associated with the same image, the People list still shows:

```text
John 1
```

not:

```text
John 2
```

Counts therefore represent:

```text
confirmed image count
suggested image count
```

rather than raw detection counts.

## 13. Suggested Image Handling

A suggested image is associated with a particular detected face. This matters when an image contains several people.

Example:

```text
Image A

John     → Confirmed
Mary     → Suggested
Peter    → Confirmed
```

The image itself is shown once, while the selected face identifies which person assignment is being reviewed.

The viewer can show:

```text
PEOPLE IN PHOTO

✓ John
? Suggested: Mary
✓ Peter
```

Accepting Mary's suggestion does not affect John or Peter.

## 14. Rejecting Suggestions

For the initial implementation, rejecting an AI suggestion returns the detected face to:

```text
Unknown
```

A more advanced recognition-feedback mechanism can be added later if necessary to prevent the same incorrect prediction from repeatedly appearing.

## 15. Recommended UI Terminology

| Concept | UI terminology | Internal terminology |
|---|---|---|
| AI predicted person | Suggested | Suggested |
| User-approved person | Confirmed | Confirmed |
| No person assigned | Unknown | Unknown |
| User excludes face | Ignore face | Ignored |
| Technical detection | Detected face | DetectedFace |

Avoid making **"Face occurrence"** a user-facing concept.

The important user concepts are:

```text
Person
Image
Suggested
Confirmed
Unknown
Ignored
```

## 16. Main User Scenarios

### Browse a person

```text
People
  John 20 (5)

        ↓

John

Confirmed
[20 images]

Suggested
[5 images]
```

### Accept an AI suggestion

```text
John 20 (5)

Suggested
[image]
```

Open the image:

```text
Suggested: John

[Accept]
```

Result:

```text
John 21 (4)
```

### Reject an AI suggestion

```text
Suggested: John

[Reject]
```

The face becomes Unknown and the John suggestion count decreases.

### Correct an AI suggestion

```text
Suggested: John

[Change person]
```

Select Mary.

Result:

```text
Mary +1 confirmed
John suggestion -1
```

### Identify an unknown person

Select an unknown face and choose:

```text
[Assign person]
```

Then select John. The image becomes a confirmed John image.

### Ignore a false face detection

A television screen contains a face. The system detects it.

The user selects the face and chooses:

```text
[Ignore face]
```

The face is stored as:

```text
Ignored
```

It no longer appears as an unknown or AI suggestion during normal People workflows.

## 17. Design Principles

1. **Image-centric UI** — users think in terms of people and images, not detection records.
2. **AI suggestions are not confirmations** — user confirmation is authoritative.
3. **Counts represent images** — not raw face detections.
4. **Reuse the existing image viewer**.
5. **Make face-to-person relationships visually obvious** with synchronized rectangles and the People-in-Photo panel.
6. **Make corrections easy** directly from the viewer.
7. **Ignore is different from Unknown** — Unknown means "not identified yet"; Ignored means "do not track this face."
8. **Ignored faces survive rescans**.
9. **Keep the first implementation simple**; advanced recognition feedback and clustering can be added later.

## 18. Future Extensions

Possible later features:

- Review low-confidence suggestions.
- Sort suggestions by recognition confidence.
- Bulk accept/reject suggestions.
- Merge people.
- Split incorrectly grouped people.
- Improve recognition models using confirmed faces.
- Recognition feedback to prevent repeated incorrect suggestions.
- Face clustering for unknown people.
- Automatic representative-image selection.
- Person-specific search and filtering.

These should not complicate the initial implementation unless actual usage requires them.
