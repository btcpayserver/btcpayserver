# Release Checklist

When creating a release:

1. Run `dotnet format` on the solution.
2. Run the `PullTransifexTranslations` test.
3. Write the release notes in `Changelog.md`.
4. Bump the version in `Build/Version.csproj`.
5. Confirm the worktree is clean and verify the intended branch, remote, HEAD,
   version, and absence of the release tag locally and remotely.
6. Ensure the release commit is GPG-signed; do not merge it through the GitHub UI.
7. Review `publish-docker.ps1` before running it. The script switches to
   `master`, tags the checked-out commit, and pushes the tag with force. Stop if
   any preflight value is unexpected or the tag already exists.
8. After CI builds the Docker images, copy the new version's changelog section into the GitHub release.

Before publishing, confirm that the release type and timing follow the [release cycle policy](release-cycles.md).
