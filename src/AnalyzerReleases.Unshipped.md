; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
NETAGENTS0001 | TypeSafety | Error | Boxing conversions
NETAGENTS0002 | TypeSafety | Error | Object and dynamic values
NETAGENTS0003 | Concurrency | Error | Lock statements
NETAGENTS0004 | Exceptions | Error | Empty catch blocks
NETAGENTS0005 | Naming | Error | Abbreviated identifiers
NETAGENTS0006 | Structure | Error | Type/file structure
NETAGENTS0007 | TypeSafety | Error | Null-forgiving operators
NETAGENTS0008 | Concurrency | Error | Unconditional loops
NETAGENTS0009 | Readability | Error | Explicit literal argument names
NETAGENTS0010 | Structure | Error | Source file length, not counting comment lines
NETAGENTS0011 | Naming | Error | Type containment collisions
NETAGENTS0012 | Concurrency | Error | Framework synchronization and blocking waits
NETAGENTS0013 | Configuration | Error | Consumer overrides of the embedded policy
NETAGENTS0015 | Reliability | Error | Ignored return values, underscore assignments, and deconstruction discards including loops
NETAGENTS0016 | Formatting | Error | Blank lines around control flow and multiline variable declarations
NETAGENTS0017 | Formatting | Error | Braces for multiline bodies and consistent conditional chains
NETAGENTS0018 | Structure | Error | At most sixteen authored C# files directly in each directory per project
NETAGENTS0019 | Formatting | Error | Single-line conditions of at most 128 characters
NETAGENTS0020 | Formatting | Error | Single-line argument and parameter lists up to 128 characters, otherwise one item per line
NETAGENTS0021 | Formatting | Error | Blank lines between adjacent method declarations
NETAGENTS0022 | Ordering | Error | Members ordered by kind, visibility, storage, mutability, and name, grouped with one blank line between groups
NETAGENTS0023 | Formatting | Error | No blank lines directly after an opening brace or before a closing brace
NETAGENTS0024 | Design | Error | Dedicated named types for result contracts instead of tuples
NETAGENTS0025 | Documentation | Error | One summary element per documentation comment
NETAGENTS0026 | Formatting | Error | No parentheses around a single inferred lambda parameter
NETAGENTS0027 | Readability | Error | Imported type names instead of namespace-qualified references
NETAGENTS0028 | Documentation | Error | Documentation elements that add to the name they describe
NETAGENTS0029 | Documentation | Error | Documentation prose inside an element
NETAGENTS0030 | Documentation | Error | Distinct summaries across overloads sharing a name
NETAGENTS0031 | Naming | Error | Method names that do not repeat their first parameter
NETAGENTS0032 | TypeSafety | Error | Numeric casts through CreateChecked, CreateSaturating, or CreateTruncating
NETAGENTS0033 | Documentation | Error | Documentation carries every tag the signature calls for
NETAGENTS0034 | Documentation | Error | Documentation tags fit the member they describe
NETAGENTS0035 | Documentation | Error | Documentation tags of one member say different things
NETAGENTS0036 | Documentation | Error | Documentation text ends with a full stop
NETAGENTS0037 | Documentation | Error | Property and constructor summaries open as the .NET documentation does
NETAGENTS0038 | Documentation | Error | Language keywords written as langword references in documentation
NETAGENTS0039 | Formatting | Error | Statements grouped by kind, one blank line between groups and none inside them
NETAGENTS0040 | Readability | Error | Groups of at most four statements or members, split by comments that say what each part is for
NETAGENTS0041 | Formatting | Error | The when keyword on the line of the catch clause, case pattern, or switch arm it guards
NETAGENTS0042 | Formatting | Error | Members that fit within 128 characters written on one line instead of wrapped at = or =>
NETAGENTS0043 | Configuration | Error | No suppression attributes or pragmas for NetAgents, code analysis, style, or documentation rules
NETAGENTS0044 | Readability | Error | Awaited values named in a local before their members are read
NETAGENTS0045 | Formatting | Error | Simple statements that fit within 128 characters written on one line
NETAGENTS0046 | Naming | Error | No local names built only from placeholder words such as result, flag, or temp
