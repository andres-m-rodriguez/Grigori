# Grigori — Architecture

Grigori is a coordination layer between coding agents and a forge (today: GitHub). Agents read
state from Grigori and wait on it; they never poll GitHub. One process holds the only connection
to the forge, so adding an agent costs nothing.

[CONVENTIONS.md](CONVENTIONS.md) describes how the code inside this structure is written.

## Why it exists

GitHub already pushes everything an agent polls for — `check_run.completed`,
`pull_request_review.submitted`, `status`. Nobody listens, so N agents rediscover it by brute
force, exhaust the shared hourly budget, and burn one agent turn per poll. Grigori listens once
and lets agents **block** on a condition instead.

## Shape

```
GitHub --webhooks--> Ingress --> Event Log --> Projections --> read API
                                     |                             ^
                                     +------> Waiters -------------+   (blocking /await)

agents --intents--> Outbox --> Rate Governor --> GitHub               (the only write path)
```

## Layout

```
Grigori.slnx
 src/
  Grigori.AppHost/                      AppHost.cs — generates and holds the webhook secret
  Apps/
   Grigori.Server/                      the host. Program.cs and nothing else.
  Features/
   Reviews/
    Grigori.Reviews.Contracts           Origin, Dtos/ — data only
    Grigori.Reviews.Application          the ports. Interfaces only, no implementations.
    Grigori.Reviews.Internal             services behind the ports
  Integrations/
   GitHub/
    Grigori.Integrations.GitHub          adapter: signature, payloads, translation
    Grigori.Integrations.GitHub.Api      Endpoints/WebhookEndpoints.cs
 docs/  scripts/
```

`Features/` holds domains — things that would still exist if GitHub did not.
`Integrations/` holds adapters — things that exist *only* because some external system does.
They are separate top-level folders because they are different kinds of thing, and because an
integration is disposable in a way a feature is not.

## Ports: how an integration plugs in

`Grigori.Reviews.Application` is the contract, and it is the only thing an integration is allowed
to see besides `Contracts`. It contains interfaces and nothing else.

| Port | Direction | Who implements it |
| --- | --- | --- |
| `IReviewIngestion` | integration → Reviews | Reviews. An integration calls it to feed observations in. |
| `IReviewIntegration` | Reviews → integration | The integration. Reviews routes on `Name` to act on an Origin. |

**An integration must never reference `Grigori.Reviews.Internal`.** That is the whole
"agents don't know about GitHub" promise, and it is now a compiler error rather than a
convention someone has to remember. `Grigori.Integrations.GitHub.csproj` references exactly
two projects, and a comment there says why.

To add an integration: implement `IReviewIntegration`, translate into `Contracts` DTOs, call
`IReviewIngestion`, and expose an `Add<Name>Integration()` extension. Nothing in `Features/`
changes.

## Feature slices

Areas, because they have genuinely different lifecycles:

| Feature | Owns |
| --- | --- |
| `Reviews` | The read model and the wait machinery. Grigori's vocabulary lives here. |
| `Intents` | The command queue: requested state changes, idempotency, priority. *(not built)* |

## Vocabulary

Grigori does not speak GitHub's nouns. Adapters translate at the boundary.

| Grigori | Maps to today | Why the rename earns its keep |
| --- | --- | --- |
| `Review` | pull request | The whole thing under review — description included, not just the diff. A Review with no `Origin` was never pushed. |
| `Revision` | head sha | Checks and notes anchor to a revision, so "is this stale?" is a data question. |
| `Check` | check_run / status | Collapses GitHub's two overlapping CI models into one. Local test runs report here too. |
| `Note` | review comment, issue comment, bot output | One thread type with an anchor and a resolved flag. |
| `Verdict` | **review state** | approve / block / comment, from any actor — including agents with no GitHub identity. |
| `Actor` | user / app / bot | Explicitly typed human \| agent \| bot. Orchestration needs this; GitHub won't tell you. |
| `Intent` | any mutating call | A requested state change with an idempotency key and a lifecycle. |

**One collision to keep straight:** GitHub's "review" is an approval or a change request — that
is a `Verdict` here. Grigori's `Review` is the whole proposal that verdicts are cast against.
When both words appear in one sentence, say "Verdict" for GitHub's.

Every record keeps its `Origin` (`github:owner/repo#4821`) so you can always get back. Nothing
in the agent-facing API requires knowing it.

## Layers inside a feature

Same set as Harmony. Not every feature has every layer — the set is a menu.

| Layer | Contents |
| --- | --- |
| `.Contracts` | `Dtos/`, `Errors/`, `Parameters/`, `Validators/`. Data only — no service interfaces. |
| `.Application` | The ports. Interfaces only, no implementations. What another project is allowed to depend on. |
| `.Database` | `Models/` + the feature's `DbContext`. Own Postgres schema, own migrations history table. |
| `.DataAccess` | Repositories. Strictly CRUD. |
| `.Internal` | Services behind the ports: projection, orchestration, background workers. |
| `.Api` | `Endpoints/`. Exposes `Add<Feature>Feature()` and `Map<Feature>Endpoints()`. |
| `.Mcp` | `Tools/`. The surface agents actually call. Mirrors `.Api` one-for-one. |

`.Application` is the load-bearing one: it is what makes `.Internal` genuinely internal. If
something outside the feature needs a type, that type belongs in `.Contracts` or `.Application`,
never `.Internal`.

## Where it is now

The baseline is phase 0 of 5, and deliberately smaller than the diagram above.

**Built:** webhook ingress with HMAC verification, translation of `pull_request.opened` into
`ReviewOpenedDto` (description included), the port layer, and an ingestion seam that logs it.

**Not built yet:** no persistence, no event log, no projections, no waiters, no intents, no MCP.
`IReviewIngestion.Ingest` is the seam every one of those grows behind — no integration has to
move when they land. `IReviewIntegration` carries only `Name` until Intents gives it something
to send.

Build order from here:

| Phase | What |
| --- | --- |
| 0 | Event log + projections + `POST /reviews/{id}/await`. Kills the rate limit outright. |
| 1 | `.Mcp` layers. Agents wait on conditions natively instead of shelling out to `gh`. |
| 2 | `Intents`: outbox, idempotency keys, rate governor. Write operations join `IReviewIntegration`. |
| 3 | Reconciler: periodic GraphQL sweep with ETags, repairing missed deliveries and cold starts. |
| 4 | `Agents`: registry, leases, heartbeats. Where it stops being a cache and becomes orchestration. |
| 5 | A second integration, purely to prove the domain model doesn't secretly encode GitHub's. |

## Connecting a GitHub App

The webhook endpoint is `POST /hooks/github`. GitHub needs a public URL, so local development
needs a tunnel (`cloudflared tunnel --url http://localhost:5219` or smee.io).

1. Create a GitHub App. Prefer it over a PAT: higher baseline rate limit, scales with
   installation size, short-lived scoped tokens.
2. Webhook URL: `https://<tunnel>/hooks/github`. Secret: the value of the AppHost's
   `github-webhook-secret` parameter (`dotnet user-secrets list --project src/Grigori.AppHost`).
3. Subscribe to **Pull requests**. Later phases add Check runs, Statuses, and Pull request reviews.
4. Install it on a repository. GitHub sends a `ping`; a 200 turns the config page green.

To exercise the path without a tunnel:

```
./scripts/dev/send-test-webhook.ps1 -Secret <the secret>
```
