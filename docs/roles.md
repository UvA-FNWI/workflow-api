# Roles

Roles define what a user can see and do within a workflow instance. Every workflow has its own set of roles, defined as YAML files in `Common/Roles/`.

## How roles work

A role is assigned to a user in two ways:

| Type | How it's assigned | Example |
|------|-------------------|---------|
| **Global role** | Resolved by one or more configured external role providers | `Coordinator`, `Admin` |
| **Instance role** | Derived from `User`-typed properties on the workflow instance | `Student`, `Supervisor`, `Examiner` |

The special role `Registered` is automatically assigned to every authenticated user.

External role providers are optional integrations. The core does not assume a specific directory or institution. A deployment may choose to provide global roles from:
### Instance roles

When a workflow instance has a property of type `User` (e.g., `Student`, `Supervisor`), the system checks whether the current user matches the value. If so, the **property name becomes the role name**. This means:

- If the instance's `Student` property contains your user ID → you get the `Student` role
- If the instance's `Supervisor` property contains your user ID → you get the `Supervisor` role

### Inheritance

Roles can inherit permissions from other roles using `inheritFrom`:

```yaml
name: Secretary
inheritFrom: [Admin, BoardMember]
```

This means `Secretary` gets all the actions defined in `Admin` and `BoardMember`, plus any actions defined on `Secretary` itself.

## Creating a role

Create a YAML file in `Common/Roles/{RoleName}.yaml`:

```yaml
# yaml-language-server: $schema=../../../Schemas/Role.json
name: MyRole
title: { en: "My Role", nl: "Mijn Rol" }
actions:
  - type: View
```

### Available properties

| Property | Required | Description |
|----------|----------|-------------|
| `name` | ✅ | Identifier for the role. Must match the filename and be in **PascalCase** |
| `title` | | Display name shown in the UI. Can be a string or bilingual `{ en, nl }` |
| `inheritFrom` | | List of other role names to inherit actions from |
| `actions` | | List of permitted actions (see [Action types](#action-types) below) |
| `assignable` | | Whether users can be manually assigned this role |
| `shortName` | | Short abbreviation (e.g., for display in compact views) |
| `notifications` | | List of notification types this role receives (e.g., `NewInstanceMessage`) |

### Action types

Each action in the `actions` list has a `type` and optional scope:

| Action type | Description |
|-------------|-------------|
| `View` | Can view form submissions |
| `Submit` | Can submit forms |
| `Edit` | Can edit existing submissions |
| `Undo` | Can undo submissions |
| `Execute` | Can trigger executable actions |
| `ViewAdminTools` | Can access admin tools in the UI |
| `ViewHidden` | Can view hidden properties |
| `ViewUsers` | Can view user information |
| `ViewStates` | Can view workflow state details |
| `Delete` | Can delete instances |
| `AddInstanceMessage` | Can add messages to an instance |
| `ViewAnswerMessages` | Can view answer messages |
| `AssignMessages` | Can assign messages |
| `CreateInstance` | Can create new workflow instances |
| `CreateRelatedInstance` | Can create related instances |
| `ImpersonateRoles` | Can impersonate workflow roles |

Actions can be scoped to specific forms or steps:

```yaml
actions:
  - type: View
    form: Request          # Only for the "Request" form
  - type: Submit
    form: Comment
    steps: [Review]        # Only during the "Review" step
```

### Persistent step actions

Set `persistent: true` on an action to keep it available while a linked step is active
and after that step completes. Buttons remain in the matching step cards. This works
both in `globalActions` with an explicit `steps` list and in a step's `actions`, where
the containing step is linked automatically. Each linked step is evaluated separately,
so a future step does not gain a button merely because another linked step is complete.

```yaml
# In a step definition:
actions:
  - type: View
    roles: [Reviewer]
    form: Request
    persistent: true
```

Roles and action conditions are still checked on every request. Persistence does not
bypass hard deadlines on unfinished steps, including parent steps. Completed steps
remain available under the existing deadline rules. The existing exemptions for
`View` permissions and actions without linked steps still apply.
For report actions, use a condition that checks the report identifier is present.
An action without linked steps retains its existing global behavior. Completion is
evaluated from the current workflow state, so resetting a step can revoke persistent
access if the step is no longer active or complete.

The admin card lists persistent actions alongside ordinary actions under their linked steps.

## Well-known roles

> ⚠️ **Important:** The following role names have special meaning in the frontend UI. If your workflow uses equivalent concepts, **use these exact names** to ensure correct UI behavior.

| Role name | Frontend behavior |
|-----------|-------------------|
| `Student` | The progress bar label says "Here's where you are in the process" instead of "This is how far the student is" |

## Naming conventions

- Use **PascalCase** for role names (e.g., `Student`, `Supervisor`, `SecondReviewer`)
- Use **English** names
- The filename must match the role name: `Student` → `Student.yaml`
- The `name` field inside the YAML must also match the filename

## Examples

### Minimal role (no special permissions)

```yaml
name: Student
```

### Role providers in practice

One deployment might fetch global roles from a university directory. Another might map them from Azure AD groups or a custom HR system. The workflow engine only cares about the resulting role names.

### Role with view permissions

```yaml
name: Examiner
actions:
  - type: View
```

### Role with admin access and inheritance

```yaml
name: SuperAdmin
title: SuperAdmin
inheritFrom: [Admin, BoardMember]
actions:
  - type: ViewAdminTools
  - type: ViewAnswerMessages
```

### Role with scoped form permissions

```yaml
name: LimitedMember
title: { en: "Board member (limited)", nl: "Bestuurslid (beperkt)" }
actions:
  - type: View
    form: Request
  - type: View
    form: Decision
  - type: Submit
    form: Comment
  - type: Edit
    form: Comment
```
