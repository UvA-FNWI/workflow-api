# Roles

Roles define what a user can see and do within a workflow instance. Every workflow has its own set of roles, defined as YAML files in `Common/Roles/`.

## How roles work

A role is assigned to a user in two ways:

| Type | How it's assigned | Example |
|------|-------------------|---------|
| **Global role** | Fetched from the DataNose API based on the user's account | `Coordinator`, `Admin` |
| **Instance role** | Derived from `User`-typed properties on the workflow instance | `Student`, `Supervisor`, `Examiner` |

The special role `Registered` is automatically assigned to every authenticated user.

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

Actions can be scoped to specific forms or steps:

```yaml
actions:
  - type: View
    form: Request          # Only for the "Request" form
  - type: Submit
    form: Comment
    steps: [Review]        # Only during the "Review" step
```

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
