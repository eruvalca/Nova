---
applyTo: "**/*.cs"
description: "Nova C# coding conventions, Try-method contracts, editorconfig expectations, documentation, and logging rules."
---

# C# Conventions

- Prefer modern C# language features and syntax by default, while preserving behavior and readability.
- Prefer primary constructors for classes and structs when parameters are primarily for DI or state initialization.
- Prefer file-scoped namespace declarations over block-scoped namespaces.
- Use braces for all control-flow blocks (`if`, `else`, `for`, `foreach`, `while`, `do`, `switch`, `try`/`catch`/`finally`, and `lock`), including single-line bodies.
- Prefer pattern matching (`is`, `is not`, `switch` expressions, property patterns, list patterns) when it improves clarity.
- Prefer null-propagation and null-coalescing operators (`?.`, `?[]`, `??`, `??=`) instead of verbose null checks.
- Prefer collection expressions (`[]`, `[a, b, ..other]`) and modern collection initialization patterns.
- Prefer target-typed `new()`, expression-bodied members where readable, inline `out` variables, and simplified initialization.
- Prefer `string.Empty` over empty string literals for representing empty strings.
- Eliminate unused parameters and unused value assignments.
- Prefer `using var x = ...;` over `using (...) { }` when the variable lifetime naturally ends at the enclosing scope.
- Before finalizing C# changes, run `dotnet format` with the narrowest scope that covers edited files/projects; use solution-wide formatting only for broad refactors.

## `Try*` contracts

- A method named `TryParse*`, `TryGet*`, or similar must return `false` with safe out values for
  expected malformed input or schema drift; callers must not need exception handling for ordinary
  parse failure.
- Catch only the specific parsing/format exceptions needed to uphold that contract (for example,
  `JsonException`). Do not use a broad catch or hide unrelated programming and infrastructure
  failures.

## Discriminated Unions (OneOf)

- Prefer the `OneOf` library for discriminated-union style modeling instead of custom inheritance hierarchies, flag enums with payload side channels, or tuple-based outcome patterns.
- Use `OneOf<T1, … , TN>` for method return types that can produce one of several known result shapes (success, validation failure, not found, conflict).
- **Prefer native OneOf types** (Success, Error<T>, NotFound, Conflict) for service operations within a single tier or that do not cross boundaries.
- **Use ServiceResult<T>** (defined in `Nova.Shared.Results`) only when the operation crosses service boundaries: HTTP endpoints, WebAssembly client calls, or shared interfaces that span tiers.
  - Example: `ClubMembershipClaimRefresher` (internal, single tier) → native OneOf.
  - Example: `IProfilePhotoService` (boundary-crossing interface) → ServiceResult.
- Handle unions exhaustively with `Match` when branches produce a value and `Switch` for side-effect-only branches. Prefer named handlers or domain-named lambda parameters for multi-case flows.
- Do not branch on positional members such as `IsT0`, `IsT1`, `AsT0`, or `AsT1` in production code; their meaning changes when union ordering changes.
- Keep union variants domain-oriented; avoid broad catch-all variants such as `object` or `string` when a dedicated type is more precise.
- Use the OneOf source generator for a reused union shape, a public/service contract with several cases, or a union whose domain identity improves signatures. Keep a simple, single-use two-case policy as native `OneOf<T0, T1>`.
- Document each possible case in XML docs so callers understand expected flows.
- Internal pure policies use native `OneOf` with domain-named outcomes; `ServiceResult` remains at cross-tier boundaries. See `.github/instructions/functional-core.instructions.md`.
- Prefer feature-local `*Policy` names for deterministic cross-entity decisions. Keep policies free of ambient or mutable static state.

## Operation Input Naming

- Name records that carry values into a service operation with an `Input` suffix (for example, `UpdateCampaignPlacementInput`).
- Reserve `Command` and `Query` suffixes for code that intentionally implements a CQRS command/query architecture. Do not use CQRS terminology for ordinary service method inputs.

