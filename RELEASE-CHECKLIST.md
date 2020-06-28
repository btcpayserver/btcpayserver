# Release checklist

Things to think about when creating a new release:

* Run `dotnet format` on the solution
* Run `PullTransifexTranslations` test.
* Write chanlog in CHANGELOG.md
* Bump version in `Build/Version.csproj`
* Run `publish-docker.ps1`  
* When the docker images has been built by CI, copy the changelog for the new version in the github's release
