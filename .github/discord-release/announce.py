"""Publish GitHub release notes through a channel webhook; Python stdlib only."""
import json
import math
import os
from pathlib import Path
import re
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlsplit, urlunsplit
from urllib.request import Request, build_opener, HTTPRedirectHandler


class AnnouncementError(Exception):
    pass


class NoRedirects(HTTPRedirectHandler):
    # Never forward a webhook token or GitHub Authorization to a redirect target.
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def request_json(url, *, method="GET", data=None, headers=None, opener=None,
                 sleep=time.sleep):
    opener = opener or build_opener(NoRedirects())
    body = json.dumps(data, ensure_ascii=False).encode("utf-8") if data is not None else None
    request = Request(url, data=body, method=method, headers={
        "User-Agent": "CampaignStudio-ReleaseAnnouncements/1.0",
        "Content-Type": "application/json", **(headers or {})})
    for attempt in range(4):
        try:
            with opener.open(request, timeout=30) as response:
                return json.loads(response.read().decode("utf-8"))
        except HTTPError as error:
            # Only rate-limit rejections are safe to retry for a message POST.
            # A timeout/5xx may have happened after Discord saved the message.
            if error.code == 429 and attempt < 3:
                try:
                    retry = json.loads(error.read()).get("retry_after", 1)
                    delay = float(retry)
                except (ValueError, TypeError, AttributeError):
                    delay = 1
                finally:
                    error.close()
                if math.isfinite(delay) and 0 <= delay <= 30:
                    sleep(max(1, delay))
                    continue
            error.close()
            raise AnnouncementError(
                f"Request failed (HTTP {error.code}). Check the destination before retrying a send."
            ) from None
        except (URLError, TimeoutError, OSError):
            raise AnnouncementError(
                "Network request failed. Check Discord before retrying: delivery may have completed."
            ) from None
        except (ValueError, UnicodeError):
            raise AnnouncementError("Service returned an invalid response; delivery is unconfirmed.") from None


def clip(text, limit):
    # Count UTF-16 units conservatively so emoji cannot exceed Discord's limits.
    text = str(text)
    raw = text.encode("utf-16-le")
    if len(raw) <= limit * 2:
        return text
    return raw[:(limit - 1) * 2].decode("utf-16-le", errors="ignore").rstrip() + "…"


def github_url(value):
    value = str(value or "")
    parsed = urlsplit(value)
    if (parsed.scheme != "https" or parsed.netloc != "github.com"
            or any(char in value for char in "\r\n<>")):
        raise AnnouncementError("Release contains an invalid GitHub link.")
    return value.replace("(", "%28").replace(")", "%29")


def make_payload(release, config):
    if release.get("draft") or not release.get("published_at"):
        raise AnnouncementError("Draft or unpublished releases are never announced.")
    release_url = github_url(release.get("html_url"))
    installer = next((a for a in release.get("assets", [])
                      if a.get("name") == config["installer_name"]
                      and a.get("state", "uploaded") == "uploaded"), None)
    if not installer:
        raise AnnouncementError("The release installer is not uploaded yet.")
    installer_url = github_url(installer.get("browser_download_url"))
    notes = (release.get("body") or "").strip()
    description = clip(notes, 3500) if notes else "Patch notes are available on the release page."
    description += f"\n\n[Full patch notes and downloads]({release_url})"
    if len(description.encode("utf-16-le")) > 4096 * 2:
        description = clip(notes or "See the release page for patch notes.", 3500)
    return {
        "allowed_mentions": {"parse": []},
        "embeds": [{
            "title": clip(release.get("name") or release.get("tag_name") or "Campaign Studio update", 256),
            "url": release_url,
            "description": description,
            "color": 0xF0AB35 if release.get("prerelease") else 0x39B878,
            "fields": [
                {"name": "Release", "value": clip(release.get("tag_name", ""), 256), "inline": True},
                {"name": "Channel", "value": "Beta / prerelease" if release.get("prerelease") else "Stable", "inline": True},
                {"name": "Download", "value": f"[Windows installer]({installer_url})"
                 if len(installer_url) < 900 else f"[Release page]({release_url})", "inline": False},
            ],
            "footer": {"text": "Tape to Tape • Campaign Studio"},
            "timestamp": release["published_at"],
        }],
    }


