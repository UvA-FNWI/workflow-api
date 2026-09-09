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

By default, development startup loads workflow definitions from the public
[`milestones-config`](https://github.com/UvA-FNWI/milestones-config) repository.

### Use a local milestones-config checkout

To develop the API and workflow configuration together, clone `milestones-config` anywhere on your computer and
point `WorkflowSource:LocalPath` at the checkout root, which is the directory containing both `Projects/` and
`Layouts/default.html`. Do not point it at `Projects/` itself, as that will not work.

Add the override to the ignored `UvA.Workflow.Api/appsettings.local.json` file:

```json
{
  "WorkflowSource": {
    "LocalPath": "/absolute/path/to/milestones-config"
  }
}
```

To keep the configuration visible inside the workflow-api workspace while retaining it as a separate repository,
clone the repositories next to each other and create an ignored link from the workflow-api root:

```bash
# Run from the workflow-api repository root
git clone git@github.com:UvA-FNWI/milestones-config.git ../milestones-config
ln -s ../milestones-config milestones-config
```

Then use the link in `UvA.Workflow.Api/appsettings.local.json`:

```json
{
  "WorkflowSource": {
    "LocalPath": "../milestones-config"
  }
}
```

The `milestones-config` link and `appsettings.local.json` are ignored by Git.
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

### Configuration migrations

Property renames are declared in a workflow's `Migrations` folder. The workflow name and filename form a stable
identifier, so never rename or reuse a migration file after it has run. For example,
`Projects/Project/Migrations/rename-title.yaml` can contain:

```yaml
kind: renameProperty
oldProperty: Title
newProperty: ProjectTitle
```

The parser assigns `Scope` from the declaring workflow's name. When a migration first runs, the migration service
resolves its targets from the loaded model and stores them in `Migration.WorkflowDefinitions`.
A migration declared by `Project-Base` targets `Project-Base` and every workflow inheriting from it, directly or
indirectly. Ancestors, siblings, and unrelated workflows are excluded. Global migrations are not supported;
changes across unrelated workflow families require a declaration in each root workflow.

Each declaration runs once under its source identity, such as `Project-Base:rename-title`; it is not duplicated
for each descendant. Migrations with the same filename in different sources have independent identities.
Every target must declare the new property and no longer declare the old property. Targets are not filtered by
property presence: old-only, both-property, and neither-property definitions fail validation before any data is changed.

Recorded migrations are skipped on subsequent loads, including when new descendants or workflow definitions are added.
Their saved targets remain unchanged; configured declarations do not store a target array.
The migration API and admin page expose migration history only; migrations cannot be added manually. API responses
include the declaring `scope` and the resolved `workflowDefinitions` array.

The workflow configuration must declare `ProjectTitle` and no longer declare `Title`. When the baseline
configuration is loaded, the API checks the migrations collection in its MongoDB database. A migration that is not
recorded there is applied immediately and marked as finished. Instance fields are renamed from the old name to
the new name, removing the old field and replacing any existing value at the new name. Matching journal paths
are updated without replacing the journal or its other fields.
Already-finished migrations are skipped. Preview branches and uploaded preview versions never run migrations.
Configured migrations are also disabled by the PR deployment chart, so PR builds cannot modify shared migration
state or instance data.

## Development Notes

The solution is split between core behavior, the API layer, and optional integration modules. For detailed guidance on where new code belongs, read [docs/architecture/project-boundaries.md](docs/architecture/project-boundaries.md).

Before opening a pull request, check [CONTRIBUTING.md](CONTRIBUTING.md) for the short contributor checklist.
