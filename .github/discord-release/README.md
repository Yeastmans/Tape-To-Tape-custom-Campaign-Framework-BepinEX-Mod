# Discord release announcements

Publishes the GitHub release title, tag, beta/stable label, patch notes and Windows
installer link to **#release-patchnotes** in server **1559632629094154361**.
Runs on GitHub Actions; no computer or Discord bot process needs to stay running.

## Connect the channel

1. In Discord, open **#release-patchnotes > Edit Channel > Integrations > Webhooks**.
   Create an incoming webhook named **Campaign Studio Updates**. Confirm its channel
   is **release-patchnotes**, then copy its webhook URL.
2. In the mod GitHub repository, open **Settings > Secrets and variables > Actions >
   New repository secret**. Name it **DISCORD_RELEASE_WEBHOOK_URL** and paste the URL
   as its value. Do not put the URL in source, release notes or chat.
3. For an additional channel check, enable Discord Developer Mode, copy the channel
   ID and put it in `config.json` as `channel_id`. The supplied ID is the **server**
   ID, not the channel ID. The server is always checked before any message is sent.
4. Merge/push `.github` to the repository's default branch before the next release.
   Make sure GitHub Actions is enabled for the repository.

## Preview and activate

Open **Actions > Discord release announcements > Run workflow**. Enter an existing
published release tag. Leave **Send to Discord** unchecked for a preview. It checks
the actual release and formats the message without needing the webhook secret.

To announce that existing release once, run again with **Send to Discord** checked.
Future beta and stable releases are automatically announced when published.
Publishing the workflow itself does not announce an old release. Drafts, ordinary
commits and release-note edits do not post messages.

Check the first real post in #release-patchnotes: verify the title, notes, beta/stable
label, full-notes link and installer link. No users or roles should be pinged.

## Behaviour and troubleshooting

- The notes come directly from the published release body. Write the player-facing
  patch notes before publishing; the bot does not generate or invent changes.
- Notes longer than the Discord embed budget are shortened with a link to the full
  release notes. The complete notes remain on GitHub.
- The bot refreshes the exact release and waits up to two minutes for the installer
  if publication happens before asset upload. Upload assets while the release is a
  draft when possible. An absent installer fails the run without posting.
- The webhook's name and avatar control the posting identity; change those in Discord.
- A missing secret, wrong server, missing installer or failed API response fails the
  workflow visibly. Network failures and server errors are not automatically retried
  because Discord might already have accepted the message. Rate-limit rejections are
  retried with bounded waits.
- **Check the channel before rerunning a send.** Manual sends and reruns can duplicate
  an earlier post; there is no durable cross-run deduplication store.
- If a release is created by another Actions workflow using `GITHUB_TOKEN`, GitHub
  suppresses the downstream release trigger. Use a GitHub App/PAT for that publisher,
  or explicitly dispatch this workflow after publishing. Local `gh release create`
  with a user login can trigger it normally.
- No automatic crossposting to other servers or announcement-channel followers.

Local checks: `python -m unittest discover -s .github/discord-release -p 'test_*.py'`.
The tests use fake network responses and do not send Discord messages.

References: [GitHub release triggers](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release),
[GitHub token behaviour](https://docs.github.com/en/actions/concepts/security/github_token),
[Discord webhooks](https://docs.discord.com/developers/resources/webhook).
