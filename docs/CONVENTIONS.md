# Grigori — Conventions

Working rules for this codebase. [ARCHITECTURE.md](ARCHITECTURE.md) describes the structure;
this describes how the code inside it is written. When the two disagree with existing code,
these documents win — fix the code.

These conventions are inherited from Harmony. Where Grigori deviates, the deviation is listed at
the bottom with its reason, so it reads as a decision rather than drift.

## Project naming

`Grigori.<Area>.<Layer>`, PascalCase, dot-separated, no abbreviations (`DataAccess`, not `DAL`).
`.slnx` folder nodes mirror the physical tree exactly.

| Folder | Holds |
| --- | --- |
| `src/Apps/` | Hosts. `Grigori.Server` is `Program.cs` and configuration, nothing more. |
| `src/Features/<Feature>/` | Domains — things that would exist even if GitHub did not. |
| `src/Integrations/<Name>/` | Adapters — things that exist only because an external system does. |
| `src/Common/` | Cross-cutting libraries no feature owns. |

Inside a project, group by technical kind (`Dtos/`, `Errors/`, `Endpoints/`); interface and
implementation sit as sibling files at the project root.

**No `.Domain` / `.Infrastructure` layering.** Slice vertically by feature first; layer suffixes
apply only inside a slice. `.Application` is a real layer here, but it means *ports* — see below.

## Ports: `.Application` is interfaces only

`Grigori.<Feature>.Application` contains interfaces and nothing else. It is the published
contract: the only thing outside the feature that another project may depend on, alongside
`.Contracts`.

- **Nothing outside a feature may reference its `.Internal` project.** If an outside caller needs
  a type, it belongs in `.Contracts` (data) or `.Application` (behaviour). An integration
  referencing `.Internal` is the specific mistake this layout exists to prevent.
- Inbound ports (`IReviewIngestion`) are implemented by the feature and called by others.
- Outbound ports (`IReviewIntegration`) are implemented by others and called by the feature.
  Implementations register against the port, never as a concrete type.
- Adding an integration touches nothing under `Features/`: implement the outbound port,
  translate into `.Contracts` DTOs, call the inbound port, expose `Add<Name>Integration()`.

**Watch for duplicate interface names when moving a type into `.Application`.** A same-namespace
type beats a `using`, so a leftover copy in `.Internal` silently wins at compile time and only
fails when DI cannot resolve the one that was actually registered. Delete the old file in the
same commit.

## Error handling: OneOf, never exceptions

Repositories, handlers, and clients return `OneOf<TSuccess, TError1, ...>`. Business failures are
values; every failure mode is visible in the signature.

```csharp
Task<OneOf<Success, SignatureRejected, EventIgnored, MalformedPayload>> Handle(
    WebhookDeliveryDto delivery, CancellationToken cancellationToken);
```

- **Errors** live in an `Errors/` folder as plain records (`Grigori.<Feature>.Contracts/Errors/`
  once split out). Empty markers are `readonly record struct` (`SignatureRejected`).
- **Built-ins**: use `OneOf.Types.Success` and `OneOf.Types.NotFound` rather than inventing
  equivalents.
- Consume with `TryPickT0` for the success path and `Match` for mapping the error remainder.
- Exceptions remain for the truly exceptional — a dead socket, not a PR that doesn't exist. Where
  a third-party API throws on bad input (`JsonSerializer`), catch at the boundary and return an
  error value; a thrown exception here becomes a 500, and GitHub retries a 500 forever.

## No private methods

Don't extract private helper methods — inline the logic at the call site. When logic is genuinely
shared, promote it to a real seam: a public static helper class (`GitHubWebhookSignature`), a
common library, or a service.

## Repositories are CRUD

Repository methods are reusable get/create/exists/delete primitives. **Resolution, deduplication,
and orchestration belong in services** in the feature's `.Internal` project.

- Reads take a **Parameter object**; writes take a **DTO** (both from Contracts).
- Parameter records are named `FooParameter` — resource name only, never `GetFooParameter`.
- Method names never use "By": a lookup with a different key gets its own name — `GetChange(ChangeParameter)`
  and `GetChangeByForgeRef` becomes `GetForgeChange(ForgeChangeParameter)`.
- No variant-baked query methods: one method per projection, whose Parameter carries filter, sort,
  and size. Cursor pagination uses `Guid? Cursor` (GUIDv7 ids are time-ordered keysets) and reads
  return `IAsyncEnumerable<T>`.
- Implementations are `internal sealed`; each layer exposes registrations through a static
  `DependencyInjection` class (`AddChangesDataAccess()`).

