# Spec Delta

## MODIFIED Requirements

### Requirement: Connection selection
Every Azure DevOps tool SHALL accept an optional `project` argument holding a project alias. Without it, the default project SHALL be used. Matching SHALL be case-insensitive and SHALL consider only configured projects; there is no fallback to unconfigured projects of a collection.

#### Scenario: Explicit connection
- **WHEN** a tool is called with `project="shop"` and a project with alias `SHOP` is configured
- **THEN** the call runs against that project with its connection's PAT

#### Scenario: Default connection
- **WHEN** a tool is called without `project`
- **THEN** the call runs against the project marked as default

#### Scenario: Unknown connection
- **WHEN** a tool is called with a `project` that matches no configured alias, even if such a project exists in the collection
- **THEN** the call fails with a tool error naming the unknown alias and listing the configured aliases
