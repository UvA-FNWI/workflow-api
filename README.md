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

Start a new local MongoDB instance (pinned to 8.2.9):

**Existing local database?** Follow [the MongoDB upgrade instructions](docs/mongodb-upgrade.md)
before running this command. MongoDB stores its data in `UvA.Workflow.Api/data/mongo`.

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
With `WorkflowSource:LocalPath` set, the API loads that checkout at startup. Automatic reloads are disabled by default.
To enable them for your own development session, add `WatchLocalChanges` to your ignored
`UvA.Workflow.Api/appsettings.local.json` file:

```json
{
  "WorkflowSource": {
    "LocalPath": "../milestones-config",
    "WatchLocalChanges": true
  }
}
```

Restart the API after changing this setting. While enabled, local file changes reload workflow config;
refresh the browser to see the updated config. Set `WatchLocalChanges` to `false` (or remove it) and restart
the API to disable automatic reloads. The watcher requires `LocalPath` to be set.

To also refresh connected browsers after a successful reload, set `VITE_AUTO_REFRESH_CONFIG=true`
in workflow-ui's ignored `.env.local` and restart its development server. The UI subscribes to the
authenticated `/Versions/Events` stream, which is available only with local watching enabled.
On each update, it invalidates API cache tags and refetches active data without reloading the page.
Named preview pages are unaffected by baseline changes. See the workflow-ui README for details.

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

Migrations rename stored properties when the baseline configuration is loaded. Add a YAML file to the workflow's
`Migrations` folder, for example `Projects/Project-Base/Migrations/2026-09-09-rename-title.yaml`:

```yaml
kind: RenameProperty
oldProperty: Title
newProperty: ProjectTitle
```

Filename convention: `yyyy-MM-dd-{description}.yaml`.

`kind` is optional and defaults to `RenameProperty`. Only top-level property renames are supported.

The migration applies to the declaring workflow and all workflows inheriting from it, directly or indirectly.
Update the workflow definitions alongside the migration: they must contain the new property and no longer contain
the old one. Existing instance values and journal paths are renamed automatically.

Recorded migrations are skipped on later loads, so keep their filenames and declaring workflow names stable.
Preview configurations do not run migrations. You can view migration results on the migrations page.

## Development Notes

The solution is split between core behavior, the API layer, and optional integration modules. For detailed guidance on where new code belongs, read [docs/architecture/project-boundaries.md](docs/architecture/project-boundaries.md).

Before opening a pull request, check [CONTRIBUTING.md](CONTRIBUTING.md) for the short contributor checklist.
