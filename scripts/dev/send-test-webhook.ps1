# Sends a correctly signed pull_request.opened delivery at a locally running Grigori, so the
# whole webhook path can be exercised without a public tunnel or a real pull request.
#
#   ./scripts/dev/send-test-webhook.ps1 -Secret <webhook secret>
#
# The secret must match GitHub__WebhookSecret on the running server. Under the AppHost it is
# the generated `github-webhook-secret` parameter:
#   dotnet user-secrets list --project src/Grigori.AppHost

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Secret,

    [string]$Url = 'http://localhost:5219/hooks/github',

    [ValidateSet('pull_request', 'ping')]
    [string]$Event = 'pull_request',

    [string]$Action = 'opened'
)

$ErrorActionPreference = 'Stop'

$payload = @'
{
  "action": "__ACTION__",
  "repository": { "full_name": "grigori-dev/grigori" },
  "pull_request": {
    "number": 4821,
    "title": "Integrations: dedupe webhook deliveries by GUID",
    "body": "GitHub retries deliveries and does not promise ordering, so the same delivery\nGUID can arrive twice.\n\n- Store the GUID on receipt\n- Drop anything already seen\n\nCloses #4790",
    "html_url": "https://github.com/grigori-dev/grigori/pull/4821",
    "draft": false,
    "created_at": "2026-08-09T19:41:07Z",
    "user": { "login": "octocat" },
    "head": { "ref": "forges/dedupe-deliveries", "sha": "9f2c1b47ad3e5608c1d4b90e77a2f31c6de5410b" },
    "base": { "ref": "main", "sha": "1a09508cc7d2b4419e0f83a6ed57cb2049d1f7e3" }
  }
}
'@.Replace('__ACTION__', $Action)

$bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)

$hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($Secret))
try {
    $signature = 'sha256=' + [System.Convert]::ToHexString($hmac.ComputeHash($bytes)).ToLowerInvariant()
}
finally {
    $hmac.Dispose()
}

$headers = @{
    'X-GitHub-Event'      = $Event
    'X-GitHub-Delivery'   = [guid]::NewGuid().ToString()
    'X-Hub-Signature-256' = $signature
}

$response = Invoke-WebRequest -Uri $Url -Method Post -Body $bytes -ContentType 'application/json' -Headers $headers -SkipHttpErrorCheck

Write-Host "$($response.StatusCode) $($response.StatusDescription)  ->  $Url"
if ($response.Content) {
    Write-Host $response.Content
}

exit ($response.StatusCode -ge 400 ? 1 : 0)