## Entity / model style

Entities live in a `Models/` folder inside the `<Feature>.Database` project.

- Properties are **packed** — no blank line between them. One blank line separates scalar columns
  from navigation properties.
- No class-level XML summaries. Property comments only when the name can't carry the meaning; a
  commented property gets blank lines around it so it stands out.
- Every mapped scalar/FK is `required`, **even when nullable** — writers state intent.
- Navigations are always nullable; collection navigations initialize to `= [];`.
- Entities are `sealed`. Explicit `HasMaxLength` on every string column.

## General C# style

- File-scoped namespaces. Allman braces, 4-space indent.
- Braceless guard clauses, with the return on its own line:

  ```csharp
  if (string.IsNullOrEmpty(header))
      return false;
  ```
- Primary constructors for services and handlers. Implementations are `internal sealed`; the
  interface they satisfy is `public`.
- **No `Async` suffix** on your own methods (`Handle`, `Ingest`, `Wait`). Keep it only when
  overriding a framework contract that already has it (`ExecuteAsync`).
- Null-check-with-capture patterns name the type explicitly: `payload is not GitHubPullRequestEvent
  pullRequestEvent`, never the anonymous `is not { }`. (Exception: nullable tuples.)
- Ids are generated inside repositories with `Guid.CreateVersion7()`, never by callers.
- Time comes from `TimeProvider`, never `DateTimeOffset.UtcNow` in services or repositories. This
  matters more here than in Harmony: every event carries a timestamp that replay tests must control.
- Comments explain *why*, never restate the code. Every non-obvious infrastructure decision gets
  its reason written down at the point of the decision.

## Endpoints

Minimal APIs, never controllers. One static `<Resource>Endpoints` class per resource in an
`Endpoints/` folder, each exposing `internal static IEndpointRouteBuilder
Map<Resource>Endpoints(this IEndpointRouteBuilder builder)` and owning its own `MapGroup`. Once a
feature has several, a public `<Feature>Endpoints` aggregator owns the group and calls them.

Handlers use **`TypedResults`** with `Results<...>` union return types — never untyped `Results.*`.

## Validation

FluentValidation. Validators live in `Contracts/Validators/` as `<Dto>Validator : AbstractValidator<Dto>`,
**never registered in DI** — instantiated inline at the call site.

## Dependency injection

Every library project gets one `DependencyInjection.cs` at its root, exposing
`Add<Area><Layer>(this IServiceCollection services)`. The `.Api` layer's `Add<Feature>Feature()`
composes the feature's other layers; integrations expose `Add<Name>Integration()`. The host calls
one of each and maps endpoints — `Program.cs` holds no service registrations of its own.

## Migrations

Each feature's DbContext uses its own schema (`changes`, `intents`, `forges`) and its own
migrations history table inside that schema. Generate with:

```
dotnet ef migrations add <Name> --project src/Features/<Feature>/Grigori.<Feature>.Database
```

## Secrets

Local secrets are Aspire parameters backed by AppHost user secrets
(`dotnet user-secrets set Parameters:<name> <value> --project src/Grigori.AppHost`).
Nothing secret is ever committed.

## Build configuration

Each `.csproj` is self-contained: `net10.0`, `ImplicitUsings` and `Nullable` enabled, package
versions written literally. No `Directory.Build.props`, no central package management, no
`.editorconfig` — matching Harmony. Consistency is a habit here, not a tool.

## Commits

`<Area>: <lowercase imperative, no period>` — `Forges: dedupe webhook deliveries by GUID`,
`Changes: add wait registry and await endpoint`. Slash-join two areas when a change spans them.
Omit the scope only for genuinely cross-cutting changes. Not Conventional Commits.

## Gates

`scripts/gates/*.ps1` are the checks agents run instead of reading raw tool output. Each filters
for real errors, exits 1/0, and opens with a comment explaining what "passing" means and any known
false positives.

## Deviations from Harmony

- **Streaming is SSE and long-poll, not SignalR.** Harmony's realtime is SignalR hubs. Agents are
  often bash scripts holding a `curl`, which cannot speak SignalR. `POST /changes/{id}/await`
  long-polls and needs no client library.
- **The projection layer gets tests.** Harmony has none anywhere, and Grigori inherits that for
  most code. The exception is translation and projection: recorded webhook payloads in, projected
  state out is a pure function, and it is the one place where an unnoticed ordering bug silently
  corrupts what every agent believes.
- **`.Mcp` is a new layer.** It has no Harmony precedent, but follows the house pattern of one
  surface project per feature rather than a single MCP server accumulating every tool.
