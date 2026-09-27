Calulates crafting cost based on data from https://github.com/NotEnoughUpdates/NotEnoughUpdates-REPO


## Configuration
See appsettings.json all of the settings in there can be overwritten by enviroment variables.  
The container port is `8000` it runs as nonroot user.

## Releasing the client package
Bump `VERSION` in `Client/generate.sh` and merge to main; the `Package` workflow builds,
attests and publishes `Coflnet.Sky.Crafts.Client` to nuget.org via trusted publishing (OIDC, no API key).
Verify a release: `gh attestation verify <file>.nupkg.contents --repo Coflnet/SkyCrafts`.
One-time nuget.org trusted publishing policy: owner `ekwav`, repository owner `Coflnet`, repository
`SkyCrafts`, Workflow File `package.yml` (the file name only, not the path), no environment,
package `Coflnet.Sky.Crafts.Client`.


