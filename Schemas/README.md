# JSON schemas

This folder contains JSON schemas for the various YAML files. Generate them by running
`dotnet run` in `UvA.Workflow.SchemaGenerator`. The generator exits with code 1 when it
updates schemas and code 0 when they are already current.

`PropertyDefinition.json` contains the property type variants and their supporting
definitions. `WorkflowDefinition.json` and `Step.json` reference it using a relative
`$ref`, so keep these schema files together when serving or copying them.

The API serves these files at `/Schemas/{name}.json`. After regenerating schemas,
rebuild the API to copy them into its output directory.

Text properties declare `multiline`, `allowAttachments`, and `variant` directly. Choice
and reference properties use scalar layouts such as `layout: RadioList`; embedded
objects use `layout: InlineEditing` or `Modal`. The old nested property layout objects
are rejected. Form, page, and email layouts retain their existing formats.

File properties declare `prefix`, `allowedTypes`, and `maximumSize` directly. The filename prefix supports templates; omitted limits allow PDF files up to 10,000,000 bytes. For file arrays, the size limit applies to the combined uploaded files. Nested `fileSettings` objects are no longer supported.
