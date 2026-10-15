# Git workflow sample: the life of a feature branch

This is the workflow used for the user-management phases (branch per phase, merge to `main` with a merge commit, delete the branch). Commands are for Git Bash or PowerShell from the repo root. Replace names in `<angle brackets>`.

## 0. One-time setup

```bash
git config --global user.name  "Your Name"
git config --global user.email "you@example.com"

# Windows: stops the "LF will be replaced by CRLF" warnings from being a problem
git config --global core.autocrlf true
```

The warning `LF will be replaced by CRLF the next time Git touches it` is harmless. It only means line endings get normalized on commit.

## 1. Start from a clean, up-to-date main

```bash
git checkout main            # or: git switch main
git status                   # should say "nothing to commit" (untracked files are fine)
git pull origin main         # get the latest from the remote (skip if you have no remote changes)
```

Never start feature work directly on `main`. The branch is what keeps `main` always working.

## 2. Create the feature branch

```bash
git checkout -b feature/user-management-phase3
# same thing, newer syntax:
git switch -c feature/user-management-phase3
```

Naming convention: `feature/<what>`, `fix/<what>`, `chore/<what>`. Lowercase, hyphens.

Check where you are at any time:

```bash
git branch                   # list local branches, * = current
git status -sb               # short status with branch name and ahead/behind
```

## 3. Work in small steps and commit often

The cycle: edit files → look at what changed → stage → commit.

```bash
git status                   # what changed (modified = tracked, ?? = untracked)
git diff                     # unstaged changes, line by line
git diff --stat              # just a per-file summary

git add src/PictureManager.Api/Endpoints/UserEndpoints.cs   # stage one file
git add src tests                                           # stage whole folders
git add -p                                                  # stage piece by piece (interactive)
# Avoid `git add .` / `git add -A` here: it picks up scratch files (pm-images.tar, tools/*, .claude/ ...)

git diff --staged            # review exactly what the commit will contain

git commit -m "feat(users): admin /api/users endpoints and user directory"
```

Commit message style used in this repo (Conventional Commits):

| Prefix | Use for |
|---|---|
| `feat(scope):` | new behaviour |
| `fix(scope):` | bug fix |
| `docs:` | documentation only |
| `chore:` | maintenance, config, tooling |
| `test:` | tests only |
| `refactor:` | restructuring without behaviour change |

A longer message (subject, blank line, body, trailer):

```bash
git commit -m "feat(favorites): per-user favorites in a UserFavorites table

The migration copies the old global favorites to the initial admin.

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

Rules of thumb: one logical change per commit, tests in the same commit as the code they cover, and every commit should build.

### Looking back at your work

```bash
git log --oneline -10                  # last 10 commits, one line each
git log --oneline main..HEAD           # commits on this branch that main doesn't have yet
git log --oneline --graph --all -15    # branch/merge picture
git show <commit>                      # one commit in full
git diff main...HEAD --stat            # everything this branch changes relative to main
```

### Fixing mistakes (safe versions first)

```bash
git restore <file>                     # throw away UNCOMMITTED edits to a file (cannot be undone)
git restore --staged <file>            # unstage a file but keep the edits
git commit --amend -m "better message" # rewrite the LAST commit (only if not pushed yet)
git revert <commit>                    # make a new commit that undoes an old one (safe, even after pushing)
git stash                              # park uncommitted work
git stash pop                          # bring it back
```

`git reset --hard` and `git push --force` discard work. Don't use them unless you know exactly why.

## 4. Share the branch (optional but recommended as a backup)

```bash
git push -u origin feature/user-management-phase3     # first push; -u links local and remote branch
git push                                              # later pushes of the same branch
```

If you work with pull requests on GitHub, open one now:

```bash
gh pr create --base main --title "User management phase 3" --body "Album sharing and per-user favorites"
```

## 5. Keep the branch current with main (only if main moved)

If other work landed on `main` while you were on the branch:

```bash
git fetch origin
git merge origin/main            # bring main's changes into your branch
# or, for a linear history (only before the branch is pushed/shared):
git rebase origin/main
```

If Git reports a conflict:

```bash
git status                       # lists the conflicted files
# open each file, fix the <<<<<<< ======= >>>>>>> sections, save
git add <fixed-file>
git merge --continue             # (or: git rebase --continue)
git merge --abort                # bail out and go back to before the merge
```

## 6. Final checks before merging

```bash
git status                       # clean, nothing left uncommitted
dotnet test                      # backend
cd web && npm run test && npm run build && cd ..
```

Stop any running `PictureManager.Api.exe` first or the build fails with "file is locked".

## 7. Merge back into main

```bash
git checkout main
git pull origin main                                  # make sure main is current
git merge --no-ff feature/user-management-phase3 \
  -m "Merge branch 'feature/user-management-phase3': album sharing and per-user favorites"
```

`--no-ff` always creates a merge commit, so the history keeps "these commits belonged to one feature". Without it Git may just move `main` forward (fast-forward) and the branch boundary disappears.

Check the result:

```bash
git log --oneline --graph -10
git status -sb                   # shows "ahead N" of origin/main
```

If you used a GitHub pull request, merge it in the web UI instead, then `git checkout main && git pull`.

## 8. Publish main

```bash
git push origin main
```

(Not done automatically in this project. Pushing is a deliberate step.)

## 9. Close the branch

```bash
git branch -d feature/user-management-phase3          # delete local branch; refuses if it has unmerged commits (safe)
git push origin --delete feature/user-management-phase3   # delete the remote copy, if you pushed one
git remote prune origin                               # forget remote branches that no longer exist
```

`git branch -D <name>` (capital D) deletes even if unmerged. Use it only for a branch you really want to throw away.

## 10. Housekeeping after the merge

```bash
git branch                       # confirm only the branches you still want
git log --oneline -5             # confirm the merge commit is on top
```

Then apply anything the feature needs outside Git, for example database migrations (`dotnet ef database update`, or however the README says to apply them).

## Whole life cycle on one screen

```bash
git checkout main && git pull origin main
git checkout -b feature/<name>

# ...edit... then repeat as often as needed:
git add <files> && git commit -m "feat(scope): what changed"

git push -u origin feature/<name>        # backup / pull request

# before merging:
dotnet test && (cd web && npm run test && npm run build)

git checkout main && git pull origin main
git merge --no-ff feature/<name> -m "Merge branch 'feature/<name>': summary"
git push origin main
git branch -d feature/<name>
git push origin --delete feature/<name>
```

## Quick reference

| I want to... | Command |
|---|---|
| See what changed | `git status`, `git diff` |
| See the history | `git log --oneline --graph -15` |
| Stage and commit | `git add <files>` then `git commit -m "..."` |
| Undo uncommitted edits to a file | `git restore <file>` |
| Undo a pushed commit safely | `git revert <commit>` |
| Park work temporarily | `git stash` / `git stash pop` |
| Switch branch | `git checkout <branch>` or `git switch <branch>` |
| Which branch am I on? | `git branch` or `git status -sb` |
| What is on this branch but not on main? | `git log --oneline main..HEAD` |
| Delete a merged branch | `git branch -d <branch>` |
