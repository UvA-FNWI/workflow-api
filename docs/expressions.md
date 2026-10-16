# Expressions in YAML conditions

Use list expressions as a `value.property` and keep comparisons on the condition's value node:

```yaml
value:
  property: count(except(SelectedChoices, =None))
  greaterThanOrEqual: "2"
```

- `count(list)` returns the number of items. Null and scalar values, including strings, return zero.
- `except(list, =item)` returns a copy without any exact, case-sensitive matches. It preserves order and duplicates of other items.
- `except(list, ValueSetName)` removes the names of all choices in the named ValueSet, regardless of their display text or conditions. Common, local and inherited ValueSets are available by name; an instance property with the same name takes precedence.
- `or(a, b)` and `and(a, b)` combine boolean expressions using the same argument conversion rules.

For example, with a ValueSet named `IgnoredChoices`:

```yaml
value:
  property: count(except(SelectedChoices, IgnoredChoices))
  lessThanOrEqual: "2"
```

`except` returns an empty list for null or scalar input. It does not change the original list.

To check whether a multi-select is empty directly:

```yaml
value:
  property: SelectedChoices
  isEmpty: true
```

`isEmpty` treats null, empty or whitespace-only strings, and empty collections as empty. A collection with any items is non-empty, including a selection named `None`. Existing `coalesce` behavior is unchanged: it falls back only for null.
