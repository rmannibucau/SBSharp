#! /bin/bash

set -e

#
# HYPOTHESIS:
# - the nuget key is available in the environment as $NUGET_KEY
# - git, grep, sed, bc and dotnet commands are available and functional
#

# enable to test the script - just set "echo"
wrapping_command=

# some configuration
release_commit_prefix='[release] Tagging '
release_commit_next_version_prefix='[release] Preparing next dev iteration '
file_with_version='src/Directory.Build.props'

# extract version to keep it dynamic and easy to maintain
version="$(grep VersionPrefix $file_with_version | head -n1 | sed 's#.*>\(.*\)</.*#\1#')"
echo "[INFO] Releasing version $version"

# create the tag
tag_name="v$version"
$wrapping_command git tag -a "$tag_name" -m "$release_commit_prefix $version"
$wrapping_command git push origin "$tag_name"

# pack the nupkg bundles
$wrapping_command dotnet pack -c Release

# publish
for p in \
  "SBSharp.Core" \
  "SBSharp.Launcher"
do
    echo "[INFO] Publishing $p"
    $wrapping_command dotnet nuget push "./src/$p/bin/Release/$p.0.0.1.nupkg" --api-key "$NUGET_KEY" --symbol-api-key "$NUGET_KEY" --source https://api.nuget.org/v3/index.json
done

# move to next version
next_version="$(echo -n $version | sed 's#\([0-9]*\)\.\([0-9]*\)\..*#\1.\2.#')$(echo $version | sed 's#.*\.\([0-9]*\)#\1+1#' | bc)"
echo "[INFO] Moving to version $next_version"
$wrapping_command sed -i "s#>$version<#>$next_version<#g" "$file_with_version"
$wrapping_command git commit -a -m "$release_commit_next_version_prefix ($next_version)"
$wrapping_command git push origin main
