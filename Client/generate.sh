#!/usr/bin/env bash
set -euo pipefail

# Single source of truth for the published client version. Bump this (and merge to main) to
# cut a release: the Package GitHub Actions workflow triggers on changes to this file.
VERSION=0.14.1
PACKAGE_NAME=Coflnet.Sky.Crafts.Client

# Where to read the OpenAPI document from. Defaults to a locally running API (the historical
# workflow: start the app with `dotnet run`, then run this script). Can also be a file path,
# e.g. one produced without starting any dependent services (used by CI).
SWAGGER_INPUT="${SWAGGER_INPUT:-http://localhost:5009/swagger/v1/swagger.json}"
# Generator image pinned by tag AND digest so output is reproducible; override only for testing.
GENERATOR_IMAGE="${GENERATOR_IMAGE:-openapitools/openapi-generator-cli:v7.25.0@sha256:2ab0a9680222de65dc9d3baf861aa02b99e1b80c211d8221ebf3ae8f8a102524}"
# When set to 1, stop after `dotnet pack` and copying the .nupkg here; NUGET_API_KEY is never read.
SKIP_PUSH="${SKIP_PUSH:-0}"

docker_mounts=(-v "${PWD}:/local")
if [[ "$SWAGGER_INPUT" =~ ^https?:// ]]; then
  generator_input="$SWAGGER_INPUT"
else
  # Local file: bind-mount it at a fixed in-container path so its location on the host (which
  # may be outside $PWD, e.g. a file produced elsewhere in a CI job) doesn't matter.
  swagger_abs="$(cd "$(dirname "$SWAGGER_INPUT")" && pwd)/$(basename "$SWAGGER_INPUT")"
  docker_mounts+=(-v "${swagger_abs}:/swagger-input.json:ro")
  generator_input="/swagger-input.json"
fi

docker run --rm "${docker_mounts[@]}" --network host -u $(id -u ${USER}):$(id -g ${USER}) "$GENERATOR_IMAGE" generate \
-i "$generator_input" \
-g csharp \
-o /local/out --additional-properties=packageName=$PACKAGE_NAME,packageVersion=$VERSION,licenseId=MIT,targetFramework=net10.0,library=restsharp

cd out
path=src/$PACKAGE_NAME/$PACKAGE_NAME.csproj
sed -i 's/GIT_USER_ID/Coflnet/g' $path
sed -i 's/GIT_REPO_ID/SkyCrafts/g' $path
sed -i 's/>OpenAPI/>Coflnet/g' $path
sed -i 's@annotations</Nullable>@annotations</Nullable>\n    <PackageReadmeFile>README.md</PackageReadmeFile>\n    <PublishRepositoryUrl>true</PublishRepositoryUrl>@g' $path
# Force the exact repository URL a verifier reads from the .nuspec (RepositoryType is already
# "git" via the generator's own GIT_USER_ID/GIT_REPO_ID template above).
sed -i 's@<RepositoryUrl>.*</RepositoryUrl>@<RepositoryUrl>https://github.com/Coflnet/SkyCrafts</RepositoryUrl>@' $path
# Pack the README. Anchored on the (always-present, unique) closing </PropertyGroup> tag instead
# of a line number: the generator rewrites this file from scratch on every run, so a line number
# drifts the moment the template gains or loses a line, silently dropping the README from the package.
sed -i '/<\/PropertyGroup>/a\
\
  <ItemGroup>\
    <None Include="..\/..\/..\/..\/README.md" Pack="true" PackagePath="\\"\/>\
  </ItemGroup>' $path

pack_args=()
if [[ -n "${GITHUB_SHA:-}" ]]; then
  # Embeds <repository type="git" url="..." commit="$GITHUB_SHA"/> in the .nuspec so a verifier
  # can tie the published package back to the exact commit it was built from.
  pack_args+=(-p:RepositoryCommit="$GITHUB_SHA")
fi
dotnet pack "${pack_args[@]}"
cp src/$PACKAGE_NAME/bin/Release/$PACKAGE_NAME.*.nupkg ..

if [[ "$SKIP_PUSH" == "1" ]]; then
  rm -r *.sln
  exit 0
fi

if [[ -z "${NUGET_API_KEY:-}" ]]; then
  echo "NUGET_API_KEY is not set; refusing to push $PACKAGE_NAME.$VERSION. Set SKIP_PUSH=1 to only build/pack locally." >&2
  exit 1
fi

dotnet nuget push ../$PACKAGE_NAME.$VERSION.nupkg --api-key $NUGET_API_KEY --source "nuget.org" --skip-duplicate
rm -r *.sln
