# Contributing

## Boundary Rules

Use [docs/architecture/project-boundaries.md](docs/architecture/project-boundaries.md) as the source of truth for where code belongs.

Short version:
- Put reusable business logic in `UvA.Workflow`.
- Put generic HTTP/API delivery code in `UvA.Workflow.Api`.
- Put vendor-specific, institution-specific, or backend-specific code in an optional module.

## Before Opening a PR

Check the following:
- No new institution-specific concepts leaked into `UvA.Workflow` public contracts.
- No new controller, middleware, or `HttpContext` code was added to `UvA.Workflow`.
- No concrete storage implementation was added to core.
- New auth providers were added as modules, not inside the generic API auth selector.
- New user directories or mail providers were added as modules, not in core.
- Docs/config examples remain organization-neutral unless they are explicitly module-specific examples.
