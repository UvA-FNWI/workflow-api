# Test fixtures

Workflow definitions (`Projects/`) and the mail layout (`Layouts/`) used by the engine tests. They are copied to the test output and loaded via `UnitTestsHelpers.CreateModelParser()`.

These are test fixtures, not production config. The production workflow definitions live in the separate `milestones-config` repo. This copy exists only so the tests have a stable model to run against, and it has been trimmed to what the tests need.

Change these freely to suit the tests. There is no need to keep them in sync with `milestones-config`.
