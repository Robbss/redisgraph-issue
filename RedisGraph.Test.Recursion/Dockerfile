#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["RedisGraph.Test.Recursion/RedisGraph.Test.Recursion.csproj", "RedisGraph.Test.Recursion/"]
RUN dotnet restore "RedisGraph.Test.Recursion/RedisGraph.Test.Recursion.csproj"
COPY . .
WORKDIR "/src/RedisGraph.Test.Recursion"
RUN dotnet build "RedisGraph.Test.Recursion.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RedisGraph.Test.Recursion.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RedisGraph.Test.Recursion.dll"]
