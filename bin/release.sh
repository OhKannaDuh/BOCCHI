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

ZIP_PATH="$PROJECT/bin/Release/$PROJECT/latest.zip"
CSPROJ="$PROJECT/$PROJECT.csproj"

if git rev-parse "$TAG" >/dev/null 2>&1; then
  echo "Error: Tag '$TAG' already exists locally."
  exit 1
fi

if git ls-remote --tags origin | grep -q "refs/tags/$TAG"; then
  echo "Error: Tag '$TAG' already exists on remote."
  exit 1
fi

if command -v xmllint >/dev/null 2>&1; then
  CS_VERSION=$(xmllint --xpath "string(//Project/PropertyGroup/Version)" "$CSPROJ")
else
  CS_VERSION=$(grep -oPm1 '(?<=<Version>)[^<]+' "$CSPROJ" || true)
fi

if [ -z "$CS_VERSION" ]; then
  echo "Error: Could not read <Version> from $CSPROJ"
  exit 1
fi

if [ "$CS_VERSION" != "$TAG" ]; then
    echo "csproj version ($CS_VERSION) does not match tag ($TAG). Updating..."
    xmllint --shell "$PROJECT/$PROJECT.csproj" <<EOF > /dev/null
cd //Project/PropertyGroup/Version
set $TAG
save
EOF
    git add "$PROJECT/$PROJECT.csproj"
    git commit -m "Version: $TAG"
fi

if ! grep -q "# $TAG" CHANGELOG.md; then
  echo "Error: CHANGELOG.md does not contain entry for $TAG."
  exit 1
fi

rm -f "$ZIP_PATH"
echo "Building project..."
dotnet build -c Release
if [ ! -f "$ZIP_PATH" ]; then
  echo "Error: Build failed or $ZIP_PATH not created."
  exit 1
fi

echo "Creating annotated tag $TAG..."
git tag -a "$TAG" -m "$TAG"

echo "Pushing '$BRANCH' and tag '$TAG' to origin..."
git push origin "$BRANCH"
git push origin "$TAG"

echo "Creating GitHub release..."
EXTRA_ARGS=()
if [ "$IS_TESTING" = true ]; then
  EXTRA_ARGS+=(--prerelease)
fi
gh release create "$TAG" --title "$TAG" --generate-notes "${EXTRA_ARGS[@]}"
gh release upload "$TAG" "$ZIP_PATH" --clobber


echo "Updating manifest repo..."
rm -rf plugins
gh repo clone plugins
cd plugins

cd manifest-generator
npm install
manifest_output=$(npx tsx src/index.ts)
commit_message=$(echo "$manifest_output" | awk '/^Suggested commit message:/{getline; print}')
if [ -z "$commit_message" ]; then
    commit_message="Update Manifest"
fi
cd ..

git add manifest.json
if ! git diff --cached --quiet; then
    git commit -m "$commit_message"
    git push origin master
else
    echo "No manifest changes to commit."
fi

# --- Discord message ---------------------------------------------------------

cd discord-message-generator
npm install
echo "------------------------"
npx tsx src/index.ts
cd ../..

rm -rf plugins

echo "Release $TAG complete."
