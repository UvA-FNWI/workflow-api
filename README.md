# Workflow API

Workflow API is a service for modelling real-world processes in a generic and reusable way. The repository contains the reusable workflow engine, an ASP.NET Core API host, example workflow definitions, JSON schemas for workflow authoring, and integration modules for storage, authentication, user lookup, and notifications.

## What Is In This Repository

- `UvA.Workflow`: core workflow model, parsing, orchestration, domain logic, and reusable abstractions.
- `UvA.Workflow.Api`: ASP.NET Core API host, controllers, endpoint wiring, Swagger, and API-specific behavior.
- `UvA.Workflow.Api.Authentication`: authentication scheme selection and provider-specific authentication support.
- `UvA.Workflow.Persistence.Mongo`: MongoDB persistence implementation.
- `UvA.Workflow.Notifications.Graph`: Microsoft Graph notification delivery.
- `UvA.Workflow.Users.DataNose`: DataNose-backed user and role integration.
- `UvA.Workflow.Users.EduId`: EduId-backed user and invite integration.
- `UvA.Workflow.SchemaGenerator`: tooling for generating workflow authoring schemas.
- `Examples`: sample workflow definitions and authoring content.
- `Schemas`: JSON schemas used by workflow authors and tooling.
- `Deployment`: Helm charts and deployment configuration.
- `docs`: deeper project documentation.

## Prerequisites

- .NET 10 SDK
- Docker, if you want to run MongoDB locally with the included compose file
- Access to any external services needed by the integrations you enable, such as identity providers, mail delivery, or user directories

## Getting Started

Restore and build the solution:

```bash
dotnet restore UvA.Workflow.slnx
dotnet build UvA.Workflow.slnx
```

Start a local MongoDB instance:

```bash
docker compose -f UvA.Workflow.Api/docker-compose.yaml up -d
```

Run the API:

```bash
dotnet run --project UvA.Workflow.Api
```

The default development profile serves Swagger at:

- `https://localhost:7093/swagger`
- `http://localhost:5124/swagger`

The API loads sample workflow definitions from `Examples/Projects` during local startup.

## Configuration

Application settings live under `UvA.Workflow.Api`. The API reads the normal ASP.NET Core configuration sources and also optionally loads `appsettings.local.json` for local overrides.

Configuration includes:

- MongoDB connection settings
- allowed CORS origins and frontend base URL
- authentication provider settings
- integration credentials for user lookup, invitations, mail, file storage, and external services
- encryption and API keys

Keep environment-specific values and secrets out of shared documentation and prefer local, user secrets, or deployment-level configuration for sensitive values.

## Testing

Run the test suite with:

```bash
dotnet test UvA.Workflow.slnx
```

The API Docker build also runs the solution tests before publishing the application image.

## Workflow Authoring

Workflow definitions are YAML-based and are organized under `Examples`. Schemas under `Schemas` describe the supported format and can be used by editors and tooling for validation and completion. We currently use this to power type checks in our [VS Code extension](https://github.com/uvA-FNWI/workflow-dev).

## Development Notes

The solution is split between core behavior, the API layer, and optional integration modules. For detailed guidance on where new code belongs, read [docs/architecture/project-boundaries.md](docs/architecture/project-boundaries.md).

Before opening a pull request, check [CONTRIBUTING.md](CONTRIBUTING.md) for the short contributor checklist.
