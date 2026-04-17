# Project Boundaries

This repository is split into three layers:

1. `UvA.Workflow`
   Contains reusable workflow and application logic.
2. `UvA.Workflow.Api`
   Contains the generic HTTP/API delivery layer for that logic.
3. Optional integration modules
   Contain replaceable implementations for persistence, identity, mail delivery, and institution-specific behavior.

## `UvA.Workflow`

`UvA.Workflow` is the engine. Code belongs here when it is reusable without ASP.NET and without a specific identity provider, storage backend, or institution.

Allowed examples:
- Workflow model parsing and evaluation
- Expressions, templates, and conditions
- Domain entities and value objects
- Workflow services and orchestration
- Repository and integration interfaces
- Generic user abstractions
- Generic mail/message abstractions

Does not belong here:
- Controllers
- Middleware
- `HttpContext` access
- Swagger/OpenAPI concerns
- Concrete Mongo repositories
- Concrete Microsoft Graph delivery
- DataNose, EduId, SurfConext, or Canvas-specific logic

Rule of thumb:
- If the code can be tested in isolation with mocks and no web host, it probably belongs in `UvA.Workflow`.

## `UvA.Workflow.Api`

`UvA.Workflow.Api` is the generic HTTP layer.

Allowed examples:
- Controllers and DTOs
- API exception mapping
- API-specific authorization attributes/policies
- `HttpContext` adapters for core abstractions
- Generic API composition
- Generic authentication selector infrastructure
- Impersonation API behavior

Does not belong here:
- Vendor-specific auth handlers
- Provider-specific onboarding or invites
- Concrete persistence implementations
- Institution-specific user directories
- Concrete mail providers

Rule of thumb:
- If the code depends on MVC, middleware, `HttpContext`, or endpoint shape, it belongs in `UvA.Workflow.Api` or an API integration module.

## Optional Integration Modules

Optional modules are the replaceable edges of the system.

Current modules:
- `UvA.Workflow.Persistence.Mongo`
- `UvA.Workflow.Notifications.Graph`
- `UvA.Workflow.Users.DataNose`
- `UvA.Workflow.Users.EduId`
- `UvA.Workflow.Api.Authentication.SurfConext`
- `UvA.Workflow.Api.Authentication.CanvasLti`
- `UvA.Workflow.Api.Authentication.Abstractions`

These modules may depend on `UvA.Workflow` and, where needed, shared auth abstractions. They must not push provider-specific concepts back into `UvA.Workflow`.

Examples of code that belongs in a module:
- A Mongo repository implementation
- A Microsoft Graph mail sender
- A SurfConext token introspection handler
- A Canvas LTI claims resolver
- A DataNose-backed user search source
- An EduId invitation client

## Dependency Direction

Allowed:
- `UvA.Workflow` -> external neutral libraries
- `UvA.Workflow.Api` -> `UvA.Workflow`
- Optional modules -> `UvA.Workflow`
- Optional auth modules -> `UvA.Workflow.Api.Authentication.Abstractions`
- The executable host -> `UvA.Workflow`, `UvA.Workflow.Api`, and whichever modules it opts into

Not allowed:
- `UvA.Workflow` -> optional modules
- `UvA.Workflow` -> `UvA.Workflow.Api`
- Generic API code -> institution-specific behavior
- Optional modules introducing provider-specific enums or concepts back into core contracts

## Contributor Checklist

Before adding a new file, answer these questions:

1. Does it depend on `HttpContext`, MVC, middleware, auth handlers, or Swagger?
   If yes, it belongs in `UvA.Workflow.Api` or an API module.
2. Does it mention a vendor, institution, identity provider, or deployment-specific URL?
   If yes, it belongs in an optional module.
3. Is it a concrete repository/store for a specific backend?
   If yes, it belongs in a persistence module.
4. Is it business logic that can run with mocks in a unit test?
   If yes, it probably belongs in `UvA.Workflow`.
5. Does adding it force new provider-specific fields or enums into core public types?
   If yes, redesign the abstraction first.

## Packaging Map

- Core engine: `UvA.Workflow`
- Generic API host layer: `UvA.Workflow.Api`
- Shared auth contracts: `UvA.Workflow.Api.Authentication.Abstractions`
- Mongo persistence: `UvA.Workflow.Persistence.Mongo`
- Graph mail: `UvA.Workflow.Notifications.Graph`
- DataNose users: `UvA.Workflow.Users.DataNose`
- EduId users/invites: `UvA.Workflow.Users.EduId`
- SurfConext auth: `UvA.Workflow.Api.Authentication.SurfConext`
- Canvas LTI auth: `UvA.Workflow.Api.Authentication.CanvasLti`
