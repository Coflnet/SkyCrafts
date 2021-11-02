FROM mcr.microsoft.com/dotnet/sdk:5.0 as build
WORKDIR /build
COPY SkyCrafts.csproj SkyCrafts.csproj
RUN dotnet restore
RUN git clone --depth=1 https://github.com/NotEnoughUpdates/NotEnoughUpdates-REPO.git itemData
COPY . .
RUN dotnet publish -c release

FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /app

COPY --from=build /build/bin/release/net5.0/publish/ .
COPY --from=build /build/itemData .

ENTRYPOINT ["dotnet", "SkyCrafts.dll"]

VOLUME /data

