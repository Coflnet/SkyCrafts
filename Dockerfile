FROM mcr.microsoft.com/dotnet/sdk:8.0 as build
WORKDIR /build
COPY SkyCrafts.csproj SkyCrafts.csproj
RUN dotnet restore
RUN git clone --depth=1 https://github.com/NotEnoughUpdates/NotEnoughUpdates-REPO.git itemData
COPY . .
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app .
COPY --from=build /build/itemData .

ENV ASPNETCORE_URLS=http://+:8000
# using a non-root user is a best practice for security related execution. 
RUN useradd --uid $(shuf -i 2000-65000 -n 1) app-user 
USER app-user

ENTRYPOINT ["dotnet", "SkyCrafts.dll", "--hostBuilder:reloadConfigOnChange=false"]

VOLUME /data

