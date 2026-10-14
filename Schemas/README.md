# JSON schemas

This folder contains JSON schemas for the various YAML files. Generate them by running
`dotnet run` in `UvA.Workflow.SchemaGenerator`. The generator exits with code 1 when it
updates schemas and code 0 when they are already current.

`PropertyDefinition.json` contains the property type variants and their supporting
definitions. `WorkflowDefinition.json` and `Step.json` reference it using a relative
`$ref`, so keep these schema files together when serving or copying them.

The API serves these files at `/Schemas/{name}.json`. After regenerating schemas,
rebuild the API to copy them into its output directory.