def validate_webhook(value):
    try:
        parsed = urlsplit(value.strip())
        if (parsed.scheme != "https" or parsed.netloc not in {"discord.com", "discordapp.com"}
                or parsed.query or parsed.fragment
                or not re.fullmatch(r"/api(?:/v\d+)?/webhooks/\d+/[A-Za-z0-9_.-]+/?", parsed.path)):
            raise ValueError()
        return urlunsplit((parsed.scheme, parsed.netloc, parsed.path.rstrip("/"), "", ""))
    except ValueError:
        raise AnnouncementError("Set DISCORD_RELEASE_WEBHOOK_URL to a Discord channel webhook URL.") from None


def deliver(payload, webhook, config, request=request_json):
    webhook = validate_webhook(webhook)
    metadata = request(webhook)
    if str(metadata.get("guild_id")) != config["guild_id"]:
        raise AnnouncementError("Webhook belongs to a different Discord server; nothing posted.")
    if config.get("channel_id") and str(metadata.get("channel_id")) != config["channel_id"]:
        raise AnnouncementError("Webhook belongs to a different Discord channel; nothing posted.")
    if metadata.get("type") != 1:
        raise AnnouncementError("Use an incoming webhook created in the release-patchnotes channel.")
    result = request(webhook + "?wait=true", method="POST", data=payload)
    if not result.get("id") or str(result.get("channel_id")) != str(metadata.get("channel_id")):
        raise AnnouncementError("Discord did not confirm the expected message; check the channel before retrying.")
    return f"https://discord.com/channels/{config['guild_id']}/{result['channel_id']}/{result['id']}"


def load_release(event, event_name, tag, config, token, send, request=request_json, sleep=time.sleep):
    repository = config["repository"]
    if event.get("repository", {}).get("full_name") != repository:
        raise AnnouncementError("Event repository does not match the configured mod repository.")
    if event_name == "release":
        if event.get("action") != "published" or event.get("release", {}).get("draft"):
            raise AnnouncementError("Only published release events are supported.")
        release_id = event.get("release", {}).get("id")
        if not isinstance(release_id, int):
            raise AnnouncementError("Release event has no valid release ID.")
        suffix = str(release_id)
    elif event_name == "workflow_dispatch" and tag.strip():
        suffix = "tags/" + quote(tag.strip(), safe="")
    else:
        raise AnnouncementError("Provide a published release event or a manual release tag.")
    url = f"https://api.github.com/repos/{repository}/releases/{suffix}"
    headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"}
    if token:
        headers["Authorization"] = "Bearer " + token
    # Some publishers upload assets just after publishing. Refresh the exact
    # release ID, never /latest (which would miss beta releases or race).
    for attempt in range(9 if send else 1):
        release = request(url, headers=headers)
        if release.get("draft") or not release.get("published_at"):
            raise AnnouncementError("Release is not published; nothing posted.")
        if any(a.get("name") == config["installer_name"] and a.get("state", "uploaded") == "uploaded"
               for a in release.get("assets", [])):
            return release
        if not send or attempt == 8:
            break
        sleep(15)
    raise AnnouncementError("Installer was not ready within two minutes; nothing posted. Retry after upload completes.")


def main():
    config = json.loads(Path(__file__).with_name("config.json").read_text(encoding="utf-8"))
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    send = os.environ.get("SEND_TO_DISCORD", "false").lower() == "true"
    release = load_release(event, os.environ.get("GITHUB_EVENT_NAME", ""),
                           os.environ.get("RELEASE_TAG", ""), config,
                           os.environ.get("GITHUB_TOKEN", ""), send)
    payload = make_payload(release, config)
    if not send:
        print(json.dumps(payload, indent=2, ensure_ascii=False))
        print("Preview only; no Discord message sent.")
    else:
        print("Posted: " + deliver(payload, os.environ.get("DISCORD_WEBHOOK_URL", ""), config))


if __name__ == "__main__":
    try:
        main()
    except (AnnouncementError, KeyError, OSError, ValueError) as error:
        # Never print arbitrary network exception text: it can contain the token.
        print(str(error) if isinstance(error, AnnouncementError) else "Invalid or missing local configuration/event file.", file=sys.stderr)
        sys.exit(1)
