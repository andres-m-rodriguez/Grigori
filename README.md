# Grigori

A coordination layer between coding agents and GitHub.

Agents read state from Grigori and wait on it. They never poll GitHub, so they never hit its
rate limits, and adding another agent costs nothing.

## The problem

Every agent running `gh pr checks` on a thirty-second timer is asking GitHub a question GitHub
already volunteered the answer to. GitHub pushes `check_run.completed`,
`pull_request_review.submitted`, and `status` the moment they happen. Nobody is listening, so N
agents rediscover it by brute force.

That costs twice. It burns the shared hourly budget until everything 403s, and — the part that
actually hurts — every poll is an agent turn. An agent waiting twenty minutes for CI spends forty
wake-ups re-reading the same JSON.

## The idea

One process subscribes to GitHub's webhooks and materialises the state. Every agent then reads
that state for free and, more importantly, can **block** on it:

```http
POST /reviews/{id}/await
{ "until": "checks_settled", "timeout": "20m" }
```

The request hangs open until the condition holds. Zero GitHub calls, zero intermediate agent
turns. Exposed over MCP it is better still — the agent calls one tool, the tool does not return
for eighteen minutes, and the model spends nothing in between.

## Vocabulary

Grigori does not speak GitHub's nouns. Adapters translate at the boundary, so nothing downstream
knows the phrase "pull request".

| Grigori | GitHub |
| --- | --- |
| `Review` | pull request — the whole thing under review, description included |
| `Revision` | head sha |
| `Check` | check_run / status |
| `Note` | review comment, issue comment, bot output |
| `Verdict` | review state (approve / request changes) |
| `Origin` | `github:owner/repo#4821` |

A `Review` with no `Origin` was never pushed anywhere — that is deliberate room for agents to
review each other's work before a branch exists.

## Status

Early. The webhook ingress works end to end: signature verification, translation of
`pull_request.opened` into Grigori's vocabulary, and an ingestion seam.

Not built yet: persistence, the event log, projections, the wait registry, intents, MCP.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design and build order, and
[docs/CONVENTIONS.md](docs/CONVENTIONS.md) for how the code is written.

## Running it

```powershell
dotnet user-secrets set "Parameters:github-webhook-secret" "<value>" --project src/Grigori.AppHost
dotnet run --project src/Grigori.AppHost
```

GitHub needs a public URL to deliver to. A dev tunnel is enough:

```powershell
devtunnel create grigori --allow-anonymous
devtunnel port create grigori -p 5219
devtunnel host grigori
```

Then add a repository webhook pointing at `https://<tunnel>/hooks/github` with content type
`application/json`, the same secret, subscribed to **Pull requests**. Content type matters — the
default form encoding wraps the body and breaks the signature.

To exercise the path without a tunnel:

```powershell
./scripts/dev/send-test-webhook.ps1 -Secret "<value>"
```

## Layout

```
src/
  Grigori.AppHost/                 Aspire orchestration
  Apps/Grigori.Server/             the host
  Features/Reviews/
    Grigori.Reviews.Contracts      data
    Grigori.Reviews.Application    the ports — interfaces only
    Grigori.Reviews.Internal       services behind the ports
  Integrations/GitHub/
    Grigori.Integrations.GitHub        adapter
    Grigori.Integrations.GitHub.Api    webhook endpoint
```

`Features/` holds domains — things that would exist even if GitHub did not. `Integrations/` holds
adapters. An integration may reference a feature's `.Contracts` and `.Application` and nothing
else, so "agents don't know about GitHub" is a compiler error rather than a rule to remember.

The name fits: the Grigori were the Watchers, the ones who kept the record.
