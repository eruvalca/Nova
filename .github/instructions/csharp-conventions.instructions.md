---
applyTo: "**/*.cs"
description: "Calcio C# coding conventions, editorconfig expectations, and logging rules."
---

# C# Conventions

- Prefer modern C# language features and syntax by default, while preserving behavior and readability.
- Prefer primary constructors for classes and structs when constructor parameters are primarily used for dependency injection or state initialization.
- Prefer file-scoped namespace declarations over block-scoped namespaces.
- Use braces for all control-flow blocks (`if`, `else`, `for`, `foreach`, `while`, `do`, `switch`, `try`/`catch`/`finally`, and `lock`), including single-line bodies.
- Prefer pattern matching (`is`, `is not`, `switch` expressions, property patterns, list patterns, and relational patterns) when it improves clarity.
- Prefer null-propagation and null-coalescing operators (`?.`, `?[]`, `??`, `??=`) instead of verbose null checks when semantics are equivalent.
- Prefer collection expressions (`[]`, `[a, b, ..other]`) and modern collection initialization patterns when supported and clearer.
- Prefer modern expression style: target-typed `new()`, expression-bodied members where readable, inline `out` variables, and simplified object/collection initialization.
- Prefer `string.Empty` over empty string literals for representing empty strings.
- Eliminate unused parameters and unused value assignments.
- Prefer the C# 8+ declaration form `using var x = ...;` over the braced `using (...) { }` form when the variable lifetime naturally ends at the enclosing scope.

## Discriminated Unions (OneOf)

- Prefer the `OneOf` library for discriminated-union style modeling instead of custom inheritance hierarchies, flag enums with payload side channels, or tuple-based outcome patterns.
- Use `OneOf<T1, ... , TN>` for method return types that can produce one of several known result shapes (for example success, validation failure, not found, or conflict).
- Prefer exhaustive handling with `Match`, `Switch`, or equivalent pattern-based dispatch so every union case is handled explicitly.
- Keep union variants domain-oriented and meaningful; avoid broad catch-all variants such as `object` or `string` when a dedicated type is more precise.
- When a union shape is reused in multiple places, prefer the OneOf source-generator companion library to define a named union type and generate boilerplate members.
- Use source-generated named unions when they improve discoverability, reduce duplication, or centralize shared behavior and documentation for common result shapes.
- Preserve clear API contracts: if a method returns a union, document each possible case in XML docs so callers understand expected flows.

## Documentation

- Add XML documentation comments (`///`) for every C# type and member you add or modify, including `public`, `protected`, `internal`, and `private` declarations.
- Required coverage includes classes, records, structs, interfaces, enums, delegates, services, constructors, methods, properties, fields, and events.
- Every documented symbol must include a meaningful `<summary>` that explains purpose and behavior, not just a restatement of the symbol name.
- Add `<param>` for each method or constructor parameter. Add `<returns>` for non-`void` return values, including `Task<T>` and `ValueTask<T>`.
- Keep documentation behavior-accurate. When behavior changes, update docs in the same change.
- Generated files (for example `*.g.cs`, designer-generated files, and third-party generated sources) are excluded unless the generator supports doc customization.

## Logging

- Use source-generated logging via `partial` methods annotated with `LoggerMessage`.
- Inject `ILogger<T>` via the constructor in all classes that log. Do not use `ILoggerFactory` directly or create loggers outside of DI composition unless the class is a factory or host configuration component.
- Mark classes `partial` when they contain source-generated logging methods.
- If the target class is `static`, convert the logging methods to a separate non-static `partial` logging helper class, or document why source-generated logging cannot be applied and use a fallback `ILogger` passed as a parameter.
- Define one logging method per distinct message; keep messages short, stable, and template-based for structured sinks.
- Do not build log messages with interpolation or concatenation before logging. Pass structured values as method parameters.
- When logging exceptions, pass the `Exception` object as the first parameter of the `LoggerMessage` method and include only the structured context values (for example, operation name and resource identifier) needed to diagnose the failure. Do not swallow exceptions silently.
- Do not log PII or secrets. Include only identifiers necessary for diagnosis.
- Use `Trace` or `Debug` for internal state useful only during development, `Information` for significant application lifecycle events (for example, startup or configuration loaded), `Warning` for recoverable unexpected conditions, `Error` for failures that affect a single operation, and `Critical` for failures that require immediate intervention.