## Extension Members (C# 14)

- **Use C# 14 extension blocks** for all new extension members. Declare a static class containing one or more `extension(ReceiverType receiver) { ... }` blocks instead of classic `this`-parameter extension methods.
- The receiver parameter is declared once on the `extension(...)` block; members inside reference it directly and omit both `static` and the `this` parameter.
- Generic receivers and constraints go on the block: `extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder { ... }`.
- Use a receiver type without a parameter name (`extension(IEnumerable<T>)`) when defining static extension members or operators.
- Group multiple extension blocks for different receiver types in one static class when they form a cohesive feature.
- Private non-extension helpers stay as ordinary `private static` methods at class level.
- Control visibility at the enclosing static class: `internal static class` when consumed only within the assembly; `public static class` when extending framework types consumed across projects.
- Do not use `file static` for extension classes consumed from other files.
- Document extension members with XML comments explaining the receiver type they extend and the behavior they add.
- Canonical examples: `Nova/Features/Shared/ServiceResultExtensions.cs`, `Nova.Shared/Results/HttpResponseMessageExtensions.cs`, `Nova.ServiceDefaults/Extensions.cs`.

## Entity-to-DTO Mapping

Use **C# 14 extension blocks** to map domain entities to DTOs. Place one extension class per entity in `Nova/Extensions/{Feature}/`, named `{EntityType}Extensions.cs`.

- Use C# 14 extension block syntax (`extension(EntityType entity) { ... }`) rather than classic `this`-parameter methods.
- Mark the containing static class `internal` — mapping extensions are server-only (entities live in `Nova`; DTOs in `Nova.Shared`).
- Name each mapping method `To{DtoType}()` and return the DTO directly from an expression body.
- Document every method with XML comments; when a navigation property must be loaded before calling the method, state that requirement explicitly in `<summary>`.
- **Never call a mapping method directly in an EF LINQ query** (e.g. inside `Select` before `ToListAsync`). EF cannot translate C# extension methods to SQL. Always materialize first (`.ToListAsync()`), then project in memory (`entities.Select(e => e.ToDto())`).
- Canonical example: `Nova/Extensions/Clubs/ClubEntityExtensions.cs`.

## Documentation

- Add XML documentation comments (`///`) for every C# type and member you add or modify, including `public`, `protected`, `internal`, and `private` declarations.
- Required coverage includes classes, records, structs, interfaces, enums, delegates, services, constructors, methods, properties, fields, and events.
- Every documented symbol must include a meaningful `<summary>` that explains purpose and behavior, not just a restatement of the symbol name.
- Add `<param>` for each method or constructor parameter. Add `<returns>` for non-`void` return values, including `Task<T>` and `ValueTask<T>`.
- Keep documentation behavior-accurate. When behavior changes, update docs in the same change.
- Generated or third-party sources are excluded unless their generator supports documentation customization.

## Logging

- Use source-generated logging via `partial` methods annotated with `[LoggerMessage]`.
- Inject `ILogger<T>` via the constructor. Do not use `ILoggerFactory` directly outside DI composition except in factories or host-configuration components.
- Mark classes `partial` when they contain source-generated logging methods.
- For a static target, use a separate non-static partial logging helper or document why source generation cannot apply and pass an `ILogger` explicitly.
- Define one logging method per distinct message; keep messages short, stable, and template-based for structured sinks.
- Do not build log messages with interpolation or concatenation. Pass structured values as method parameters.
- When logging exceptions, pass the `Exception` object as the first parameter and include only context values (operation name, resource id) needed to diagnose the failure. Do not swallow exceptions silently.
- Do not log PII or secrets.
- Use `Trace`/`Debug` for internal dev state, `Information` for significant lifecycle events, `Warning` for recoverable unexpected conditions, `Error` for single-operation failures, and `Critical` for failures requiring immediate intervention.
