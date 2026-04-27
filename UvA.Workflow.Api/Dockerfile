FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ENV HUSKY=0
WORKDIR /src
COPY ["UvA.Workflow.slnx", "./"]
COPY ["UvA.Workflow.Api/UvA.Workflow.Api.csproj", "UvA.Workflow.Api/"]
COPY ["UvA.Workflow/UvA.Workflow.csproj", "UvA.Workflow/"]
COPY ["UvA.Workflow.Tests/UvA.Workflow.Tests.csproj", "UvA.Workflow.Tests/"]
COPY ["UvA.Workflow.SchemaGenerator/UvA.Workflow.SchemaGenerator.csproj", "UvA.Workflow.SchemaGenerator/"]
RUN dotnet restore "UvA.Workflow.slnx"
COPY . .
RUN dotnet test "UvA.Workflow.slnx" -c $BUILD_CONFIGURATION --no-restore
WORKDIR "/src/UvA.Workflow.Api"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./UvA.Workflow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY Examples /Examples
ENTRYPOINT ["dotnet", "UvA.Workflow.Api.dll"]
