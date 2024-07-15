VERSION=0.4.0

docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5009/swagger/v1/swagger.json \
-g csharp \
-o /local/out --additional-properties=packageName=Coflnet.Sky.Crafts.Client,packageVersion=$VERSION,licenseId=MIT,targetFramework=net6.0

cd out
path=src/Coflnet.Sky.Crafts.Client/Coflnet.Sky.Crafts.Client.csproj
sed -i 's/GIT_USER_ID/Coflnet/g' $path
sed -i 's/GIT_REPO_ID/SkyCrafts/g' $path
sed -i 's/>OpenAPI/>Coflnet/g' $path
sed -i 's@annotations</Nullable>@annotations</Nullable>\n    <PackageReadmeFile>README.md</PackageReadmeFile>@g' $path
sed -i 's@Remove="System.Web" />@Remove="System.Web" />\n    <None Include="../../../../README.md" Pack="true" PackagePath="\"/>@g' $path

dotnet pack
cp src/Coflnet.Sky.Crafts.Client/bin/Release/Coflnet.Sky.Crafts.Client.*.nupkg ..
