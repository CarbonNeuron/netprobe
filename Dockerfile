FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NetProbe.slnx ./
COPY src/NetProbe.Shared/NetProbe.Shared.csproj src/NetProbe.Shared/
COPY src/NetProbe/NetProbe.csproj src/NetProbe/
RUN dotnet restore

COPY src/ src/
RUN dotnet publish src/NetProbe/NetProbe.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV PATH="/app:${PATH}"
ENTRYPOINT ["netprobe"]
