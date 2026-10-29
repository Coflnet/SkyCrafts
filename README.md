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




## Optional internal HTTPS

HTTP stays on 8000. Setting HTTPS:TLS:CA_CERT_PATH, CERT_PATH and KEY_PATH
adds native HTTPS on 8443 through Coflnet.Core 0.7.5; new handshakes reload
renewed PEM files. HTTPS:TLS:REQUIRE_CLIENT_CERTIFICATE defaults to false.
Trusted clients and anonymous callers are accepted in that mode; an offered
untrusted certificate is rejected. Set true only after a separate enforcement
review. Keep CA/name validation enabled on every caller.

/health/ready reports HTTP-listener health only. It does not mean recipe/profit
caches are warm. Keep the Service readiness probe on /crafts/ready and its
existing warmup semantics. Verify each candidate's direct Pod/forwarded HTTPS
connection within the user's 60-second connection gate while it is NotReady;
then wait for business readiness and a recipe/profit canary before continuing.
No new upstream request or database/broker readiness check runs at startup.

After server verification, migrate actual Crafts callers individually. Crafts'
outbound generated API/Items/Bazaar/PlayerState clients and separate HttpClients
need their own verified HTTPS configuration when those servers are ready; the
optional BazaarFlipper copper fallback and public Hypixel HTTPS remain unchanged.
