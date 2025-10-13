FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["UvA.Workflow.Api/UvA.Workflow.Api.csproj", "UvA.Workflow.Api/"]
COPY ["UvA.Workflow/UvA.Workflow.csproj", "UvA.Workflow/"]
RUN dotnet restore "UvA.Workflow.Api/UvA.Workflow.Api.csproj"
COPY . .
WORKDIR "/src/UvA.Workflow.Api"
RUN dotnet build "./UvA.Workflow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./UvA.Workflow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UvA.Workflow.Api.dll"]
