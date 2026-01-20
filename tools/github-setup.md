# GitHub Integration Setup

Grigori supports optional GitHub integration that unlocks activity tracking features like commit history, pull request monitoring, and repository analytics.

## Quick Start

1. **Create a GitHub Personal Access Token (PAT)**
   - Go to https://github.com/settings/tokens/new
   - Or click "Create Token" in Grigori's Settings page (it will open with correct scopes)

2. **Configure Required Scopes**
   - `repo` - Full control of private repositories (needed to access commit history and PRs)
   - `read:user` - Read user profile information

3. **Enter Token in Grigori**
   - Open Grigori dashboard at http://localhost:5151
   - Go to **Settings** in the sidebar
   - Paste your token in the "Personal Access Token" field
   - Click **Connect**

## Creating a Personal Access Token

### Step-by-Step Instructions

1. Log in to your GitHub account
2. Navigate to **Settings** → **Developer settings** → **Personal access tokens** → **Tokens (classic)**
3. Click **Generate new token** → **Generate new token (classic)**
4. Configure the token:
   - **Note**: Enter a descriptive name like "Grigori"
   - **Expiration**: Choose based on your preference (90 days recommended)
   - **Scopes**: Select the following:
     - [x] `repo` (Full control of private repositories)
     - [x] `read:user` (Read user profile data)
5. Click **Generate token**
6. **Copy the token immediately** - you won't be able to see it again!

### Direct Link

Use this link to create a token with the correct scopes pre-selected:

```
https://github.com/settings/tokens/new?description=Grigori&scopes=repo,read:user
```

## Token Storage

Your GitHub token is stored securely on your local machine:

- **Windows**: Encrypted using Windows Data Protection API (DPAPI)
- **macOS/Linux**: Encrypted using AES-256 with machine-specific key

Token location: `%LOCALAPPDATA%\Grigori\.github_token` (Windows) or `~/.local/share/Grigori/.github_token` (Unix)

The token is:
- Never transmitted anywhere except to GitHub's API
- Automatically loaded on startup
- Easily removable via the Settings page "Disconnect" button

## Features Unlocked by GitHub Integration

| Feature | Description | Status |
|---------|-------------|--------|
| Activity Feed | Recent commits, PRs, and branch activity | Available |
| Hot Spots | Most frequently changed files/areas | Available |
| Project Pulse | Repository health and activity indicators | Available |
| AI Summaries | LLM-powered change summaries | Coming Soon |

## Troubleshooting

### "Invalid token or token has expired"
- Verify the token is copied correctly (no extra spaces)
- Check if the token has expired on GitHub
- Regenerate the token if needed

### "Token does not have required permissions"
- Make sure both `repo` and `read:user` scopes are selected
- Regenerate the token with correct scopes

### Can't access private repositories
- The `repo` scope is required for private repository access
- For organization repositories, you may need SSO authorization

## Revoking Access

To disconnect Grigori from GitHub:

1. In Grigori: Go to Settings → Click **Disconnect**
2. On GitHub (optional): Go to Settings → Developer settings → Personal access tokens → Delete the Grigori token

## Fine-Grained Tokens (Beta)

GitHub's new fine-grained tokens are not yet fully supported. For now, please use classic Personal Access Tokens.

## Security Considerations

- Generate a dedicated token for Grigori (don't reuse tokens from other apps)
- Use the minimum required scopes
- Set an expiration date on your token
- Regenerate tokens periodically
- Never commit tokens to version control
