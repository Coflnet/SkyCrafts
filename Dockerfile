FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
RUN git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev
WORKDIR /build/sky

COPY SkyCrafts.csproj SkyCrafts.csproj
RUN dotnet restore
RUN git clone --depth=1 https://github.com/NotEnoughUpdates/NotEnoughUpdates-REPO.git itemData
COPY . .
RUN dotnet test --project SkyCrafts.Tests/SkyCrafts.Tests.csproj
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app .
COPY --from=build /build/sky/itemData .

ENV ASPNETCORE_URLS=http://+:8000
# using a non-root user is a best practice for security related execution. 
# apt in the base image has CVE-2026-109518 (HIGH); upgrade it until the base image ships the fix.
RUN apt-get update \
    && apt-get install -y --only-upgrade --no-install-recommends apt libapt-pkg6.0t64 \
    && rm -rf /var/lib/apt/lists/*
RUN useradd --uid $(shuf -i 2000-65000 -n 1) app-user 
USER app-user

ENTRYPOINT ["dotnet", "SkyCrafts.dll", "--hostBuilder:reloadConfigOnChange=false"]

VOLUME /data

