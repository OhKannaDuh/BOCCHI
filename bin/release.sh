#!/bin/bash
set -euo pipefail

if [ -z "${1:-}" ]; then
  echo "Usage: $0 <tag> [--testing]"
  exit 1
fi

TAG="$1"
IS_TESTING=false
if [ "${2:-}" == "--testing" ]; then
  IS_TESTING=true
fi

echo "Ensuring head is not detached and working tree is clean..."

BRANCH="$(git rev-parse --abbrev-ref HEAD)"
if [ "$BRANCH" = "HEAD" ]; then
  echo "Error: detached HEAD; checkout a branch before releasing."
  exit 1
fi

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "Error: You have uncommitted changes. Commit or stash them before releasing."
  exit 1
fi

if ! git rev-parse --abbrev-ref --symbolic-full-name "@{u}" >/dev/null 2>&1; then
  echo "No upstream set for '$BRANCH'. Pushing to origin and setting upstream..."
  git push --set-upstream origin "$BRANCH"
fi

git fetch origin "$BRANCH" --tags

read -r REMOTE_ONLY LOCAL_ONLY < <(git rev-list --left-right --count "origin/$BRANCH...HEAD")
if [ "$REMOTE_ONLY" -gt 0 ]; then
  echo "Error: Your branch '$BRANCH' is behind origin/$BRANCH by $REMOTE_ONLY commit(s). Pull/rebase first."
  exit 1
fi

if [ "$LOCAL_ONLY" -gt 0 ]; then
  echo "Pushing $LOCAL_ONLY local commit(s) on '$BRANCH' to origin..."
  git push origin "$BRANCH"
fi


SLN_FILE=$(find . -maxdepth 1 -name "*.sln" | head -n 1 || true)
if [ -z "$SLN_FILE" ]; then
  echo "Error: no solution (.sln) file found in repo root."
  exit 1
fi

PROJECT=$(basename "$SLN_FILE" .sln)
if [ -z "$PROJECT" ]; then
  echo "Error: could not determine project name from $SLN_FILE"
  exit 1
fi

# Directory.Build.props sets Platforms=x64 → output under bin/x64/Release/
ZIP_PATH="$PROJECT/bin/x64/Release/$PROJECT/latest.zip"
ZIP_PATH_FALLBACK="$PROJECT/bin/Release/$PROJECT/latest.zip"
CSPROJ="$PROJECT/$PROJECT.csproj"

RESUME=false
if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
  if [ "$(git rev-list -n 1 "$TAG")" != "$(git rev-parse HEAD)" ]; then
    echo "Error: Tag '$TAG' already exists on another commit."
    exit 1
  fi
  echo "Tag '$TAG' already points at HEAD; finishing the earlier release."
  RESUME=true
fi

if ! tr -d '\r' < CHANGELOG.md | grep -qxF "# $TAG"; then
  echo "Error: CHANGELOG.md does not contain entry for $TAG."
  exit 1
fi

CS_VERSION=$(dotnet msbuild "$CSPROJ" -nologo -getProperty:Version 2>/dev/null | tail -n 1 || true)

if [ -z "$CS_VERSION" ]; then
  echo "Error: Could not read <Version> from $CSPROJ"
  exit 1
fi

if [ "$CS_VERSION" != "$TAG" ]; then
  echo "csproj version ($CS_VERSION) does not match tag ($TAG). Updating..."

  CSPROJ_FILE="$PROJECT/$PROJECT.csproj"

  if grep -q "<Version>" "$CSPROJ_FILE"; then
    sed -i '0,/<Version>/{s|<Version>[^<]*</Version>|<Version>'"$TAG"'</Version>|}' "$CSPROJ_FILE"
  else
    sed -i '0,/<PropertyGroup/{s|<PropertyGroup>|<PropertyGroup>\n    <Version>'"$TAG"'</Version>|}' "$CSPROJ_FILE"
  fi

  git add "$CSPROJ_FILE"
  git commit -m "Version: $TAG"
fi

echo "Running tests..."
dotnet test BOCCHI.Tests -c Release

rm -f "$ZIP_PATH" "$ZIP_PATH_FALLBACK"
echo "Building project..."
dotnet build -c Release -p:Platform=x64
if [ ! -f "$ZIP_PATH" ]; then
  if [ -f "$ZIP_PATH_FALLBACK" ]; then
    ZIP_PATH="$ZIP_PATH_FALLBACK"
  else
    echo "Error: Build failed or zip not created (tried $ZIP_PATH and $ZIP_PATH_FALLBACK)."
    exit 1
  fi
fi

echo "Using zip: $ZIP_PATH"

if [ "$RESUME" = false ]; then
  echo "Creating annotated tag $TAG..."
  git tag -a "$TAG" -m "$TAG"
fi

echo "Pushing '$BRANCH' and tag '$TAG' to origin..."
git push origin "$BRANCH"
git push origin "$TAG"

echo "Creating GitHub release..."
EXTRA_ARGS=()
if [ "$IS_TESTING" = true ]; then
  EXTRA_ARGS+=(--prerelease)
fi
if ! gh release view "$TAG" >/dev/null 2>&1; then
  gh release create "$TAG" --title "$TAG" --notes-file CHANGELOG.md "${EXTRA_ARGS[@]}"
fi
gh release upload "$TAG" "$ZIP_PATH" --clobber


echo "Updating manifest repo..."
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
PLUGINS_DIR="$WORK_DIR/plugins"
gh repo clone OhKannaDuh/plugins "$PLUGINS_DIR"

cd "$PLUGINS_DIR/manifest-generator"
npm install
manifest_output=$(npx tsx src/index.ts)
commit_message=$(echo "$manifest_output" | awk '/^Suggested commit message:/{getline; print}')
if [ -z "$commit_message" ]; then
    commit_message="Update Manifest"
fi
cd "$PLUGINS_DIR"

git add manifest.json
if ! git diff --cached --quiet; then
    git commit -m "$commit_message"
    git push origin master
else
    echo "No manifest changes to commit."
fi

# --- Discord message ---------------------------------------------------------

cd "$PLUGINS_DIR/discord-message-generator"
npm install
echo "------------------------"
npx tsx src/index.ts

echo "Release $TAG complete."
