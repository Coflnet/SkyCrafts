VERSION=0.0.1

docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5009/swagger/v1/swagger.json \
-g csharp-netcore \
-o /local/out --additional-properties=packageName=Coflnet.Sky.Crafts.Client,packageVersion=$VERSION,licenseId=MIT

cd out
sed -i 's/GIT_USER_ID/Coflnet/g' src/Coflnet.Sky.Crafts.Client/Coflnet.Sky.Crafts.Client.csproj
sed -i 's/GIT_REPO_ID/SkyCrafts/g' src/Coflnet.Sky.Crafts.Client/Coflnet.Sky.Crafts.Client.csproj
sed -i 's/>OpenAPI/>Coflnet/g' src/Coflnet.Sky.Crafts.Client/Coflnet.Sky.Crafts.Client.csproj

dotnet pack
cp src/Coflnet.Sky.Crafts.Client/bin/Debug/Coflnet.Sky.Crafts.Client.*.nupkg ..
