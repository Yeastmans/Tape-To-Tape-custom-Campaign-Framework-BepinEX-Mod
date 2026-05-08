#!/usr/bin/env python3
"""
Custom Campaign Framework — GUI Creator

tkinter-based editor for players, teams, and campaigns. No typing needed
for skins (dropdowns), colors (pickers with live swatch), or picking from
existing campaigns (file browsers). All fields have inline hints.
"""
import os, sys, re
import tkinter as tk
from tkinter import ttk, colorchooser, messagebox
from tkinter.scrolledtext import ScrolledText

# When running as a PyInstaller .exe, __file__ points to a temp extraction folder.
# Use sys.executable's directory instead so we find campaigns/library/active.txt
# next to the actual .exe on disk.
if getattr(sys, 'frozen', False):
    SCRIPT_DIR = os.path.dirname(sys.executable)
else:
    SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)

# --- Folder layout (mod content root = SCRIPT_DIR) ---
# SCRIPT_DIR/
#   active.txt          — which campaign the game will load
#   campaigns/          — all campaign folders live here
#   library/            — shared players + teams, reusable across campaigns
CAMPAIGNS_DIR = os.path.join(SCRIPT_DIR, "campaigns")

# Create these directories on first run if they're missing. Also migrate
# older installs that had campaigns at the root and _library as hidden.
def _ensure_layout():
    os.makedirs(CAMPAIGNS_DIR, exist_ok=True)
    os.makedirs(os.path.join(SCRIPT_DIR, "library"), exist_ok=True)
    # Legacy migration: move _library/* → library/* (one-time)
    legacy_lib = os.path.join(SCRIPT_DIR, "_library")
    new_lib = os.path.join(SCRIPT_DIR, "library")
    if os.path.isdir(legacy_lib) and not os.listdir(new_lib):
        try:
            for item in os.listdir(legacy_lib):
                os.rename(os.path.join(legacy_lib, item),
                          os.path.join(new_lib, item))
            os.rmdir(legacy_lib)
        except Exception: pass
    # Legacy migration: move any campaign-looking folder at SCRIPT_DIR root
    # (has campaign.txt or teams/) into campaigns/ subfolder.
    for name in list(os.listdir(SCRIPT_DIR)):
        p = os.path.join(SCRIPT_DIR, name)
        if not os.path.isdir(p) or name in ("campaigns", "library",
                                             "_library", "_templates",
                                             "__pycache__", "dist", "build"):
            continue
        if name.startswith(".") or name.startswith("_"):
            continue
        has_campaign_marker = (os.path.isdir(os.path.join(p, "teams"))
                                or os.path.isfile(os.path.join(p, "campaign.txt")))
        if has_campaign_marker:
            target = os.path.join(CAMPAIGNS_DIR, name)
            if not os.path.exists(target):
                try: os.rename(p, target)
                except Exception: pass
_ensure_layout()

# ============================================================
#   AUTO-UPDATER (checks GitHub raw for newer VERSION.txt)
# ============================================================
APP_VERSION = "2.1.17"
UPDATE_REPO = "Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod"
UPDATE_BRANCH = "main"
UPDATE_RELEASES_API = f"https://api.github.com/repos/{UPDATE_REPO}/releases/latest"
UPDATE_VERSION_URL = f"https://raw.githubusercontent.com/{UPDATE_REPO}/{UPDATE_BRANCH}/release/VERSION.txt"
UPDATE_INSTALLER_URL = f"https://raw.githubusercontent.com/{UPDATE_REPO}/{UPDATE_BRANCH}/release/T2T_Custom_Campaign_Framework_Setup.exe"
UPDATE_RELEASES_PAGE = f"https://github.com/{UPDATE_REPO}/releases"


def _read_local_version():
    """Read installed VERSION.txt next to the exe; fall back to APP_VERSION."""
    for p in (os.path.join(SCRIPT_DIR, "VERSION.txt"),
              os.path.join(os.path.dirname(SCRIPT_DIR), "VERSION.txt")):
        try:
            with open(p, "r", encoding="utf-8") as f:
                v = (f.read() or "").strip()
                if v: return v
        except Exception: pass
    return APP_VERSION


def _parse_version(s):
    """Turn '2.1.0' into (2,1,0). Non-numeric parts sort as 0."""
    out = []
    for part in (s or "").strip().split("."):
        try: out.append(int(part))
        except Exception: out.append(0)
    while len(out) < 3: out.append(0)
    return tuple(out)


def _fetch_remote_release(timeout=6):
    """Return (version, installer_url). Prefer the latest GitHub Release
    (has a tag_name like 'v2.1.0' and the installer .exe attached as an
    asset); fall back to raw VERSION.txt on the main branch."""
    import urllib.request, json
    # 1) GitHub Releases API
    try:
        req = urllib.request.Request(UPDATE_RELEASES_API,
                                      headers={"User-Agent": "T2T-CampaignCreator",
                                               "Accept": "application/vnd.github+json"})
        with urllib.request.urlopen(req, timeout=timeout) as r:
            data = json.loads(r.read().decode("utf-8", errors="ignore"))
        tag = (data.get("tag_name") or "").strip().lstrip("vV")
        installer_url = None
        for a in data.get("assets", []) or []:
            name = (a.get("name") or "").lower()
            if name.endswith(".exe") and "setup" in name:
                installer_url = a.get("browser_download_url")
                break
        if tag:
            return tag, (installer_url or UPDATE_INSTALLER_URL)
    except Exception: pass
    # 2) Raw VERSION.txt on main
    try:
        req = urllib.request.Request(UPDATE_VERSION_URL,
                                      headers={"User-Agent": "T2T-CampaignCreator"})
        with urllib.request.urlopen(req, timeout=timeout) as r:
            v = (r.read().decode("utf-8", errors="ignore") or "").strip()
            if v: return v, UPDATE_INSTALLER_URL
    except Exception: pass
    return None, None


def _updater_log(msg):
    """Append a line to %TEMP%/t2t_updater.log so we can diagnose hangs."""
    import tempfile, time
    try:
        p = os.path.join(tempfile.gettempdir(), "t2t_updater.log")
        with open(p, "a", encoding="utf-8") as f:
            f.write(f"[{time.strftime('%H:%M:%S')}] {msg}\n")
    except Exception: pass


def _download_with_urllib(src, dest_path, progress_cb=None, cancel_flag=None,
                           connect_timeout=30):
    """Download using urllib.request.urlretrieve — simple and follows
    redirects automatically."""
    import urllib.request, socket
    socket.setdefaulttimeout(connect_timeout)
    _updater_log(f"urllib start: {src}")

    # Install opener with User-Agent (github sometimes rejects default)
    opener = urllib.request.build_opener()
    opener.addheaders = [("User-Agent", "T2T-CampaignCreator"),
                          ("Accept", "*/*")]
    urllib.request.install_opener(opener)

    def hook(count, block_size, total_size):
        if cancel_flag and cancel_flag():
            raise RuntimeError("cancelled")
        got = count * block_size
        if progress_cb:
            try: progress_cb(got, total_size if total_size > 0 else 0)
            except Exception: pass

    part = dest_path + ".part"
    urllib.request.urlretrieve(src, part, reporthook=hook)
    size = os.path.getsize(part)
    _updater_log(f"urllib done: {size} bytes")
    if size < 1024:
        raise RuntimeError(f"Downloaded only {size} bytes - likely an error page")
    os.replace(part, dest_path)


def _head_content_length(url, timeout=10):
    """Best-effort: ask GitHub how big the installer is via a HEAD request.
    Follows redirects. Returns 0 on failure."""
    import urllib.request
    try:
        req = urllib.request.Request(url, method="HEAD",
                                      headers={"User-Agent": "T2T-CampaignCreator"})
        with urllib.request.urlopen(req, timeout=timeout) as r:
            cl = r.headers.get("Content-Length")
            return int(cl) if cl else 0
    except Exception as e:
        _updater_log(f"HEAD failed: {e}")
        return 0


def _download_with_curl(src, dest_path, progress_cb=None, cancel_flag=None,
                         status_cb=None, timeout=600):
    """Download using curl.exe. Runs curl in the background with stderr
    suppressed, and polls the .part file size for progress (much more
    reliable than parsing curl's progress bar from a pipe)."""
    import subprocess, shutil, time as _time
    curl = shutil.which("curl") or r"C:\Windows\System32\curl.exe"
    if not os.path.isfile(curl):
        raise RuntimeError("curl.exe not found on this system")
    _updater_log(f"curl start: {src}")
    if status_cb: status_cb("Getting file size...")

    total = _head_content_length(src)
    _updater_log(f"HEAD content-length: {total}")
    if status_cb:
        status_cb(f"Starting download ({total//1024:,} KB)..." if total else
                  "Starting download...")

    part = dest_path + ".part"
    try: os.remove(part)
    except Exception: pass

    p = subprocess.Popen(
        [curl, "-L", "--fail", "-A", "T2T-CampaignCreator",
         "--connect-timeout", "30", "--max-time", str(timeout),
         "-s", "-o", part, src],
        stderr=subprocess.PIPE, stdout=subprocess.PIPE,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))

    last_size = 0
    last_change = _time.time()
    stall_threshold = 45  # abort if no bytes arrive for 45s
    while True:
        if cancel_flag and cancel_flag():
            try: p.terminate()
            except Exception: pass
            _updater_log("curl cancelled")
            raise RuntimeError("cancelled")
        rc = p.poll()
        try:
            size = os.path.getsize(part) if os.path.isfile(part) else 0
        except Exception:
            size = 0
        if size != last_size:
            last_size = size
            last_change = _time.time()
            if progress_cb:
                try: progress_cb(size, total)
                except Exception: pass
        elif _time.time() - last_change > stall_threshold:
            try: p.terminate()
            except Exception: pass
            _updater_log(f"curl stalled at {size} bytes")
            raise RuntimeError(
                f"Download stalled at {size//1024} KB (no activity for {stall_threshold}s)")
        if rc is not None:
            break
        _time.sleep(0.2)

    if rc != 0:
        err = (p.stderr.read() or b"").decode("ascii", errors="ignore")[:400]
        _updater_log(f"curl failed rc={rc}: {err}")
        raise RuntimeError(f"curl exited {rc}: {err.strip() or '(no output)'}")
    size = os.path.getsize(part) if os.path.isfile(part) else 0
    _updater_log(f"curl done: {size} bytes")
    if size < 1024:
        raise RuntimeError(f"Downloaded only {size} bytes")
    # Final progress tick so UI shows 100%
    if progress_cb and total > 0:
        try: progress_cb(size, total)
        except Exception: pass
    os.replace(part, dest_path)


def _download_installer(dest_path, url=None, progress_cb=None,
                         cancel_flag=None, status_cb=None, **_):
    """Download the installer. Tries curl.exe first (most reliable on
    Windows), falls back to urllib on failure or non-Windows.
    status_cb(text): optional callback for short human-readable status strings."""
    src = url or UPDATE_INSTALLER_URL
    _updater_log(f"download start: {src}")
    if status_cb: status_cb("Preparing download...")
    errors = []
    if sys.platform.startswith("win"):
        try:
            _download_with_curl(src, dest_path, progress_cb=progress_cb,
                                 cancel_flag=cancel_flag, status_cb=status_cb)
            return
        except Exception as e:
            if cancel_flag and cancel_flag(): raise
            _updater_log(f"curl failed, falling back to urllib: {e!r}")
            if status_cb: status_cb(f"curl failed ({e}) — trying urllib...")
            errors.append(f"curl: {e}")
    try:
        if status_cb: status_cb("Downloading via urllib...")
        _download_with_urllib(src, dest_path, progress_cb=progress_cb,
                               cancel_flag=cancel_flag)
        return
    except Exception as e:
        if cancel_flag and cancel_flag(): raise
        errors.append(f"urllib: {e}")
        raise RuntimeError(" | ".join(errors))


def check_for_updates_async(root, silent=True):
    """Kick off a background version check. On a newer remote, prompt on
    the tk main thread. If silent=False, also notify when up-to-date."""
    import threading

    def _worker():
        try:
            local = _read_local_version()
            remote, installer_url = _fetch_remote_release()
            if not remote:
                if not silent:
                    root.after(0, lambda: messagebox.showwarning(
                        "Update check",
                        "Could not reach GitHub to check for updates."))
                return
            if _parse_version(remote) <= _parse_version(local):
                if not silent:
                    root.after(0, lambda: messagebox.showinfo(
                        "Up to date",
                        f"You're on the latest version ({local})."))
                return
            root.after(0, lambda: _prompt_update(root, local, remote, installer_url))
        except Exception as e:
            if not silent:
                root.after(0, lambda: messagebox.showerror("Update check",
                                                            f"Update check failed: {e}"))

    threading.Thread(target=_worker, daemon=True).start()


def _prompt_update(root, local, remote, installer_url=None):
    if not messagebox.askyesno(
        "Update available",
        f"A newer version of the Custom Campaign Framework is available.\n\n"
        f"Installed: {local}\n"
        f"Available: {remote}\n\n"
        f"Download and launch the installer now?",
        parent=root):
        return

    import tempfile, threading as _t, webbrowser, time as _time
    tmp = os.path.join(tempfile.gettempdir(),
                       f"T2T_Custom_Campaign_Framework_Setup_{remote}.exe")
    _updater_log(f"update prompt accepted: url={installer_url}, tmp={tmp}")

    # If anything in dialog construction fails, surface the traceback to the
    # file log so we can diagnose — otherwise tkinter silently swallows it
    # and the window appears blank.
    try:
        dlg = tk.Toplevel(root)
        dlg.title(f"Downloading update v{remote}")
        dlg.transient(root)
        dlg.resizable(True, True)
        _fit_geometry(dlg, 620, 460)

        header = tk.Label(dlg, text=f"Downloading Custom Campaign Framework v{remote}",
                           font=("", 10, "bold"), anchor="w")
        header.pack(fill="x", padx=16, pady=(14, 2))
        subhead = tk.Label(dlg, text=f"Saving to: {tmp}",
                            fg="#666", font=("", 8), anchor="w")
        subhead.pack(fill="x", padx=16, pady=(0, 8))

        pb = ttk.Progressbar(dlg, orient="horizontal", mode="indeterminate",
                              maximum=100)
        pb.pack(fill="x", padx=16, pady=(0, 4))

        status = tk.Label(dlg, text="Preparing download...",
                           font=("", 9), anchor="w")
        status.pack(fill="x", padx=16, pady=(0, 2))
        size_lbl = tk.Label(dlg, text="", fg="#0066aa",
                             font=("", 9, "bold"), anchor="w")
        size_lbl.pack(fill="x", padx=16, pady=(0, 6))

        # Scrolling log of what the downloader is doing — so user can see the
        # process isn't frozen even before the first bytes arrive.
        log_frame = ttk.LabelFrame(dlg, text=" Activity log ")
        log_frame.pack(fill="both", expand=True, padx=16, pady=(4, 8))
        log_text = tk.Text(log_frame, height=8, width=70, wrap="word",
                            font=("Consolas", 8), bg="#f5f5f5", bd=0)
        log_scroll = ttk.Scrollbar(log_frame, command=log_text.yview)
        log_text.configure(yscrollcommand=log_scroll.set, state="disabled")
        log_scroll.pack(side="right", fill="y")
        log_text.pack(side="left", fill="both", expand=True, padx=4, pady=4)

        # Force tkinter to render widgets NOW so the user sees something
        # before the download thread starts blocking on network.
        dlg.update_idletasks()
        dlg.update()
        try: pb.start(15)
        except Exception as _e: _updater_log(f"pb.start failed: {_e}")
        _updater_log("dialog built OK, widgets rendered")
    except Exception as _e:
        import traceback as _tb
        _updater_log(f"dialog setup FAILED: {_e!r}\n{_tb.format_exc()}")
        messagebox.showerror("Update dialog error",
            f"Could not build the update dialog:\n{_e}\n\n"
            f"Download manually from:\n{UPDATE_RELEASES_PAGE}")
        return

    def _append_log(msg):
        def _ui():
            try:
                log_text.configure(state="normal")
                ts = _time.strftime("%H:%M:%S")
                log_text.insert("end", f"[{ts}] {msg}\n")
                log_text.see("end")
                log_text.configure(state="disabled")
            except Exception: pass
        try: root.after(0, _ui)
        except Exception: pass

    cancelled = {"v": False}

    btn_row = ttk.Frame(dlg)
    btn_row.pack(pady=(2, 12))

    def _open_page():
        try: webbrowser.open(UPDATE_RELEASES_PAGE)
        except Exception: pass

    def _cancel():
        cancelled["v"] = True
        _append_log("Cancelled by user")
        try: dlg.destroy()
        except Exception: pass

    ttk.Button(btn_row, text="Open download page",
               command=_open_page).pack(side="left", padx=4)
    ttk.Button(btn_row, text="Cancel",
               command=_cancel).pack(side="left", padx=4)

    # Kick log with the URL + target so user can see what's happening
    _append_log(f"Requesting v{remote} from GitHub")
    _append_log(f"URL: {installer_url or UPDATE_INSTALLER_URL}")
    _append_log(f"Target: {tmp}")
    _updater_log("initial log entries written to UI")

    def _fmt_bytes(n):
        if n >= 1024*1024: return f"{n/(1024*1024):.2f} MB"
        if n >= 1024: return f"{n/1024:.0f} KB"
        return f"{n} B"

    def _status(text):
        def _ui():
            try: status.config(text=text)
            except Exception: pass
        try: root.after(0, _ui)
        except Exception: pass
        _append_log(text)

    def _progress(got, total):
        def _ui():
            try:
                if total > 0 and got <= total:
                    # Switch bar to determinate mode the first time we know the size
                    if str(pb["mode"]) != "determinate":
                        try: pb.stop()
                        except Exception: pass
                        pb.config(mode="determinate", maximum=100)
                    pct = got * 100 / total
                    pb["value"] = pct
                    size_lbl.config(
                        text=f"{_fmt_bytes(got)} of {_fmt_bytes(total)}  ({pct:.1f}%)")
                else:
                    size_lbl.config(text=f"{_fmt_bytes(got)} downloaded")
            except Exception: pass
        try: root.after(0, _ui)
        except Exception: pass

    def _launch(path):
        _updater_log(f"launching: {path}")
        _append_log(f"Launching installer: {os.path.basename(path)}")
        if sys.platform.startswith("win"):
            try:
                os.startfile(path)
                return True
            except Exception as e:
                _updater_log(f"startfile failed: {e}")
                _append_log(f"os.startfile failed: {e}")
        try:
            import subprocess
            subprocess.Popen([path], shell=False)
            return True
        except Exception as e:
            _updater_log(f"Popen failed: {e}")
            _append_log(f"Popen failed: {e}")
            return False

    def _do():
        try:
            _download_installer(tmp, url=installer_url, progress_cb=_progress,
                                 cancel_flag=lambda: cancelled["v"],
                                 status_cb=_status)
            if cancelled["v"]:
                _updater_log("post-cancel, not launching")
                return
            _status("Download complete — launching installer")
            def _done():
                try:
                    pb.stop()
                    pb.config(mode="determinate", maximum=100, value=100)
                except Exception: pass
                # Let the user see "complete" briefly before launching
                def _fire():
                    try: dlg.destroy()
                    except Exception: pass
                    ok = _launch(tmp)
                    if ok:
                        root.after(400, lambda: os._exit(0))
                    else:
                        messagebox.showerror(
                            "Install launch failed",
                            "Downloaded but could not launch the installer.\n\n"
                            f"Run it manually from:\n{tmp}\n\n"
                            f"Log: %TEMP%\\t2t_updater.log")
                root.after(600, _fire)
            root.after(0, _done)
        except Exception as e:
            _updater_log(f"download error: {e!r}")
            _append_log(f"ERROR: {e}")
            def _err():
                try:
                    pb.stop()
                    pb.config(mode="determinate", value=0)
                except Exception: pass
                if cancelled["v"]: return
                if messagebox.askyesno(
                    "Download failed",
                    f"Could not download the installer:\n\n{e}\n\n"
                    f"Log: %TEMP%\\t2t_updater.log\n\n"
                    f"Open the download page in your browser instead?",
                    parent=dlg):
                    _open_page()
                try: dlg.destroy()
                except Exception: pass
            root.after(0, _err)

    _updater_log("starting download thread")
    try:
        _t.Thread(target=_do, daemon=True).start()
        _updater_log("download thread started")
    except Exception as _e:
        _updater_log(f"thread start FAILED: {_e!r}")

# ============================================================
#   DIRTY TRACKING (unsaved-changes warning)
# ============================================================
class DirtyTracker:
    """Per-tab dirty-flag manager. Editors create one, hook its mark_dirty
    to their inputs, call mark_clean() after loading and after saving, and
    expose save_fn so the close handler can offer to save before closing.

    save_fn should return True on successful save (so the tab can close),
    False/None if save failed (tab stays open)."""
    __slots__ = ("dirty", "save_fn", "title")
    def __init__(self, title="tab"):
        self.dirty = False
        self.save_fn = None
        self.title = title

    def mark_dirty(self, *_):
        self.dirty = True

    def mark_clean(self, *_):
        self.dirty = False

    def is_dirty(self):
        return self.dirty


def attach_dirty_tracking(frame, tracker):
    """Walk frame's descendants and wire common inputs to tracker.mark_dirty.
    Call this AFTER the form is built and initial values loaded. Then call
    tracker.mark_clean() once to reset any writes the loader triggered."""
    if frame is None or tracker is None: return
    def visit(w):
        try: cls = w.winfo_class()
        except Exception: return
        try:
            if cls in ("TEntry", "Entry", "TCombobox", "Spinbox", "TSpinbox"):
                w.bind("<KeyRelease>", tracker.mark_dirty, add="+")
                w.bind("<<ComboboxSelected>>", tracker.mark_dirty, add="+")
                w.bind("<FocusOut>", tracker.mark_dirty, add="+")
                w.bind("<<Paste>>", tracker.mark_dirty, add="+")
            elif cls in ("Text",):
                try: w.edit_modified(False)
                except Exception: pass
                def _on_mod(e, widget=w, tr=tracker):
                    tr.mark_dirty()
                    try: widget.edit_modified(False)
                    except Exception: pass
                w.bind("<<Modified>>", _on_mod, add="+")
            elif cls in ("TCheckbutton", "Checkbutton",
                         "TRadiobutton", "Radiobutton",
                         "TScale", "Scale"):
                w.bind("<ButtonRelease-1>", tracker.mark_dirty, add="+")
                w.bind("<space>", tracker.mark_dirty, add="+")
        except Exception: pass
        for c in w.winfo_children():
            visit(c)
    visit(frame)


def confirm_tab_close(master, tab):
    """Return True if tab is safe to close (user said yes/no, or clean).
    Return False if user cancelled. If there's a dirty tracker + save_fn
    and the user picks Yes, calls save_fn; abort close if save fails."""
    tracker = getattr(tab, "_dirty_tracker", None)
    if not tracker or not tracker.is_dirty():
        return True
    title = tracker.title or "this tab"
    ans = messagebox.askyesnocancel(
        "Unsaved changes",
        f"'{title}' has unsaved changes.\n\nSave before closing?",
        parent=master)
    if ans is None: return False
    if not ans: return True  # discard
    if not tracker.save_fn:
        return True
    try:
        ok = tracker.save_fn()
    except Exception as e:
        messagebox.showerror("Save failed",
                              f"Could not save:\n{e}", parent=master)
        return False
    return bool(ok) if ok is not None else True


# ============================================================
#   COMMUNITY UPLOADS (Dropbox via Cloudflare Worker proxy)
# ============================================================
# The worker holds the Dropbox credentials. Creator never sees them — it
# only knows this public URL. POST /upload ships a zip + metadata.
# GET /list returns available campaigns. GET /download?path=X streams a zip.
COMMUNITY_WORKER_URL = "https://t2tcampaings.shaespring14.workers.dev"


def _community_list(timeout=10):
    """Return list of { name, path, size, modified } dicts, or raise."""
    import urllib.request, json as _json
    req = urllib.request.Request(COMMUNITY_WORKER_URL + "/list",
                                  headers={"User-Agent": "T2T-CampaignCreator"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        data = _json.loads(r.read().decode("utf-8", errors="ignore"))
    if not data.get("ok"):
        raise RuntimeError(data.get("error") or "list failed")
    return data.get("items", [])


def _community_download(path, dest_path, timeout=60):
    """Stream a campaign zip from the Dropbox folder to dest_path."""
    import urllib.request, urllib.parse
    url = COMMUNITY_WORKER_URL + "/download?path=" + urllib.parse.quote(path)
    req = urllib.request.Request(url, headers={"User-Agent": "T2T-CampaignCreator"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        with open(dest_path, "wb") as f:
            while True:
                chunk = r.read(65536)
                if not chunk: break
                f.write(chunk)


def _community_upload(zip_path, display_name, meta_json="", timeout=120):
    """Upload a zip to Dropbox via the worker. Returns the uploaded path on Dropbox."""
    import urllib.request, json as _json, mimetypes, uuid
    boundary = "----T2T" + uuid.uuid4().hex
    with open(zip_path, "rb") as f:
        file_bytes = f.read()
    parts = []
    def _add_text(name, value):
        parts.append(f"--{boundary}\r\n".encode())
        parts.append(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode())
        parts.append((value + "\r\n").encode())
    def _add_file(name, fname, data):
        parts.append(f"--{boundary}\r\n".encode())
        parts.append(f'Content-Disposition: form-data; name="{name}"; filename="{fname}"\r\n'.encode())
        parts.append(b'Content-Type: application/zip\r\n\r\n')
        parts.append(data)
        parts.append(b"\r\n")
    _add_text("name", display_name)
    _add_text("meta", meta_json or "{}")
    _add_file("file", os.path.basename(zip_path), file_bytes)
    parts.append(f"--{boundary}--\r\n".encode())
    body = b"".join(parts)
    req = urllib.request.Request(
        COMMUNITY_WORKER_URL + "/upload",
        data=body,
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "User-Agent": "T2T-CampaignCreator",
        },
        method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        resp = _json.loads(r.read().decode("utf-8", errors="ignore"))
    if not resp.get("ok"):
        raise RuntimeError(resp.get("error") or "upload failed")
    return resp.get("path", "")


def _zip_campaign(campaign_dir, dest_zip, exclude=("save.txt",)):
    """Zip a campaign folder (skipping listed names at any depth)."""
    import zipfile
    base = os.path.dirname(campaign_dir)
    with zipfile.ZipFile(dest_zip, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, dirs, files in os.walk(campaign_dir):
            # Normalize excludes — applies to any file whose basename matches
            for fn in files:
                if fn in exclude: continue
                full = os.path.join(root, fn)
                arc = os.path.relpath(full, base)
                zf.write(full, arc)


def _extract_campaign_zip(zip_path, dest_campaigns_dir):
    """Extract a campaign zip. Returns the top-level folder name installed
    (assumes the zip contains a single folder at its root). Existing folders
    are NOT overwritten — caller should handle collision resolution."""
    import zipfile
    with zipfile.ZipFile(zip_path, "r") as zf:
        names = zf.namelist()
        # Detect top-level folder name (first path segment)
        top = None
        for n in names:
            seg = n.split("/", 1)[0]
            if seg and (top is None or seg == top):
                top = seg
            elif seg != top:
                top = None
                break
        if not top:
            raise RuntimeError("Zip doesn't have a single top-level folder")
        target = os.path.join(dest_campaigns_dir, top)
        if os.path.exists(target):
            raise FileExistsError(target)
        zf.extractall(dest_campaigns_dir)
        return top


try:
    from _game_data import (ALL_TALENTS, ALL_RELICS, TALENTS, RELICS,
                             GOALIE_TALENTS, ALL_GOALIE_TALENTS)
except Exception:
    ALL_TALENTS = []
    ALL_RELICS = []
    TALENTS = []
    RELICS = []
    GOALIE_TALENTS = []
    ALL_GOALIE_TALENTS = []

# ============================================================
#   VALUE REGISTRIES (pulled from VALID_VALUES.txt / Plugin.cs)
# ============================================================
BODY_SKINS = [
    "standard",         # colorable — takes jersey primary/secondary/accent
    "tycoons",          # business suit (fixed look)
    "princess",         # armored dress (fixed)
    "golfers",          # polo shirt (fixed)
    "prisoners",        # jumpsuit (fixed)
    "mountaineers",     # lederhosen (fixed)
    "mountaineers beer", # lederhosen + beer (fixed)
    "hockey fc",        # soccer jersey (fixed)
    "figure skaters",   # figure skater (fixed)
    "referee",          # ref stripes (fixed)
    "random body",      # random each game
]
HELMET_SKINS = [
    "team colors",      # colorable — takes helmet color fields
    "cage",             # face cage (fixed)
    "none",             # no helmet (bare head)
    "random helmet",
]
STICK_SKINS = [
    "black", "gold", "red", "purple", "teal", "red gold",
    "sword", "golf",
    "team stick",       # colorable — takes stick color
    "random stick",
]
SKATE_SKINS = [
    "standard",         # Body_Skates/Customization/Customization_colors — colorable (body/blade/laces)
    "random skates",    # randomly picks from the above
]
GLOVES_SKINS = [
    "standard",         # colorable — takes gloves color
]
PANTS_SKINS = [
    "standard",         # colorable — takes pants color
]
BICEP_SKINS = [
    "standard",         # colorable — takes bicep color
    "crusaders", "figure_skaters",
    "golfers", "hockey_fc", "mountaineers_black", "mountaineers_white",
    "princess", "prisoners", "referees", "tycoons",
]

# Player-override-specific lists. Prepend a "(use team default)" marker
# so an empty-looking override is obvious. Rename "standard" →
# "team colors" so it matches the Helmet naming.
def _override_list(base, standard_label="team colors"):
    out = ["(use team default)"]
    for v in base:
        if v == "standard":
            out.append(standard_label)
        else:
            out.append(v)
    return out

OV_BODY_SKINS   = _override_list(BODY_SKINS)
OV_HELMET_SKINS = ["(use team default)"] + HELMET_SKINS
OV_STICK_SKINS  = ["(use team default)"] + STICK_SKINS
OV_SKATE_SKINS  = _override_list(SKATE_SKINS)
OV_BICEP_SKINS  = _override_list(BICEP_SKINS)
GOALIE_HELMET_SKINS = [
    "team colors", "canadians", "cheese", "cultists", "disco",
    "figure_skaters", "golfers", "hockey_fc", "knights", "meatballs",
    "mountaineers", "princess", "prisoners", "referees", "toronto", "tycoons",
]
GOALIE_BODY_SKINS = [
    "team colors", "figure_skaters", "golfers", "hockey_fc", "knights",
    "mountaineers", "princess", "prisoners", "referees", "tycoons",
]
GOALIE_GLOVE_SKINS = ["team colors", "brown", "figure_skaters", "golfers", "hockey_fc", "knights", "tycoons"]
GOALIE_BLOCKER_SKINS = ["team colors", "brown", "figure_skaters", "golfers", "knights", "tycoons"]
GOALIE_PADS_SKINS = ["team colors", "brown", "figure_skaters", "hockey_fc", "tycoons"]
GOALIE_STICK_SKINS = ["team colors", "figure_skaters", "tycoons"]
# Legacy alias kept so old configs that wrote "standard" still resolve to
# the same option when loaded (normalized to "team colors" on read).
_GOALIE_LEGACY_STANDARD = "standard"
SIZES = ["ExtraSmall", "Small", "Medium", "Big", "ExtraBig", "ExtraExtraBig", "random"]
SKIN_COLORS = ["light", "dark", "random"]
YESNO_RANDOM = ["no", "yes", "random"]
POSITIONS = ["Goalie", "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense"]
LINE2_POSITIONS = ["Line 2 Left Wing", "Line 2 Right Wing", "Line 2 Center", "Line 2 Left Defense", "Line 2 Right Defense"]

# Abilities pulled from ALL_ABILITIES.txt dump
ABILITIES = [
    "none",  # no ability
    "Dash", "Dash LVL2",
    "Disco Dance", "Disco Dance LVL2",
    "Dragon Shout", "Dragon Shout LVL2",
    "Gas", "Gas LVL2",
    "Grappling Hook", "Grappling Hook LVL2",
    "Head Smasher", "Head Smasher LVL2",
    "Headshot Redirect", "Headshot Redirect LVL2",
    "Jump LVL2",
    "Kazoo LVL2",
    "Magneto LVL2",
    "Polymorphic Ability",
    "Slowmo", "Slowmo LVL2",
    "StickThief",
    "Yeet", "Yeet LVL2",
    "YoYo LVL2",
    "defenseTeleport",
    "deke",
    "enrageAbility",
    "explodingPuck",
    "fakePuck", "fakePuck LVL2",
    "fakeshot",
    "gravityWell", "gravityWell LVL2",
    "immunity", "immunity LVL2",
    "jump",
    "kazoo",
    "magneto",
    "megaLob",
    "megaPokecheck",
    "probe",
    "spinorama",
    "throwingStick", "throwingStick LVL2",
    "toedrag", "toedraglvl2",
    "tornado",
    "wet_towel",
    "yoyo",
    "yoyoShot",
    "zap",
]

# Goalie talents pulled from dumped game data
GOALIE_TALENTS_COMMON = [
    "Goalie Enraged On Goal", "Goalie Enraged On Shot",
    "Goalie Enraged On Breakaway", "Goalie Enrage First30Sec",
    "Goalie Enrage Last30Sec", "Goalie Pass Rebound", "Goalie Pass Propel",
    "Goalie Speed Talent", "Goalie Headshot", "Goalie Throw Stick",
    "Goalie Assist", "Goalie Dance", "Goalie Fart", "Always Catch Pucks",
    "Crease Clearer", "Mega Rebound", "Musical Nets", "Musical Nets (Level 2)",
    "Shutout",
]
# Face short names. The mod's ResolveSkin resolves these to the full paths —
# so writing "Captain" is equivalent to "Faces/Canadians/Captain".
# Grouped by team for browsing; alphabetical within each group.
FACES = [
    "Helmet_Face",                  # Helmet head (default for goalies)
    "none",                         # no face override (use team default)
    "random",                       # random each match
    # Angus Events
    "Angus_Bald", "Angus_Chad", "Angus_Speed", "Angus_Trio",
    # Any Team (bench / misc)
    "Bench_Bench", "Bench_Brewster", "Bench_Buttface",
    "Bench_Buttface_Angus", "Bench_Buttface_Rambo",
    "Bench_Kirby", "Bench_Kovalski", "Bench_Stumple", "Bench_Stumple_Helmet",
    "Chickensneeze", "Nasher", "Onepunch", "Referee_Old",
    # Canadians
    "Captain", "Gratz", "Poule",
    # Chicago
    "Angus", "Angus_Pixel", "Chapstick", "Chicos", "Grohl", "Louder", "Rory",
    # Cultists
    "Cultist", "Dord_Evil", "Jelly_Evil", "Rory_Evil",
    # Disco
    "Oioioi",
    # Figure Skaters
    "FigureSkaterbig", "FigureSkatersmall", "Figure_Skater_Vanilla",
    # Golfers
    "Golfer_Elite", "Golfer_Gillman", "Golfer_Lady",
    "Golfer_Ramirez", "Golfer_Whacker",
    # Hockey FC
    "Backham", "Ehrhoffaldo", "Icekicks", "Knudribble", "Maroondona",
    "Messier", "OHenry", "Ronaldo", "Zidanejad",
    # Knights
    "Lancelov", "Lancelov_Helmless", "Prince", "Red_Knight_Helmetless",
    # Midwest
    "Amber", "Brie", "Mental", "Rochefort",
    # Mountaineers
    "Furter", "Krupp", "Pianist", "Torte", "Wurst",
    # Princess
    "Boni", "Clementine", "Joan",
    # Prisoners
    "Averell", "Dalton", "Joe", "Ma",
    # Referees
    "Gedeon",
    # Toronto
    "Dord", "Jelly", "Kilmore", "Mathieu", "Popping", "Spark",
    # Twinfalls
    "Crockett", "Haggis", "Jerky", "Wiener",
    # Tycoons
    "Tycoons_Elite", "Tycoons_Lady", "Tycoons_Large", "Tycoons_Small",
]

# Logo skins (handedness matters — L vs R for stick side)
LOGO_SKINS = [
    "Team_Logo/Custom_R",   # Right-handed player (default)
    "Team_Logo/Custom_L",   # Left-handed player
]

# Number skins (jersey numbers — one per number)
# Format: Numbers/Number_NN or Number_NNLH for left-handed
NUMBER_SKINS = [f"Numbers/Number_{n}" for n in range(1, 100)] + [f"Numbers/Number_{n}LH" for n in range(1, 100)]

# Glasses (optional eyewear)
GLASSES_SKINS = [
    "none",                 # no glasses
    "Glasses/Round",
    "Glasses/Square",
    "Glasses/Aviator",
    "Glasses/Sunglasses",
]


# ============================================================
#   FILE I/O
# ============================================================
def read_kv(path):
    """Read key=value file into dict."""
    d = {}
    if not os.path.isfile(path):
        return d
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if not s or s.startswith("#") or "=" not in s:
                continue
            k, v = s.split("=", 1)
            d[k.strip()] = v.strip()
    return d


def write_kv(path, data, order=None, header=None):
    """Write dict to key=value file preserving order if given."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        if header:
            for line in header.split("\n"):
                f.write("# " + line + "\n")
        keys = order if order else data.keys()
        for k in keys:
            if k in data and data[k] != "":
                f.write(f"{k:24s}= {data[k]}\n")


LIBRARY_SOURCE = "All Players"  # Pseudo-campaign that surfaces library/players/
GAME_TEAM_NAMES_FILE = os.path.join(SCRIPT_DIR, "_game_team_names.txt")
GAME_PLAYER_NAMES_FILE = os.path.join(SCRIPT_DIR, "_game_player_names.txt")
GAME_TEAM_LOGOS_FILE = os.path.join(SCRIPT_DIR, "_game_team_logos.txt")

# Reward-pool files (written by the DLL to BepInEx/plugins/ each launch).
# Used by the Reward Pools editor to populate per-relic / per-talent checkboxes.
def _find_plugins_dir():
    """Locate BepInEx/plugins/ so we can read _reward_relics.txt / _reward_talents.txt."""
    candidates = [
        os.path.abspath(os.path.join(SCRIPT_DIR, "..")),  # Custom Campaigns Mod is inside plugins
        r"C:/Steam/steamapps/common/Tape to Tape/BepInEx/plugins",
        os.path.expandvars(r"%ProgramFiles(x86)%/Steam/steamapps/common/Tape to Tape/BepInEx/plugins"),
    ]
    for c in candidates:
        if c and os.path.isdir(c):
            return c
    return SCRIPT_DIR

_PLUGINS_DIR = _find_plugins_dir()
REWARD_RELICS_FILE = os.path.join(_PLUGINS_DIR, "_reward_relics.txt")
REWARD_TALENTS_FILE = os.path.join(_PLUGINS_DIR, "_reward_talents.txt")


def load_reward_relic_list():
    """Return list of (id, display_name, category, in_default_pool) from the
    DLL dump. in_default_pool is True if the relic normally shows up as a
    random reward (i.e. it's in RelicRepository.usedInCampaignPoolRelics).
    Empty list if the game hasn't been launched with the mod yet."""
    if not os.path.isfile(REWARD_RELICS_FILE): return []
    out = []
    try:
        with open(REWARD_RELICS_FILE, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"): continue
                parts = line.split("|")
                if len(parts) < 2: continue
                rid = parts[0].strip()
                name = parts[1].strip() or rid
                cat = parts[2].strip() if len(parts) >= 3 else ""
                in_pool = (parts[3].strip() == "1") if len(parts) >= 4 else True
                out.append((rid, name, cat, in_pool))
    except Exception: pass
    return out


def load_reward_talent_list():
    """Return list of (id, display_name, in_default_pool) from the DLL dump."""
    if not os.path.isfile(REWARD_TALENTS_FILE): return []
    out = []
    try:
        with open(REWARD_TALENTS_FILE, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"): continue
                parts = line.split("|")
                if len(parts) < 1: continue
                tid = parts[0].strip()
                name = parts[1].strip() if len(parts) >= 2 else tid
                in_pool = (parts[2].strip() == "1") if len(parts) >= 3 else True
                out.append((tid, name or tid, in_pool))
    except Exception: pass
    return out


def read_reward_pools(campaign_dir):
    """Read campaign's reward_pools.txt. Returns (excluded_relics_set, excluded_talents_set)."""
    ex_r, ex_t = set(), set()
    if not campaign_dir: return ex_r, ex_t
    path = os.path.join(campaign_dir, "reward_pools.txt")
    if not os.path.isfile(path): return ex_r, ex_t
    try:
        with open(path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line: continue
                k, _, v = line.partition("=")
                k = k.strip().lower()
                vals = [p.strip() for p in v.split(",") if p.strip()]
                if k in ("excluded relics", "excluded relic"): ex_r.update(vals)
                elif k in ("excluded talents", "excluded talent"): ex_t.update(vals)
    except Exception: pass
    return ex_r, ex_t


def write_reward_pools(campaign_dir, excluded_relics, excluded_talents):
    """Write campaign's reward_pools.txt."""
    if not campaign_dir: return
    os.makedirs(campaign_dir, exist_ok=True)
    path = os.path.join(campaign_dir, "reward_pools.txt")
    with open(path, "w", encoding="utf-8") as f:
        f.write("# Reward pool exclusions — entries here are REMOVED from random reward picks\n")
        f.write("# (relic / talent ids match _reward_relics.txt / _reward_talents.txt)\n\n")
        if excluded_relics:
            f.write(f"Excluded Relics         = {', '.join(sorted(excluded_relics))}\n")
        if excluded_talents:
            f.write(f"Excluded Talents        = {', '.join(sorted(excluded_talents))}\n")


def get_game_team_names():
    """Return sorted list of in-game team names from the name-list file.
       Generated automatically when the game launches with the mod installed."""
    if os.path.isfile(GAME_TEAM_NAMES_FILE):
        try:
            with open(GAME_TEAM_NAMES_FILE, "r", encoding="utf-8") as f:
                return [line.strip() for line in f if line.strip()]
        except Exception: pass
    return []


def get_all_team_names():
    """Return game team names + library team names + base game team names merged and sorted."""
    names = set(get_game_team_names())
    if os.path.isdir(TEAM_LIBRARY_DIR):
        for d in os.listdir(TEAM_LIBRARY_DIR):
            if os.path.isdir(os.path.join(TEAM_LIBRARY_DIR, d)):
                names.add(d)
    for sub in ("Base Game Teams", "Custom Teams (in-game editor)"):
        bg_dir = os.path.join(LIBRARY_DIR, sub)
        if os.path.isdir(bg_dir):
            for d in os.listdir(bg_dir):
                if os.path.isdir(os.path.join(bg_dir, d)):
                    names.add(d)
    return sorted(names)


def get_game_team_logos():
    """Return sorted list of dumped CustomLogos PNG names (no extension).

    The DLL writes this file next to the team/player name lists every launch,
    after it exports each in-game team logo to the game's CustomLogos folder.
    """
    if os.path.isfile(GAME_TEAM_LOGOS_FILE):
        try:
            with open(GAME_TEAM_LOGOS_FILE, "r", encoding="utf-8") as f:
                return [line.strip() for line in f if line.strip()]
        except Exception: pass
    return []


def get_game_player_names():
    """Return sorted list of in-game player names from the simple name-list."""
    names = []
    if os.path.isfile(GAME_PLAYER_NAMES_FILE):
        try:
            with open(GAME_PLAYER_NAMES_FILE, "r", encoding="utf-8") as f:
                names = [line.strip() for line in f if line.strip()]
        except Exception: pass
    return names


def get_all_player_names():
    """Return game player names + library player names + base game player names merged and sorted."""
    names = set(get_game_player_names())
    # Library players
    if os.path.isdir(PLAYER_LIBRARY_DIR):
        for f in os.listdir(PLAYER_LIBRARY_DIR):
            if f.endswith(".txt"):
                names.add(f[:-4])
    # Auto-generated player folders
    for sub in ("Base Game Players", "Custom Players (in-game editor)"):
        bg_dir = os.path.join(LIBRARY_DIR, sub)
        if os.path.isdir(bg_dir):
            for f in os.listdir(bg_dir):
                if f.endswith(".txt") and not f.startswith("_"):
                    names.add(f[:-4])
    return sorted(names)


def resolve_library_player_path(filename):
    """Find a player file across all library subfolders. Returns full path or None."""
    for loc in [PLAYER_LIBRARY_DIR,
                os.path.join(LIBRARY_DIR, "Base Game Players"),
                os.path.join(LIBRARY_DIR, "Custom Players (in-game editor)")]:
        p = os.path.join(loc, filename)
        if os.path.isfile(p):
            return p
    return None


def resolve_library_team_dir(team_name):
    """Find a team folder across all library subfolders. Returns full path or None."""
    for loc in [TEAM_LIBRARY_DIR,
                os.path.join(LIBRARY_DIR, "Base Game Teams"),
                os.path.join(LIBRARY_DIR, "Custom Teams (in-game editor)")]:
        p = os.path.join(loc, team_name)
        if os.path.isdir(p):
            return p
    return None


def is_base_game_path(path):
    """Return True if path is inside a read-only auto-generated subfolder."""
    norm = os.path.normpath(path).lower()
    for sub in ("base game teams", "base game players",
                "custom teams (in-game editor)", "custom players (in-game editor)"):
        if os.sep + sub.lower() + os.sep in norm or norm.endswith(os.sep + sub.lower()):
            return True
    return False


def auto_copy_to_library(src_path, is_team=False):
    """If src_path is in a base-game/auto-generated folder, copy it to the
       user's library folder and return the new path. Otherwise return src_path as-is."""
    if not is_base_game_path(src_path):
        return src_path
    import shutil
    name = os.path.basename(src_path)
    if is_team:
        dst = os.path.join(TEAM_LIBRARY_DIR, name)
        if os.path.exists(dst):
            dst = os.path.join(TEAM_LIBRARY_DIR, deduplicate_dir(name, TEAM_LIBRARY_DIR))
        os.makedirs(TEAM_LIBRARY_DIR, exist_ok=True)
        shutil.copytree(src_path, dst)
        return dst
    else:
        os.makedirs(PLAYER_LIBRARY_DIR, exist_ok=True)
        base = name[:-4] if name.endswith(".txt") else name
        safe = deduplicate_name(base, PLAYER_LIBRARY_DIR)
        dst = os.path.join(PLAYER_LIBRARY_DIR, safe + ".txt")
        shutil.copy2(src_path, dst)
        return dst


def import_game_team_to_library(team_name):
    """Create a library team that imports from the given game team name.
       The game resolves Import Team at runtime — no full dump needed.
       Returns the library path, or raises ValueError."""
    name = team_name.strip()
    if not name:
        raise ValueError("Team name is empty.")
    safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
    team_dir = os.path.join(TEAM_LIBRARY_DIR, safe)
    os.makedirs(os.path.join(team_dir, "players"), exist_ok=True)

    team_data = {}
    team_data["Team Name"] = name
    team_data["Import Team"] = name
    team_data["Logo From"] = name
    write_kv(os.path.join(team_dir, "team.txt"), team_data, order=TEAM_FIELD_ORDER)

    return team_dir


ACTIVE_CAMPAIGN_PATH = os.path.join(SCRIPT_DIR, "active.txt")


def deduplicate_name(base_name, existing_dir, ext=".txt"):
    """If base_name + ext already exists in existing_dir, append 1, 2, 3... until unique."""
    safe = re.sub(r'[<>:"/\\|?*]', '_', base_name).strip()
    if not os.path.exists(os.path.join(existing_dir, safe + ext)):
        return safe
    i = 1
    while os.path.exists(os.path.join(existing_dir, f"{safe}{i}{ext}")):
        i += 1
    return f"{safe}{i}"


def deduplicate_dir(base_name, parent_dir):
    """If base_name folder already exists in parent_dir, append 1, 2, 3... until unique."""
    safe = re.sub(r'[<>:"/\\|?*]', '_', base_name).strip()
    if not os.path.exists(os.path.join(parent_dir, safe)):
        return safe
    i = 1
    while os.path.exists(os.path.join(parent_dir, f"{safe}{i}")):
        i += 1
    return f"{safe}{i}"


def open_in_file_explorer(path):
    """Open the given folder (or parent folder of a file) in the OS file browser.
       Works on Windows, macOS, and Linux."""
    import subprocess
    if not path or not os.path.exists(path):
        messagebox.showwarning("Not found", f"Can't open — path doesn't exist:\n{path}")
        return
    target = path if os.path.isdir(path) else os.path.dirname(path)
    try:
        if sys.platform == "win32":
            # Use explorer with /select for files, plain open for folders
            if os.path.isdir(path):
                os.startfile(target)
            else:
                subprocess.run(["explorer", "/select,", os.path.normpath(path)])
        elif sys.platform == "darwin":
            if os.path.isdir(path):
                subprocess.run(["open", target])
            else:
                subprocess.run(["open", "-R", path])  # reveal in Finder
        else:  # Linux / BSD
            subprocess.run(["xdg-open", target])
    except Exception as e:
        messagebox.showerror("Couldn't open folder",
            f"{type(e).__name__}: {e}\n\nPath: {target}")


def read_active_campaign():
    """Return the currently-active campaign folder name, or '' if active.txt missing."""
    if not os.path.isfile(ACTIVE_CAMPAIGN_PATH):
        return ""
    try:
        with open(ACTIVE_CAMPAIGN_PATH, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"): continue
                if "=" in line:
                    return line.split("=", 1)[1].strip()
                return line
    except Exception: pass
    return ""


def write_active_campaign(name):
    """Write active.txt with the given campaign folder name (or special value).
       Preserves the readable header-comment format the project uses."""
    with open(ACTIVE_CAMPAIGN_PATH, "w", encoding="utf-8") as f:
        f.write("# Set which campaign to play. Change the name to switch campaigns.\n")
        f.write("# Use \"default\" to disable the mod and play the base game.\n")
        f.write("# NOTE: CASE SENSITIVE!\n")
        f.write("# Examples:\n")
        f.write("#   Active Campaign          = Example Campaign\n")
        f.write("#   Active Campaign          = default\n")
        f.write("#\n")
        f.write(f"Active Campaign          = {name}\n")


def list_campaigns():
    """List all campaign folders plus the <Library> pseudo-campaign."""
    out = []
    if os.path.isdir(LIBRARY_DIR):
        out.append(LIBRARY_SOURCE)
    if not os.path.isdir(CAMPAIGNS_DIR):
        return out
    for name in sorted(os.listdir(CAMPAIGNS_DIR)):
        p = os.path.join(CAMPAIGNS_DIR, name)
        if not os.path.isdir(p) or name.startswith("_") or name.startswith("."):
            continue
        if os.path.isdir(os.path.join(p, "teams")) or os.path.isfile(os.path.join(p, "campaign.txt")):
            out.append(name)
    return out


def list_teams(campaign):
    """List team folder names in a campaign (or the library + base game + custom)."""
    if campaign == LIBRARY_SOURCE:
        names = set()
        if os.path.isdir(TEAM_LIBRARY_DIR):
            for d in os.listdir(TEAM_LIBRARY_DIR):
                if os.path.isdir(os.path.join(TEAM_LIBRARY_DIR, d)):
                    names.add(d)
        for sub in ("Base Game Teams", "Custom Teams (in-game editor)"):
            sd = os.path.join(LIBRARY_DIR, sub)
            if os.path.isdir(sd):
                for d in os.listdir(sd):
                    if os.path.isdir(os.path.join(sd, d)):
                        names.add(d)
        return sorted(names)
    teams_dir = os.path.join(CAMPAIGNS_DIR, campaign, "teams")
    if not os.path.isdir(teams_dir):
        return []
    return sorted([d for d in os.listdir(teams_dir) if os.path.isdir(os.path.join(teams_dir, d))])


def list_players(campaign, team):
    """List player files in a campaign's team (or the library + base game + custom).
       For <Library>, the library is now FLAT — 'team' is ignored."""
    if campaign == LIBRARY_SOURCE:
        names = set()
        if os.path.isdir(PLAYER_LIBRARY_DIR):
            for f in os.listdir(PLAYER_LIBRARY_DIR):
                if f.endswith(".txt"):
                    names.add(f)
        for sub in ("Base Game Players", "Custom Players (in-game editor)"):
            sd = os.path.join(LIBRARY_DIR, sub)
            if os.path.isdir(sd):
                for f in os.listdir(sd):
                    if f.endswith(".txt") and not f.startswith("_"):
                        names.add(f)
        return sorted(names)
    pdir = os.path.join(CAMPAIGNS_DIR, campaign, "teams", team, "players")
    if not os.path.isdir(pdir):
        return []
    return sorted([f for f in os.listdir(pdir) if f.endswith(".txt")])


def get_preferred_position(player_file):
    """Read the Preferred Position from a library player file's header comment."""
    try:
        with open(player_file, "r", encoding="utf-8") as f:
            for line in f:
                s = line.strip()
                if not s.startswith("#"): break
                m = re.search(r"Preferred Position:\s*(.+)", s)
                if m: return m.group(1).strip()
    except Exception: pass
    return None


VALID_POSITIONS = ("Goalie", "Left Wing", "Right Wing", "Center",
                   "Left Defense", "Right Defense",
                   "Line 2 Left Wing", "Line 2 Right Wing", "Line 2 Center",
                   "Line 2 Left Defense", "Line 2 Right Defense")


def parse_position_from_filename(filename):
    """Extract the Position from 'Position - Name.txt' or just 'Position.txt'."""
    base = filename[:-4] if filename.endswith(".txt") else filename
    if " - " in base:
        return base.split(" - ", 1)[0].strip()
    return base.strip()


def _existing_slot_path(team_dir, position):
    """Return the path of the existing player file for a given slot position, or None.
       Matches both 'Position.txt' and 'Position - Anything.txt'."""
    pdir = os.path.join(team_dir, "players")
    if not os.path.isdir(pdir): return None
    for f in os.listdir(pdir):
        if f.endswith(".txt") and parse_position_from_filename(f).lower() == position.lower():
            return os.path.join(pdir, f)
    return None


# ============================================================
#   SHARED WIDGETS
# ============================================================
class ColorPicker(ttk.Frame):
    """RGB color input with swatch + picker button."""
    def __init__(self, parent, label="", hint=None):
        super().__init__(parent)
        ttk.Label(self, text=label, width=26, anchor="w").pack(side="left")
        self.var = tk.StringVar()
        self.entry = ttk.Entry(self, textvariable=self.var, width=16)
        self.entry.pack(side="left", padx=4)
        self.var.trace_add("write", lambda *a: self.update_swatch())
        self.swatch = tk.Label(self, text="      ", bg="#cccccc", relief="sunken", width=6, height=1)
        self.swatch.pack(side="left", padx=4)
        # Clicking swatch also opens picker
        self.swatch.bind("<Button-1>", lambda e: self.pick())
        ttk.Button(self, text="Pick", command=self.pick, width=8).pack(side="left", padx=2)
        ttk.Button(self, text="Random ▾", command=self._show_random_menu, width=12).pack(side="left", padx=2)
        ttk.Button(self, text="Clear", command=lambda: self.var.set(""), width=8).pack(side="left", padx=2)
        if hint:
            ttk.Label(self, text=hint, foreground="#777", font=("", 8)).pack(side="left", padx=4)

    def update_swatch(self):
        v = self.var.get().strip()
        if not v:
            self.swatch.configure(bg="#cccccc")
            return
        m = re.match(r"^\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*$", v)
        if m:
            r, g, b = (min(255, max(0, int(x))) for x in m.groups())
            self.swatch.configure(bg=f"#{r:02x}{g:02x}{b:02x}")
        else:
            self.swatch.configure(bg="#eeeeee")  # invalid or "random"

    def pick(self):
        current = self.var.get().strip()
        # Build the (r, g, b), "#rrggbb" tuple that askcolor expects
        init_color = None
        m = re.match(r"^\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*$", current)
        if m:
            r, g, b = (int(x) for x in m.groups())
            init_color = f"#{r:02x}{g:02x}{b:02x}"
        try:
            # Correct API: pass color as first positional, or color=
            if init_color:
                result = colorchooser.askcolor(color=init_color, parent=self.winfo_toplevel())
            else:
                result = colorchooser.askcolor(parent=self.winfo_toplevel())
        except Exception as e:
            messagebox.showerror("Color Picker", f"Failed to open picker: {e}")
            return
        if result and result[0]:
            r, g, b = (int(round(c)) for c in result[0])
            self.var.set(f"{r}, {g}, {b}")

    def _show_random_menu(self):
        """Drop-down menu with random color options."""
        import random
        menu = tk.Menu(self, tearoff=0)
        menu.add_command(label="'random' — mod picks fresh color every match",
                         command=lambda: self.var.set("random"))
        menu.add_command(label="Roll one now (generate an RGB and save it)",
                         command=self._roll_random_now)
        menu.add_separator()
        menu.add_command(label="Range per channel: random(0,255), random(0,255), random(0,255)",
                         command=lambda: self.var.set("random(0,255), random(0,255), random(0,255)"))
        menu.add_command(label="Darks only: random(0,80) per channel",
                         command=lambda: self.var.set("random(0,80), random(0,80), random(0,80)"))
        menu.add_command(label="Brights only: random(180,255) per channel",
                         command=lambda: self.var.set("random(180,255), random(180,255), random(180,255)"))
        menu.add_command(label="Vivid: random(200,255), random(0,80), random(0,80)",
                         command=lambda: self.var.set("random(200,255), random(0,80), random(0,80)"))
        try:
            menu.tk_popup(self.winfo_pointerx(), self.winfo_pointery())
        finally:
            menu.grab_release()

    def _roll_random_now(self):
        import random
        r, g, b = random.randint(0, 255), random.randint(0, 255), random.randint(0, 255)
        self.var.set(f"{r}, {g}, {b}")

    def get(self): return self.var.get().strip()
    def set(self, v): self.var.set(v or "")


# ============================================================
#   BULK COLOR ACTIONS (shared by PlayerEditor + TeamEditor)
# ============================================================
def _editor_color_widgets(editor):
    return {k: w for k, w in editor.widgets.items() if isinstance(w, ColorPicker)}


def _bulk_clear(editor):
    for w in _editor_color_widgets(editor).values():
        w.set("")
    if hasattr(editor, "_refresh_live"): editor._refresh_live()


def _bulk_pick_one(editor):
    try:
        result = colorchooser.askcolor(parent=editor.winfo_toplevel())
    except Exception: return
    if not result or not result[0]: return
    r, g, b = (int(round(c)) for c in result[0])
    val = f"{r}, {g}, {b}"
    for w in _editor_color_widgets(editor).values():
        w.set(val)
    if hasattr(editor, "_refresh_live"): editor._refresh_live()


def _bulk_random_per_field(editor):
    import random
    for w in _editor_color_widgets(editor).values():
        r, g, b = random.randint(0, 255), random.randint(0, 255), random.randint(0, 255)
        w.set(f"{r}, {g}, {b}")
    if hasattr(editor, "_refresh_live"): editor._refresh_live()


def _bulk_copy_from_random(editor, is_team=False):
    """Pull color fields from a random existing team/player file in the
       library + base-game + custom folders."""
    import random
    candidates = []
    if is_team:
        for base in (TEAM_LIBRARY_DIR,
                     os.path.join(LIBRARY_DIR, "Base Game Teams"),
                     os.path.join(LIBRARY_DIR, "Custom Teams (in-game editor)")):
            if os.path.isdir(base):
                for d in os.listdir(base):
                    tp = os.path.join(base, d, "team.txt")
                    if os.path.isfile(tp): candidates.append(tp)
    else:
        is_goalie = getattr(editor, "is_goalie", False)
        for base in (PLAYER_LIBRARY_DIR,
                     os.path.join(LIBRARY_DIR, "Base Game Players"),
                     os.path.join(LIBRARY_DIR, "Custom Players (in-game editor)")):
            if os.path.isdir(base):
                for f in os.listdir(base):
                    if not f.endswith(".txt") or f.startswith("_"): continue
                    if ("Goalie" in f) == is_goalie:
                        candidates.append(os.path.join(base, f))
    if not candidates:
        messagebox.showinfo("Nothing to copy from",
            "No library files found. Launch the game once to auto-dump teams + players.")
        return
    pick = random.choice(candidates)
    try: data = read_kv(pick)
    except Exception as e:
        messagebox.showerror("Copy failed", f"{type(e).__name__}: {e}")
        return
    applied = 0
    for lbl, w in _editor_color_widgets(editor).items():
        if lbl in data and data[lbl]:
            w.set(data[lbl]); applied += 1
    if hasattr(editor, "_refresh_live"): editor._refresh_live()
    src_label = os.path.basename(os.path.dirname(pick)) if is_team else os.path.basename(pick)[:-4]
    messagebox.showinfo("Colors copied",
        f"Copied {applied} color field(s) from:\n{src_label}")


def _build_color_toolbar(parent, editor, is_team=False):
    bar = ttk.LabelFrame(parent, text=" Bulk color actions ")
    ttk.Button(bar, text="Clear all",
               command=lambda: _bulk_clear(editor), width=11).pack(side="left", padx=3, pady=3)
    ttk.Button(bar, text="Pick one → all",
               command=lambda: _bulk_pick_one(editor), width=14).pack(side="left", padx=3, pady=3)
    ttk.Button(bar, text="Random per field",
               command=lambda: _bulk_random_per_field(editor), width=16).pack(side="left", padx=3, pady=3)
    ttk.Button(bar, text="Copy from random existing",
               command=lambda: _bulk_copy_from_random(editor, is_team=is_team),
               width=24).pack(side="left", padx=3, pady=3)
    return bar


class LabeledEntry(ttk.Frame):
    def __init__(self, parent, label, hint=None, width=20):
        super().__init__(parent)
        ttk.Label(self, text=label, width=26, anchor="w").pack(side="left")
        self.var = tk.StringVar()
        self.entry = ttk.Entry(self, textvariable=self.var, width=width)
        self.entry.pack(side="left", padx=4)
        if hint:
            ttk.Label(self, text=hint, foreground="#777", font=("", 8)).pack(side="left", padx=4)
    def get(self): return self.var.get().strip()
    def set(self, v): self.var.set(v or "")


class LabeledCheckbox(ttk.Frame):
    """Yes/no field rendered as a checkbox. get() returns 'yes'/'no' strings
       so it's drop-in compatible with the existing config writer/reader that
       expects those values. set() accepts 'yes'/'true'/'1' as checked."""
    def __init__(self, parent, label, hint=None):
        super().__init__(parent)
        ttk.Label(self, text=label, width=26, anchor="w").pack(side="left")
        self.var = tk.BooleanVar(value=False)
        self.check = ttk.Checkbutton(self, variable=self.var)
        self.check.pack(side="left", padx=4)
        if hint:
            ttk.Label(self, text=hint, foreground="#777", font=("", 8)).pack(side="left", padx=4)
    def get(self): return "yes" if self.var.get() else "no"
    def set(self, v):
        s = (v or "").strip().lower()
        self.var.set(s in ("yes", "true", "1", "on"))


class StatSlider(ttk.Frame):
    """Stat input: label + slider + number entry. Emits on_change callback.
       Accepts free-text like 'random(50,80)' — slider disables in that case.
       If is_float=True, uses float increments with 2 decimal precision."""
    def __init__(self, parent, label, min_val=0, max_val=999, hint=None,
                 on_change=None, width=7, is_float=False, resolution=None):
        super().__init__(parent)
        self.on_change = on_change
        self._syncing = False
        self.min_val = min_val
        self.max_val = max_val
        self.is_float = is_float

        ttk.Label(self, text=label, width=26, anchor="w").pack(side="left")
        self.slider = ttk.Scale(self, from_=min_val, to=max_val, length=200,
                                command=self._on_slider)
        self.slider.pack(side="left", padx=4)
        self.var = tk.StringVar()
        self.entry = ttk.Entry(self, textvariable=self.var, width=width)
        self.entry.pack(side="left", padx=4)
        self.var.trace_add("write", self._on_entry)
        if hint:
            ttk.Label(self, text=hint, foreground="#777", font=("", 8)).pack(side="left", padx=4)

    def _format(self, v):
        if self.is_float:
            return f"{float(v):.2f}"
        return str(int(v))

    def _on_slider(self, raw):
        if self._syncing: return
        try:
            v = float(raw) if self.is_float else int(float(raw))
        except Exception:
            return
        self._syncing = True
        self.var.set(self._format(v))
        self._syncing = False
        if self.on_change: self.on_change()

    def _on_entry(self, *a):
        if self._syncing: return
        s = self.var.get().strip()
        try:
            v = float(s) if self.is_float else int(s)
            if v < self.min_val: v = self.min_val
            if v > self.max_val: v = self.max_val
            self._syncing = True
            self.slider.set(v)
            self._syncing = False
        except Exception:
            pass  # Non-numeric (random(...)) — leave slider alone
        if self.on_change: self.on_change()

    def get(self): return self.var.get().strip()
    def set(self, v):
        self._syncing = True
        self.var.set(v or "")
        try:
            if v:
                n = float(v) if self.is_float else int(float(v))
                self.slider.set(n)
        except Exception:
            pass
        self._syncing = False

    def numeric_value(self):
        """Return current numeric value, or None if non-numeric."""
        try:
            return float(self.var.get().strip()) if self.is_float else int(self.var.get().strip())
        except Exception:
            return None


class LabeledCombo(ttk.Frame):
    """Combobox that accepts either a known value or free text (not strict).
       Dropdown height scales with list length (up to 30 rows visible)."""
    def __init__(self, parent, label, values, hint=None, width=22):
        super().__init__(parent)
        ttk.Label(self, text=label, width=26, anchor="w").pack(side="left")
        self.var = tk.StringVar()
        # Show up to 30 rows in the dropdown so long lists like faces don't get clipped
        dropdown_height = min(30, max(5, len(values)))
        self.combo = ttk.Combobox(self, textvariable=self.var, values=values,
                                   width=width, height=dropdown_height)
        self.combo.pack(side="left", padx=4)
        if hint:
            ttk.Label(self, text=hint, foreground="#777", font=("", 8)).pack(side="left", padx=4)
    def get(self): return self.var.get().strip()
    def set(self, v): self.var.set(v or "")


class ListPicker(ttk.Frame):
    """A list widget with Add/Remove/Up/Down buttons, for talents/relics.
       Add opens a searchable dialog with friendly names + descriptions.
       `entries` = list of dicts with keys: key, name, desc, has_level2 (optional).
       If entries is None, falls back to plain string list from `options`.
       `is_pool` = if True, shows Set 'all' / Clear buttons (for random talent pools)."""
    def __init__(self, parent, label, entries=None, options=None, hint=None,
                 supports_level=False, is_pool=False):
        super().__init__(parent)
        self.entries = entries or []
        self.options = options or []
        self.supports_level = supports_level
        self.is_pool = is_pool
        # Build lookups by key and by display name (case-insensitive)
        self.entry_by_key = {e["key"]: e for e in self.entries}
        self.entry_by_name = {e["name"].lower(): e for e in self.entries if e.get("name")}

        # Header row
        hdr = ttk.Frame(self)
        hdr.pack(fill="x")
        ttk.Label(hdr, text=label, width=26, anchor="w", font=("", 9, "bold")).pack(side="left")
        if hint:
            ttk.Label(hdr, text=hint, foreground="#777", font=("", 8)).pack(side="left", padx=4)

        body = ttk.Frame(self)
        body.pack(fill="x", pady=2)

        # Pack buttons FIRST on the right so they always claim space —
        # otherwise when the listbox gets `fill=x, expand=True` in a narrow
        # scrollable canvas, the buttons can get pushed off the visible area.
        btns = ttk.Frame(body)
        btns.pack(side="right", padx=(4, 0), anchor="n")
        ttk.Button(btns, text="+ Add", command=self.add_item, width=10).pack(pady=1)
        ttk.Button(btns, text="- Remove", command=self.remove_item, width=10).pack(pady=1)
        ttk.Button(btns, text="↑ Up", command=lambda: self.move(-1), width=10).pack(pady=1)
        ttk.Button(btns, text="↓ Down", command=lambda: self.move(1), width=10).pack(pady=1)
        # Pool-only buttons: "Set all" only makes sense for Random Pool fields
        if self.is_pool:
            ttk.Button(btns, text="Set 'all'", command=self.set_all, width=10).pack(pady=(8, 1))
            ttk.Button(btns, text="Clear", command=self.clear_all, width=10).pack(pady=1)

        # Listbox shows friendly display ("Display Name [key]")
        self.listbox = tk.Listbox(body, height=5, width=50)
        self.listbox.pack(side="left", padx=(26, 4), fill="x", expand=True)

        # Store internal values parallel to listbox
        self._values = []  # list of raw config strings (keys, possibly with :2)

    def set_all(self):
        """Pool-only: marks the pool as 'all' (no restriction).
           Writes a sentinel that the mod resolves to 'no restriction = use everything'."""
        self.listbox.delete(0, "end")
        self._values = ["all"]
        self.listbox.insert("end", "(ALL — no restriction)")
        # Force the listbox to redraw
        self.listbox.update_idletasks()

    def clear_all(self):
        self.listbox.delete(0, "end")
        self._values = []

    def _display_for(self, raw):
        """Build display string. 'raw' may be either a key or a friendly name."""
        token = raw
        suffix = ""
        if ":" in raw:
            token, lvl = raw.rsplit(":", 1)
            suffix = f" (Lv{lvl})"
        # Try match by key OR by name
        entry = self.entry_by_key.get(token) or self.entry_by_name.get(token.lower())
        if entry:
            # Show "Friendly Name — internal_key" for clarity
            if entry.get("name") and entry["name"] != entry["key"]:
                return f"{entry['name']}{suffix}  —  {entry['key']}"
            return f"{entry['key']}{suffix}"
        return raw

    def add_item(self):
        dlg = tk.Toplevel(self)
        dlg.title("Add")
        _fit_geometry(dlg, 700, 550)
        dlg.transient(self.winfo_toplevel())
        dlg.grab_set()

        ttk.Label(dlg, text="Search:").pack(anchor="w", padx=8, pady=4)
        search_var = tk.StringVar()
        search_entry = ttk.Entry(dlg, textvariable=search_var)
        search_entry.pack(fill="x", padx=8)
        search_entry.focus_set()

        # Split: listbox on left, description panel on right
        body = ttk.Frame(dlg)
        body.pack(fill="both", expand=True, padx=8, pady=8)

        lst = tk.Listbox(body, height=20, width=42)
        lst.pack(side="left", fill="both", expand=True)
        sb = ttk.Scrollbar(body, orient="vertical", command=lst.yview)
        sb.pack(side="left", fill="y")
        lst.configure(yscrollcommand=sb.set)

        desc_frame = ttk.Frame(body)
        desc_frame.pack(side="left", fill="both", expand=True, padx=(8, 0))
        ttk.Label(desc_frame, text="Description:", font=("", 9, "bold")).pack(anchor="w")
        desc_text = tk.Text(desc_frame, wrap="word", height=10, width=40, state="disabled")
        desc_text.pack(fill="both", expand=True)
        key_label = ttk.Label(desc_frame, text="", foreground="#555", font=("", 8))
        key_label.pack(anchor="w", pady=4)

        # Decide data source: entries (with descriptions) or plain options
        using_entries = bool(self.entries)
        # Each list row is (display_str, entry_or_key)
        rows = []

        def rebuild_rows(needle=""):
            rows.clear()
            needle = needle.lower()
            if using_entries:
                for e in self.entries:
                    hay = (e["key"] + " " + e["name"] + " " + e.get("desc", "")).lower()
                    if needle in hay:
                        lv2 = " (Lv2 avail)" if e.get("has_level2") else ""
                        display = f"{e['name']}{lv2}  —  {e['key']}"
                        rows.append((display, e))
            else:
                for opt in self.options:
                    if needle in opt.lower():
                        rows.append((opt, {"key": opt, "name": opt, "desc": ""}))

        def refresh(*a):
            rebuild_rows(search_var.get())
            lst.delete(0, "end")
            for display, _ in rows:
                lst.insert("end", display)
        search_var.trace_add("write", refresh)
        refresh()

        def show_desc(*a):
            sel = lst.curselection()
            desc_text.configure(state="normal")
            desc_text.delete("1.0", "end")
            key_label.configure(text="")
            if sel:
                _, e = rows[sel[0]]
                desc_text.insert("1.0", e.get("desc", "") or "(no description)")
                key_label.configure(text=f"config value: {e['key']}")
            desc_text.configure(state="disabled")
        lst.bind("<<ListboxSelect>>", show_desc)

        if self.supports_level:
            lvl_frame = ttk.Frame(dlg)
            lvl_frame.pack(anchor="w", padx=8, pady=2)
            ttk.Label(lvl_frame, text="Level:").pack(side="left")
            level_var = tk.StringVar(value="1")
            ttk.Radiobutton(lvl_frame, text="1", variable=level_var, value="1").pack(side="left")
            ttk.Radiobutton(lvl_frame, text="2 (if available)", variable=level_var, value="2").pack(side="left")
        else:
            level_var = None

        def do_pick():
            sel = lst.curselection()
            if not sel: return
            _, e = rows[sel[0]]
            # Prefer the friendly display name for the config file.
            # The mod's parser resolves both friendly names and internal keys.
            val = e["name"] if e.get("name") else e["key"]
            if level_var and level_var.get() == "2":
                val = val + ":2"
            self._values.append(val)
            self.listbox.insert("end", self._display_for(val))
            dlg.destroy()

        lst.bind("<Double-Button-1>", lambda ev: do_pick())

        btnrow = ttk.Frame(dlg)
        btnrow.pack(fill="x", pady=4, padx=8)
        ttk.Button(btnrow, text="Add", command=do_pick, width=12).pack(side="right", padx=6)
        ttk.Button(btnrow, text="Cancel", command=dlg.destroy, width=12).pack(side="right")

    def remove_item(self):
        sel = self.listbox.curselection()
        if sel:
            idx = sel[0]
            self.listbox.delete(idx)
            del self._values[idx]

    def move(self, direction):
        sel = self.listbox.curselection()
        if not sel: return
        idx = sel[0]
        new_idx = idx + direction
        if new_idx < 0 or new_idx >= self.listbox.size(): return
        val = self._values.pop(idx)
        self._values.insert(new_idx, val)
        self.listbox.delete(idx)
        self.listbox.insert(new_idx, self._display_for(val))
        self.listbox.selection_set(new_idx)

    def get(self):
        # If nothing picked, write nothing (consumers can interpret as "all" or default)
        return ", ".join(self._values)

    def set(self, s):
        self.listbox.delete(0, "end")
        self._values = []
        if not s: return
        # Special case: "all" means "no restriction" — show a single marker item
        if s.strip().lower() in ("all", "whole pool", "full pool"):
            self._values.append("all")
            self.listbox.insert("end", "(ALL talents — no restriction)")
            return
        for item in s.split(","):
            item = item.strip()
            if item:
                self._values.append(item)
                self.listbox.insert("end", self._display_for(item))


# ============================================================
#   VALIDATION BANNER  (collapsible warning list at top of each editor)
# ============================================================
class ValidationBanner(ttk.Frame):
    """Shows a list of validation warnings. Green when clean, yellow/red when issues."""
    def __init__(self, parent):
        super().__init__(parent)
        self._label = tk.Label(self, text="", justify="left", anchor="w",
                               font=("", 9), padx=8, pady=4)
        self._label.pack(fill="x")
        self.set_issues([])

    def set_issues(self, issues):
        if not issues:
            self._label.configure(
                text="  ✓ No problems detected.",
                background="#e8f5e9", foreground="#2e7d32")
        else:
            msg = "  ⚠ {} issue{} to review:\n".format(
                len(issues), "" if len(issues) == 1 else "s")
            msg += "\n".join(f"    • {i}" for i in issues)
            self._label.configure(text=msg, background="#fff3c4", foreground="#553300")


# ============================================================
#   JERSEY PREVIEW  (mock jersey swatches colored from the current inputs)
# ============================================================
def _parse_rgb(s):
    """Parse 'R, G, B' string to hex. Returns None if invalid."""
    if not s: return None
    try:
        parts = [int(p.strip()) for p in s.split(",")]
        if len(parts) != 3: return None
        r, g, b = [max(0, min(255, v)) for v in parts]
        return f"#{r:02x}{g:02x}{b:02x}"
    except Exception:
        return None


class ActSequenceBuilder(ttk.Frame):
    """Friendly per-slot picker for the Act Sequence.

    Replaces a raw 'Act Sequence = 1, 1, 2, 2, 1, 2, 2, 2, 3' text field with
    a row of clickable 'Map N: [Act X]' cells + presets + add/remove buttons.
    Exposes .get() / .set() like LabeledEntry so the editor code treats it
    the same as any other field widget."""

    # Act color legend
    ACT_COLORS = {1: "#2e7d32", 2: "#ef6c00", 3: "#b71c1c"}
    ACT_LABELS = {1: "Act 1 (easy)", 2: "Act 2 (medium)", 3: "Act 3 (boss)"}

    PRESETS = [
        ("Short", [1, 2, 3]),
        ("Standard", [1, 1, 2, 2, 1, 2, 2, 2, 3]),
        ("Long", [1, 1, 1, 2, 2, 1, 2, 2, 2, 3, 3]),
    ]

    def __init__(self, parent, label="Act Sequence", hint=None, on_change=None):
        super().__init__(parent)
        self._on_change = on_change
        self._slots = []  # list of int (1/2/3)
        self._spartan_replace = {}  # map index → bool (only Act 1 maps)
        # var is exposed so trace_add (used by editor validation) keeps working
        self.var = tk.StringVar()

        header = ttk.Frame(self)
        header.pack(fill="x")
        ttk.Label(header, text=label, width=26, anchor="w",
                  font=("", 10, "bold")).pack(side="left")

        preset_frame = ttk.Frame(header)
        preset_frame.pack(side="left")
        ttk.Label(preset_frame, text="Preset:",
                  foreground="#555", font=("", 8)).pack(side="left", padx=(4, 2))
        for name, seq in self.PRESETS:
            ttk.Button(preset_frame, text=name, width=9,
                       command=lambda s=seq: self.set(",".join(str(x) for x in s))
                       ).pack(side="left", padx=1)

        if hint:
            ttk.Label(self, text=hint, foreground="#555",
                      font=("", 8), wraplength=700, justify="left").pack(anchor="w", padx=4)

        # Horizontal scrollable strip of map cells
        strip_outer = ttk.Frame(self)
        strip_outer.pack(fill="x", pady=4)
        self._canvas = tk.Canvas(strip_outer, height=70, highlightthickness=0)
        hscroll = ttk.Scrollbar(strip_outer, orient="horizontal",
                                 command=self._canvas.xview)
        self._canvas.configure(xscrollcommand=hscroll.set)
        self._strip = ttk.Frame(self._canvas)
        self._canvas.create_window((0, 0), window=self._strip, anchor="nw")
        self._strip.bind("<Configure>",
            lambda e: self._canvas.configure(scrollregion=self._canvas.bbox("all")))
        self._canvas.pack(side="top", fill="x", expand=True)
        hscroll.pack(side="bottom", fill="x")

        # Controls row: Add / Clear / Summary
        # Act 3 is hardcoded as the LAST map and can't be added/removed/changed —
        # every campaign must end in the boss. Only Act 1 / Act 2 are user-added.
        ctrls = ttk.Frame(self)
        ctrls.pack(fill="x", pady=(2, 0))
        ttk.Button(ctrls, text="+ Act 1 (easy)",
                   command=lambda: self._add(1)).pack(side="left", padx=2)
        ttk.Button(ctrls, text="+ Act 2 (medium)",
                   command=lambda: self._add(2)).pack(side="left", padx=2)
        ttk.Button(ctrls, text="Clear all (keep boss)",
                   command=lambda: self._set_slots([])).pack(side="left", padx=6)

        self._summary = ttk.Label(ctrls, text="", foreground="#0066aa",
                                    font=("", 9, "bold"))
        self._summary.pack(side="left", padx=10)

        # Initial render — starts with just the boss slot
        self._render()

    def _redraw_strip(self):
        for w in self._strip.winfo_children():
            w.destroy()
        last_idx = len(self._slots) - 1
        for i, act in enumerate(self._slots):
            is_boss_slot = (i == last_idx)  # always Act 3, hardcoded
            cell_bg = self.ACT_COLORS.get(act, "#666")
            cell = tk.Frame(self._strip, bg=cell_bg,
                             bd=0, relief="solid",
                             highlightthickness=2 if is_boss_slot else 1,
                             highlightbackground="#ffd700" if is_boss_slot else "#222")
            cell.pack(side="left", padx=2, pady=2)

            if is_boss_slot:
                tk.Label(cell, text=f"Map {i+1}",
                          bg=cell_bg, fg="#fff",
                          font=("", 8)).pack(padx=4, pady=(2, 0))
                tk.Label(cell, text="BOSS",
                          bg=cell_bg, fg="#fff",
                          font=("", 9, "bold")).pack(padx=6, pady=(0, 2))
                tk.Label(cell, text="Act 3",
                          bg=cell_bg, fg="#ffe", font=("", 8)).pack(pady=(0, 2))
            else:
                tk.Label(cell, text=f"Map {i+1}",
                          bg=cell_bg, fg="#fff",
                          font=("", 8)).pack(padx=4, pady=(2, 0))
                act_var = tk.StringVar(value=str(act))
                opts = ttk.Combobox(cell, textvariable=act_var, values=["1", "2"],
                                     width=2, state="readonly")
                opts.pack(padx=4, pady=1)
                def on_pick(*a, idx=i, v=act_var):
                    try: new_val = int(v.get())
                    except Exception: return
                    if new_val == 3: return
                    if idx < len(self._slots) and self._slots[idx] == new_val: return
                    if idx < len(self._slots):
                        self._slots[idx] = new_val
                    self._render()
                act_var.trace_add("write", on_pick)

                # Act 1 maps: per-map Spartan replacement checkbox
                if act == 1:
                    sp_var = tk.BooleanVar(value=self._spartan_replace.get(i, False))
                    def on_sp_toggle(*a, idx=i, v=sp_var):
                        self._spartan_replace[idx] = v.get()
                        self._sync_var()
                    sp_cb = tk.Checkbutton(cell, text="⚔",
                                           variable=sp_var, command=lambda idx=i, v=sp_var: on_sp_toggle(idx=idx, v=v),
                                           bg=cell_bg, fg="#fff",
                                           activebackground=cell_bg, selectcolor="#555",
                                           font=("", 8))
                    sp_cb.pack(pady=0)
                    tk.Label(cell, text="Spartan\n→ Elite",
                              bg=cell_bg, fg="#ddd",
                              font=("", 7), justify="center").pack(pady=(0, 1))

                tk.Button(cell, text="×", command=lambda idx=i: self._remove(idx),
                          bg=cell_bg, fg="#fff", bd=0, activebackground="#333",
                          activeforeground="#fff", font=("", 9, "bold")).pack(pady=(0, 2))

    def _render(self):
        self._normalize()
        # Prune stale spartan_replace entries for slots that no longer exist or aren't Act 1
        self._spartan_replace = {
            k: v for k, v in self._spartan_replace.items()
            if k < len(self._slots) and self._slots[k] == 1
        }
        self._redraw_strip()
        self._sync_var()
        # Summary with accurate game counts
        a1_maps = [(i, self._spartan_replace.get(i, False))
                    for i, a in enumerate(self._slots) if a == 1]
        a2_count = sum(1 for a in self._slots if a == 2)
        a3_count = sum(1 for a in self._slots if a == 3)
        a1_replaced = sum(1 for _, r in a1_maps if r)
        a1_default = len(a1_maps) - a1_replaced
        # Act 1: 3 games default, +1 if Spartan replaced → 4. Act 2/3: always 3.
        a1_games = a1_default * 3 + a1_replaced * 4
        a2_games = a2_count * 3
        a3_games = a3_count * 3
        total_games = a1_games + a2_games + a3_games
        total = len(self._slots)
        if total == 1:
            self._summary.configure(
                text=f"Total maps: 1  (only the boss = 3 games, 3 teams).")
        else:
            sp_note = ""
            if a1_replaced > 0 and a1_default > 0:
                sp_note = f"  ({a1_replaced} with Spartan→Elite, {a1_default} default)"
            elif a1_replaced > 0:
                sp_note = f"  (all Spartans→Elite)"
            self._summary.configure(
                text=f"Total maps: {total}  |  "
                     f"Act 1 ×{len(a1_maps)}{sp_note} = {a1_games} games, "
                     f"Act 2 ×{a2_count} = {a2_games}, Boss = {a3_games}  |  "
                     f"Total: {total_games} teams needed")

    def _normalize(self):
        """Enforce: Act 3 boss is ALWAYS the last slot, exactly once, and cannot be
        removed. Any earlier 3s get demoted to 2. If no 3 exists, append one."""
        # Demote any middle-of-list 3s to 2
        for i in range(len(self._slots) - 1):
            if self._slots[i] == 3:
                self._slots[i] = 2
        # Ensure there's a trailing boss slot
        if not self._slots or self._slots[-1] != 3:
            self._slots.append(3)

    def _sync_var(self):
        self.var.set(",".join(str(a) for a in self._slots))
        if self._on_change:
            try: self._on_change()
            except Exception: pass

    def _add(self, act):
        act = int(act)
        if act not in (1, 2):
            return  # Act 3 is hardcoded as last; users can't add another
        # Insert before the trailing 3 so the boss stays last
        if self._slots and self._slots[-1] == 3:
            self._slots.insert(len(self._slots) - 1, act)
        else:
            self._slots.append(act)
            self._slots.append(3)  # ensure trailing boss
        self._render()

    def _remove(self, idx):
        # Don't allow removing the boss slot
        if idx == len(self._slots) - 1:
            return
        if 0 <= idx < len(self._slots):
            self._slots.pop(idx)
        self._render()

    def _set_slots(self, slots):
        # Normalize legacy configs into the new model (Act 3 reserved for boss slot):
        # - strip the trailing Act 3 from user input (it's the old boss; we'll re-append one)
        # - demote any remaining interior Act 3s to Act 2 (preserves map count)
        # - filter anything else to Act 1/2
        try:
            parsed = [int(s) for s in slots]
        except Exception:
            parsed = []
        if parsed and parsed[-1] == 3:
            parsed = parsed[:-1]  # drop the user's trailing boss — we'll add our own
        cleaned = []
        for v in parsed:
            if v == 3: v = 2  # demote interior 3s
            if v in (1, 2): cleaned.append(v)
        self._slots = cleaned + [3]
        self._render()

    def get_replace_challenges(self):
        """Serialize per-map Spartan replacement into the config field.
        Returns 'yes' (all Act 1 maps), 'no' (none), or 'maps:1,3' (specific map indices, 1-indexed)."""
        a1_indices = [i for i, a in enumerate(self._slots) if a == 1]
        if not a1_indices:
            return "no"
        replaced = [i for i in a1_indices if self._spartan_replace.get(i, False)]
        if len(replaced) == len(a1_indices):
            return "yes"
        if not replaced:
            return "no"
        return "maps:" + ",".join(str(i + 1) for i in replaced)

    def set_replace_challenges(self, s):
        """Load per-map Spartan replacement state from config.
        Accepts 'yes', 'no', per-act '1,2', or 'maps:1,3,5' (1-indexed)."""
        s = (s or "").strip().lower()
        self._spartan_replace = {}
        if s in ("yes", "true", ""):
            for i, a in enumerate(self._slots):
                if a == 1:
                    self._spartan_replace[i] = True
        elif s in ("no", "false"):
            pass  # all Act 1 maps default unchecked
        elif s.startswith("maps:"):
            try:
                indices = {int(p.strip()) - 1 for p in s[5:].split(",") if p.strip()}
            except ValueError:
                indices = set()
            for idx in indices:
                if 0 <= idx < len(self._slots) and self._slots[idx] == 1:
                    self._spartan_replace[idx] = True
        else:
            # Legacy per-act format: if "1" in the list, enable for all Act 1 maps
            try:
                acts = {int(p.strip()) for p in s.split(",") if p.strip()}
            except ValueError:
                acts = set()
            if 1 in acts:
                for i, a in enumerate(self._slots):
                    if a == 1:
                        self._spartan_replace[i] = True
        self._render()

    # Editor-facing interface (matches LabeledEntry .get() / .set())
    def get(self):
        return self.var.get().strip()

    def set(self, s):
        if not s:
            self._set_slots([])
            return
        try:
            parts = [int(p.strip()) for p in str(s).split(",") if p.strip()]
        except ValueError:
            parts = []
        self._set_slots(parts)


class ReplaceChallengesPicker(ttk.Frame):
    """Single checkbox for Act 1 Spartan replacement.

    Spartans are the ONLY challenge maps in Tape to Tape — they appear on every
    Act 1 map. Soccer / golf / boss are elite games, not challenges, and they
    always happen regardless. So this widget has one toggle:
      checked  → Spartans replaced with a full elite-team match (Act 1 maps = 4 games)
      unchecked → default Spartan challenge mini-game (Act 1 maps = 3 games)

    Serializes to the plugin's 'Replace Challenges' field:
      checked   → 'yes' (the plugin accepts 'yes' as 'all challenge maps',
                  which in practice means just the Act 1 Spartans)
      unchecked → 'no'

    Exposes .get() / .set() / .var like LabeledEntry so editor code treats it
    identically to the old text field."""
    def __init__(self, parent, label="Replace Act 1 Spartans", hint=None, on_change=None):
        super().__init__(parent)
        self._on_change = on_change
        self.var = tk.StringVar()

        ttk.Label(self, text=label, width=26, anchor="w").pack(side="left")

        self._checked = tk.BooleanVar(value=True)
        ttk.Checkbutton(self, text="Replace with elite-team match (+1 game per Act 1 map)",
                        variable=self._checked, command=self._sync).pack(side="left", padx=4)

        if hint:
            ttk.Label(self, text=hint, foreground="#777", font=("", 8)).pack(side="left", padx=4)

        self._sync()

    def is_checked(self):
        return bool(self._checked.get())

    def _sync(self, *a):
        self.var.set("yes" if self._checked.get() else "no")
        if self._on_change:
            try: self._on_change()
            except Exception: pass

    def get(self):
        return self.var.get().strip()

    def set(self, s):
        s = (s or "").strip().lower()
        if s in ("", "yes", "true"):
            self._checked.set(True)
        elif s in ("no", "false"):
            self._checked.set(False)
        else:
            # Legacy per-act list — Spartans are only in Act 1, so
            # "anything mentioning 1" → checked
            try:
                acts = {int(p.strip()) for p in s.split(",") if p.strip()}
            except ValueError:
                acts = set()
            self._checked.set(1 in acts)
        self._sync()


class JerseyPreview(ttk.Frame):
    """Draws two mock jerseys (home + away) using ColorPicker values.
       Call update_colors(get_color) where get_color(label) -> rgb string or ''."""
    JERSEY_W = 90
    JERSEY_H = 130

    def __init__(self, parent, has_away=True):
        super().__init__(parent)
        self.has_away = has_away
        self._sides = []
        container = ttk.Frame(self)
        container.pack(pady=4)

        home_frame = ttk.Frame(container)
        home_frame.pack(pady=4)
        ttk.Label(home_frame, text="Home", font=("", 9, "bold")).pack()
        home_canvas = tk.Canvas(home_frame, width=self.JERSEY_W + 40,
                                 height=self.JERSEY_H + 20, bg="#222",
                                 highlightthickness=0)
        home_canvas.pack()
        self._sides.append(("home", home_canvas))

        if has_away:
            away_frame = ttk.Frame(container)
            away_frame.pack(pady=4)
            ttk.Label(away_frame, text="Away", font=("", 9, "bold")).pack()
            away_canvas = tk.Canvas(away_frame, width=self.JERSEY_W + 40,
                                     height=self.JERSEY_H + 20, bg="#222",
                                     highlightthickness=0)
            away_canvas.pack()
            self._sides.append(("away", away_canvas))

    def update_colors(self, get_color):
        """get_color(label) -> 'R, G, B' string or '' / None."""
        for side, cv in self._sides:
            self._draw_side(cv, side, get_color)

    def _draw_side(self, cv, side, get_color):
        cv.delete("all")
        is_home = (side == "home")
        suffix = "" if is_home else " Away"
        # Fields differ between player/team contexts — try both labels.
        def pick(*labels, default="#808080"):
            for lbl in labels:
                v = get_color(lbl)
                h = _parse_rgb(v)
                if h: return h
            return default

        # Team-style field names (primary source). Player uses 'Jersey Color'
        # for home; fall back accordingly.
        if is_home:
            body = pick("Jersey Primary", "Jersey Color")
            body2 = pick("Jersey Secondary", "Jersey Secondary Color")
            body3 = pick("Jersey Accent", "Jersey Accent Color")
            num = pick("Number Color Home", "Number Color")
        else:
            body = pick("Away Primary", "Jersey Color")
            body2 = pick("Away Secondary", "Jersey Secondary Color")
            body3 = pick("Away Accent", "Jersey Accent Color")
            num = pick("Number Color Away", "Number Color")

        helm = pick("Helmet Color")
        helm2 = pick("Helmet Secondary Color")
        helm3 = pick("Helmet Tertiary Color")
        pants = pick("Pants Color")
        pants2 = pick("Pants Secondary Color")
        socks = pick("Socks Color")
        socks2 = pick("Socks Secondary Color")
        gloves = pick("Gloves Color")
        gloves2 = pick("Gloves Secondary Color")
        skates = pick("Skates Color")
        blade = pick("Blade Color")
        laces = pick("Laces Color")
        bicep = pick("Bicep Color")

        x = 20
        y = 10
        # Helmet (primary + secondary stripe + tertiary dot)
        cv.create_oval(x + 25, y, x + 65, y + 30, fill=helm, outline="#000")
        cv.create_line(x + 30, y + 15, x + 60, y + 15, fill=helm2, width=2)
        cv.create_oval(x + 42, y + 3, x + 48, y + 9, fill=helm3, outline="")
        # Jersey body
        jy = y + 32
        cv.create_rectangle(x, jy, x + self.JERSEY_W, jy + 70,
                            fill=body, outline="#000")
        # Shoulder/bicep accents
        cv.create_rectangle(x, jy, x + 15, jy + 25, fill=bicep, outline="")
        cv.create_rectangle(x + self.JERSEY_W - 15, jy, x + self.JERSEY_W, jy + 25,
                            fill=bicep, outline="")
        # Horizontal stripe
        cv.create_rectangle(x, jy + 55, x + self.JERSEY_W, jy + 62,
                            fill=body2, outline="")
        # Number
        cv.create_text(x + self.JERSEY_W / 2, jy + 32, text="88",
                       fill=num, font=("", 20, "bold"))
        # Gloves (primary + secondary accent)
        cv.create_rectangle(x - 8, jy + 40, x + 2, jy + 60, fill=gloves, outline="#000")
        cv.create_rectangle(x + self.JERSEY_W - 2, jy + 40,
                            x + self.JERSEY_W + 8, jy + 60, fill=gloves, outline="#000")
        cv.create_rectangle(x - 8, jy + 54, x + 2, jy + 58, fill=gloves2, outline="")
        cv.create_rectangle(x + self.JERSEY_W - 2, jy + 54,
                            x + self.JERSEY_W + 8, jy + 58, fill=gloves2, outline="")
        # Pants (primary + secondary stripe)
        py = jy + 72
        cv.create_rectangle(x + 10, py, x + self.JERSEY_W - 10, py + 25,
                            fill=pants, outline="#000")
        cv.create_rectangle(x + 10, py + 18, x + self.JERSEY_W - 10, py + 22,
                            fill=pants2, outline="")
        # Socks (primary + secondary stripe)
        sy = py + 27
        cv.create_rectangle(x + 12, sy, x + 38, sy + 18, fill=socks, outline="#000")
        cv.create_rectangle(x + self.JERSEY_W - 38, sy,
                            x + self.JERSEY_W - 12, sy + 18, fill=socks, outline="#000")
        cv.create_rectangle(x + 12, sy + 12, x + 38, sy + 15, fill=socks2, outline="")
        cv.create_rectangle(x + self.JERSEY_W - 38, sy + 12,
                            x + self.JERSEY_W - 12, sy + 15, fill=socks2, outline="")
        # Skates (boot + blade + laces)
        cv.create_rectangle(x + 8, sy + 18, x + 42, sy + 22, fill=skates, outline="#000")
        cv.create_rectangle(x + self.JERSEY_W - 42, sy + 18,
                            x + self.JERSEY_W - 8, sy + 22, fill=skates, outline="#000")
        cv.create_line(x + 8, sy + 22, x + 42, sy + 22, fill=blade, width=2)
        cv.create_line(x + self.JERSEY_W - 42, sy + 22, x + self.JERSEY_W - 8, sy + 22,
                        fill=blade, width=2)
        cv.create_line(x + 20, sy + 18, x + 20, sy + 21, fill=laces, width=1)
        cv.create_line(x + 30, sy + 18, x + 30, sy + 21, fill=laces, width=1)
        cv.create_line(x + self.JERSEY_W - 30, sy + 18, x + self.JERSEY_W - 30, sy + 21,
                        fill=laces, width=1)
        cv.create_line(x + self.JERSEY_W - 20, sy + 18, x + self.JERSEY_W - 20, sy + 21,
                        fill=laces, width=1)


# ============================================================
#   PLAYER EDITOR
# ============================================================
PLAYER_FIELD_ORDER = [
    "Name", "Number", "Face", "Left Handed", "Skin Color", "Size", "Size Offset",
    "Speed", "Shot Power", "Accuracy", "Checking",
    "Ability", "Talents", "Random Talents", "Random Pool",
    "Import Player",
    "Glasses", "Stick", "Helmet", "Helmet Away", "Body", "Body Away",
    "Bicep", "Bicep Away", "Gloves", "Gloves Away",
    "Pants", "Pants Away", "Skates", "Skates Away",
    "Jersey Color", "Jersey Secondary Color", "Jersey Accent Color",
    "Helmet Color", "Helmet Secondary Color", "Helmet Tertiary Color",
    "Gloves Color", "Gloves Secondary Color", "Gloves Tertiary Color",
    "Pants Color", "Pants Secondary Color", "Pants Tertiary Color",
    "Skates Color", "Blade Color", "Laces Color",
    "Socks Color", "Socks Secondary Color", "Socks Tertiary Color",
    "Bicep Color", "Number Color", "Number Secondary Color",
]

GOALIE_FIELD_ORDER = [
    "Name", "Face",
    "Skill", "Catching", "Glove", "Blocker", "Five Hole",
    "Standing Speed", "Butterfly Speed", "Control", "Recovery",
    "Pass Power", "Shot Power", "Poke Check", "Depth", "Pass Read",
    "Goalie Talents", "Import Player",
    "Helmet Skin", "Skin", "Skin Away", "Glove Skin", "Glove Away",
    "Blocker Skin", "Blocker Away", "Pads Skin", "Pads Away",
    "Stick Skin", "Stick Away", "Logo Skin",
    "Jersey Color", "Helmet Color", "Gloves Color", "Pants Color",
    "Skates Color", "Blade Color", "Laces Color",
    "Socks Color", "Bicep Color", "Number Color",
]


class PlayerEditor(ttk.Frame):
    def __init__(self, parent, is_goalie=False, team_colors=None, is_draft_pool=False):
        super().__init__(parent)
        self.is_goalie = is_goalie
        self.is_draft_pool = is_draft_pool
        self.widgets = {}
        self.loaded_path = None
        self._team_colors = team_colors or {}  # dict of color field → "R, G, B" from team.txt

        # Validation banner at the very top
        self.validation = ValidationBanner(self)
        self.validation.pack(fill="x", padx=4, pady=(4, 2))

        # Split: scrollable fields (left) + jersey preview (right)
        body = ttk.Frame(self)
        body.pack(fill="both", expand=True)

        # Jersey preview on the right — pack FIRST so when the window is
        # narrow the preview keeps its natural width and the left scroll
        # area absorbs the squeeze instead of pushing preview off-screen.
        right = ttk.Frame(body)
        right.pack(side="right", fill="y", padx=(4, 6), pady=4)

        left = ttk.Frame(body)
        left.pack(side="left", fill="both", expand=True)

        # Scrollable frame — both vertical AND horizontal so fields never
        # get cut off when the user drags the window narrower than the
        # widest row of labeled inputs.
        canvas = tk.Canvas(left, highlightthickness=0)
        vscroll = ttk.Scrollbar(left, orient="vertical", command=canvas.yview)
        hscroll = ttk.Scrollbar(left, orient="horizontal", command=canvas.xview)
        scroll_frame = ttk.Frame(canvas)
        scroll_frame.bind("<Configure>",
            lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.create_window((0, 0), window=scroll_frame, anchor="nw")
        canvas.configure(yscrollcommand=vscroll.set, xscrollcommand=hscroll.set)
        # Grid layout so both scrollbars position correctly without conflict.
        canvas.grid(row=0, column=0, sticky="nsew")
        vscroll.grid(row=0, column=1, sticky="ns")
        hscroll.grid(row=1, column=0, sticky="ew")
        left.rowconfigure(0, weight=1)
        left.columnconfigure(0, weight=1)
        # Mousewheel scroll — only when pointer is over THIS canvas
        def _on_wheel(e, c=canvas): c.yview_scroll(int(-1*(e.delta/120)), "units")
        canvas.bind("<Enter>", lambda e, c=canvas: c.bind_all("<MouseWheel>", _on_wheel))
        canvas.bind("<Leave>", lambda e, c=canvas: c.unbind_all("<MouseWheel>"))
        ttk.Label(right, text="Preview", font=("", 9, "bold")).pack(anchor="w")
        self.preview = JerseyPreview(right, has_away=True)
        self.preview.pack()
        ttk.Label(right, text="Updates as you pick colors",
                  foreground="#777", font=("", 8)).pack(pady=(2, 0))

        self.build_fields(scroll_frame)
        # Default override dropdowns to "(use team default)" so new players start clean
        if not self.is_goalie:
            for f in self._OVERRIDE_SKIN_FIELDS:
                w = self.widgets.get(f)
                if w and not w.get():
                    w.set("(use team default)")

        # Free-agent editing: lock the Name field because the DLL intentionally
        # skips name assignment for draft-pool entries (renaming breaks the
        # rest of the customization). Keep the field VISIBLE so the user can
        # see which player they're editing, but disable typing into it.
        if self.is_draft_pool:
            name_w = self.widgets.get("Name")
            if name_w is not None and hasattr(name_w, "entry"):
                try:
                    name_w.entry.configure(state="readonly")
                except Exception: pass
                # Also drop a hint near the field explaining why it's locked.
                try:
                    ttk.Label(name_w, text="(locked — free-agent names can't be changed)",
                              foreground="#aa5500", font=("", 8)).pack(side="left", padx=6)
                except Exception: pass

        # Bulk color action toolbar under the validation banner
        toolbar = _build_color_toolbar(self, self, is_team=False)
        toolbar.pack(fill="x", padx=4, pady=(0, 4), before=body)

        # Wire up live validation + preview updates
        self._install_live_updates()
        self._refresh_live()

    def build_fields(self, parent):
        def section(title):
            ttk.Label(parent, text=title, font=("", 10, "bold")).pack(anchor="w", pady=(10, 2))

        # === IMPORT FIRST (at the top so it's the obvious shortcut) ===
        ttk.Label(parent, text="Quick option: Import an existing game player",
                  font=("", 10, "bold")).pack(anchor="w", pady=(4, 2))
        has_player_list = bool(get_game_player_names())
        if has_player_list:
            ttk.Label(parent,
                text=(
                    "Pick a game player from the dropdown to pre-fill everything — stats, face, "
                    "ability, talents, skins, colors.\n"
                    "If all you want is a clone, this is the ONLY field you need. "
                    "Leave everything else blank and click Save.\n"
                    "Use 'random' to pick a random game player.\n"
                    "Any field you fill in below will override the imported value.\n"
                    "NOTE: Import is resolved when you LAUNCH THE GAME — save here, then play."
                ),
                foreground="#555", font=("", 8), justify="left", wraplength=700
            ).pack(anchor="w", padx=4, pady=(0, 4))
        else:
            tk.Label(parent,
                text=(
                    "  Player list not yet available.\n"
                    "  Launch Tape to Tape once with the mod installed — it auto-scans all players.\n"
                    "  Then reopen this editor and the dropdown will be populated."
                ),
                background="#fff3c4", foreground="#553300", font=("", 9, "bold"),
                justify="left", anchor="w", padx=8, pady=4
            ).pack(fill="x", padx=4, pady=(0, 4))
        # Import Player: combo populated from game dump + library players
        player_names = get_all_player_names()
        if player_names:
            self.add_combo(parent, "Import Player", ["random"] + player_names,
                           "Pick a game or custom player, or 'random'")
        else:
            self.add_entry(parent, "Import Player",
                           "Player name or 'random' — run game once + create players to populate this list")

        section("Identity")
        # (Position is determined when assigning to a team — no preferred position field needed)

        if self.is_goalie:
            self.add_entry(parent, "Name", "Full name (First Last) — also used as filename")
            # Goalies don't use Face the same way skaters do — leaving it blank
            # lets the game use its proper goalie head. Setting a skater face
            # (including "Helmet_Face") makes the goalie appear headless.
            self.add_combo(parent, "Left Handed", YESNO_RANDOM, "Determines stick hand")
            self.add_combo(parent, "Skin Color", SKIN_COLORS, "light / dark / random")
            self.add_combo(parent, "Size", SIZES, "Affects hitbox + jersey fit")
            self.add_slider(parent, "Size Offset", min_val=0.5, max_val=2.0,
                hint="Fine-tune scale (default 1.0)", is_float=True, count_in_overall=False)
        else:
            self.add_entry(parent, "Name", "Full name (First Last) — also used as filename")
            self.add_entry(parent, "Number", "Jersey number 1-99")
            self.add_combo(parent, "Face", FACES,
                           "Head/face — pick from the dropdown (86 options, grouped by team)")
            self.add_combo(parent, "Left Handed", YESNO_RANDOM, "yes / no / random")
            self.add_combo(parent, "Skin Color", SKIN_COLORS, "light / dark / random")
            self.add_combo(parent, "Size", SIZES,
                           "ExtraSmall → ExtraExtraBig. Bigger = slower + harder hits")
            self.add_slider(parent, "Size Offset", min_val=0.5, max_val=2.0,
                hint="Fine-tune scale (default 1.0)", is_float=True, count_in_overall=False)

        section("Stats (0-999, base game ~40-80)")
        # Overall display — auto-updates when sliders move
        self.overall_label = ttk.Label(parent, text="Overall: —",
            font=("", 11, "bold"), foreground="#0066aa")
        self.overall_label.pack(anchor="w", padx=2, pady=2)
        if self.is_goalie:
            goalie_stats = ["Skill", "Catching", "Glove", "Blocker", "Five Hole",
                            "Standing Speed", "Butterfly Speed", "Control", "Recovery",
                            "Pass Power", "Shot Power", "Poke Check", "Depth"]
            self._stat_keys = goalie_stats
            for s in goalie_stats:
                self.add_slider(parent, s)
            # Pass Read is 0.0-1.0, not in overall
            self.add_entry(parent, "Pass Read", "0.0-1.0 (pass anticipation)")
        else:
            skater_stats = ["Speed", "Shot Power", "Accuracy", "Checking"]
            self._stat_keys = skater_stats
            for s in skater_stats:
                self.add_slider(parent, s)
        self._update_overall()

        section("Ability + Talents")
        if self.is_goalie:
            # Goalies don't have an Ability — only goalie-specific talents.
            self.add_listpicker(parent, "Goalie Talents", GOALIE_TALENTS,
                f"Pick from {len(GOALIE_TALENTS)} goalie-only talents (with descriptions)")
        else:
            self.add_combo(parent, "Ability", ABILITIES,
                "50 options — LVL2 = upgraded version")
            self.add_listpicker(parent, "Talents", TALENTS,
                f"Click + Add to pick from {len(TALENTS)} talents (with descriptions)")
            self.add_combo(parent, "Random Talents",
                [str(n) for n in range(0, 11)],
                "How many random talents to give each game (0 = none)")
            self.add_listpicker(parent, "Random Pool", TALENTS,
                "Click 'Set all' for any talent, or pick specific talents to restrict the pool",
                is_pool=True)

        section("Uniform Overrides (blank = use team default)")
        ttk.Label(parent,
            text="⚠ The Color Overrides below ONLY apply when the matching skin here is\n"
                 "   set to 'team colors' / 'standard'. Picking a fixed model (e.g. 'hockey fc',\n"
                 "   'tycoons', 'crusaders') uses that model's BAKED-IN colors — your color\n"
                 "   overrides for that piece will be ignored.",
            foreground="#a05000", font=("", 8), justify="left"
        ).pack(anchor="w", padx=4, pady=(0, 4))
        if self.is_goalie:
            self.add_combo(parent, "Helmet Skin", GOALIE_HELMET_SKINS,
                "The MASK. 'standard' = team colors; named options = fixed mask")
            self.add_combo(parent, "Skin", GOALIE_BODY_SKINS,
                "Goalie body (jersey/pants). 'standard' = team colors")
            self.add_combo(parent, "Skin Away", GOALIE_BODY_SKINS, "Body for away jersey")
            self.add_combo(parent, "Glove Skin", GOALIE_GLOVE_SKINS, "'standard' = team colors")
            self.add_combo(parent, "Glove Away", GOALIE_GLOVE_SKINS)
            self.add_combo(parent, "Blocker Skin", GOALIE_BLOCKER_SKINS, "'standard' = team colors")
            self.add_combo(parent, "Blocker Away", GOALIE_BLOCKER_SKINS)
            self.add_combo(parent, "Pads Skin", GOALIE_PADS_SKINS, "'standard' = team colors")
            self.add_combo(parent, "Pads Away", GOALIE_PADS_SKINS)
            self.add_combo(parent, "Stick Skin", GOALIE_STICK_SKINS, "'standard' = team colors")
            self.add_combo(parent, "Stick Away", GOALIE_STICK_SKINS)
            self.add_combo(parent, "Logo Skin", LOGO_SKINS,
                "Custom_L = left-handed, Custom_R = right-handed")
        else:
            self.add_combo(parent, "Glasses", GLASSES_SKINS,
                "Optional eyewear — blank = none")
            self.add_combo(parent, "Stick", OV_STICK_SKINS,
                "'(use team default)' keeps the team's stick. 'team stick' = colorable.")
            self.add_combo(parent, "Helmet", OV_HELMET_SKINS,
                "'team colors' = colorable. 'cage' = face cage. 'none' = bare head (colors ignored)")
            self.add_combo(parent, "Helmet Away", OV_HELMET_SKINS)
            self.add_combo(parent, "Body", OV_BODY_SKINS,
                "'(use team default)' keeps team's. 'team colors' = uses jersey colors below.")
            self.add_combo(parent, "Body Away", OV_BODY_SKINS)
            self.add_combo(parent, "Bicep", OV_BICEP_SKINS,
                "'(use team default)' keeps team's. 'team colors' = colorable.")
            self.add_combo(parent, "Bicep Away", OV_BICEP_SKINS)
            # Gloves and Pants only have 'standard' — hardcoded at save time.
            ttk.Label(parent,
                text="Gloves & Pants: always 'standard' — set Gloves Color / Pants Color below",
                foreground="#777", font=("", 8)).pack(anchor="w", padx=4, pady=2)
            self.add_combo(parent, "Skates", OV_SKATE_SKINS,
                "'(use team default)' keeps team's. 'team colors' = colorable (3 channels).")
            self.add_combo(parent, "Skates Away", OV_SKATE_SKINS)

        section("Color Overrides (blank = use team colors)")
        ttk.Label(parent,
            text="These override the team's default colors for this player only. "
                 "Type R, G, B (0-255) or click Pick / Random / Clear.",
            foreground="#555", font=("", 8), wraplength=600, justify="left"
        ).pack(anchor="w", padx=4, pady=2)
        color_hints = {
            "Jersey Color": "Main jersey body color (when Body = standard)",
            "Jersey Secondary Color": "Jersey trim",
            "Jersey Accent Color": "Jersey detail/stripes",
            "Helmet Color": "Helmet main color (when Helmet = team colors)",
            "Helmet Secondary Color": "Helmet trim",
            "Helmet Tertiary Color": "Helmet accent",
            "Gloves Color": "Gloves main color",
            "Gloves Secondary Color": "Gloves trim",
            "Gloves Tertiary Color": "Gloves accent",
            "Pants Color": "Pants main color",
            "Pants Secondary Color": "Pants stripe",
            "Pants Tertiary Color": "Pants accent",
            "Skates Color": "Skate boot body color",
            "Blade Color": "Skate blades",
            "Laces Color": "Skate laces",
            "Socks Color": "Socks main color",
            "Socks Secondary Color": "Socks stripe",
            "Socks Tertiary Color": "Socks accent",
            "Bicep Color": "Bicep/sleeve color",
            "Number Color": "Jersey number color (main)",
            "Number Secondary Color": "Number outline/shadow",
        }
        colors = (["Jersey Color", "Helmet Color", "Gloves Color", "Pants Color",
                   "Skates Color", "Blade Color", "Laces Color",
                   "Socks Color", "Bicep Color", "Number Color"]
                  if self.is_goalie else
                  ["Jersey Color", "Jersey Secondary Color", "Jersey Accent Color",
                   "Helmet Color", "Helmet Secondary Color", "Helmet Tertiary Color",
                   "Gloves Color", "Gloves Secondary Color", "Gloves Tertiary Color",
                   "Pants Color", "Pants Secondary Color", "Pants Tertiary Color",
                   "Skates Color", "Blade Color", "Laces Color",
                   "Socks Color", "Socks Secondary Color", "Socks Tertiary Color",
                   "Bicep Color", "Number Color", "Number Secondary Color"])
        for c in colors:
            self.add_color(parent, c, hint=color_hints.get(c))

    def add_entry(self, parent, label, hint=None):
        w = LabeledEntry(parent, label, hint)
        w.pack(anchor="w", pady=1)
        self.widgets[label] = w

    def add_combo(self, parent, label, values, hint=None):
        w = LabeledCombo(parent, label, values, hint)
        w.pack(anchor="w", pady=1)
        self.widgets[label] = w

    def add_color(self, parent, label, hint=None):
        w = ColorPicker(parent, label, hint=hint)
        w.pack(anchor="w", pady=1)
        self.widgets[label] = w

    def add_listpicker(self, parent, label, entries, hint=None,
                        supports_level=False, is_pool=False):
        w = ListPicker(parent, label, entries=entries, hint=hint,
                        supports_level=supports_level, is_pool=is_pool)
        w.pack(anchor="w", pady=3, fill="x")
        self.widgets[label] = w

    def add_slider(self, parent, label, min_val=0, max_val=999, hint=None,
                    is_float=False, count_in_overall=True):
        cb = self._update_overall if count_in_overall else None
        w = StatSlider(parent, label, min_val=min_val, max_val=max_val,
                        hint=hint, on_change=cb, is_float=is_float)
        w.pack(anchor="w", pady=1)
        self.widgets[label] = w

    def _update_overall(self):
        if not hasattr(self, "overall_label"): return
        vals = []
        for k in getattr(self, "_stat_keys", []):
            w = self.widgets.get(k)
            if w and hasattr(w, "numeric_value"):
                v = w.numeric_value()
                if v is not None: vals.append(v)
        if vals:
            avg = sum(vals) / len(vals)
            self.overall_label.configure(
                text=f"Overall: {avg:.0f}  ({len(vals)}/{len(self._stat_keys)} stats set)")
        else:
            self.overall_label.configure(text="Overall: —  (set stats to see average)")

    def get_data(self):
        return {k: w.get() for k, w in self.widgets.items() if w.get()}

    def set_data(self, data):
        for k, w in self.widgets.items():
            if k in data:
                w.set(data[k])
        self._refresh_live()

    def _install_live_updates(self):
        """Hook StringVar write-traces on all widgets so we re-validate + re-preview
           live as the user types / picks things."""
        def hook(var):
            try:
                var.trace_add("write", lambda *a: self._refresh_live())
            except Exception: pass
        for w in self.widgets.values():
            if hasattr(w, "var") and isinstance(getattr(w, "var", None), tk.StringVar):
                hook(w.var)

    def _refresh_live(self):
        """Called whenever any field changes. Updates banner + jersey preview."""
        try:
            self.validation.set_issues(self._validate())
        except Exception: pass
        try:
            # Preview uses player's color overrides first, falls back to team colors
            def get_color(lbl):
                if lbl in self.widgets:
                    v = self.widgets[lbl].get()
                    if v: return v
                # Map player color fields to team color fields
                team_map = {
                    "Jersey Color": "Jersey Primary",
                    "Jersey Secondary Color": "Jersey Secondary",
                    "Jersey Accent Color": "Jersey Accent",
                    "Helmet Color": "Helmet Color",
                    "Helmet Secondary Color": "Helmet Secondary Color",
                    "Helmet Tertiary Color": "Helmet Tertiary Color",
                    "Gloves Color": "Gloves Color",
                    "Gloves Secondary Color": "Gloves Secondary Color",
                    "Gloves Tertiary Color": "Gloves Tertiary Color",
                    "Pants Color": "Pants Color",
                    "Pants Secondary Color": "Pants Secondary Color",
                    "Pants Tertiary Color": "Pants Tertiary Color",
                    "Skates Color": "Skates Color",
                    "Blade Color": "Blade Color",
                    "Laces Color": "Laces Color",
                    "Socks Color": "Socks Color",
                    "Socks Secondary Color": "Socks Secondary Color",
                    "Socks Tertiary Color": "Socks Tertiary Color",
                    "Bicep Color": "Bicep Color",
                    "Number Color": "Number Color Home",
                    "Number Secondary Color": "Number Color Away",
                }
                team_key = team_map.get(lbl, lbl)
                return self._team_colors.get(team_key, "")
            self.preview.update_colors(get_color)
        except Exception: pass

    def _validate(self):
        """Return a list of warning strings for current state."""
        issues = []
        data = self.get_data()
        # Name (or Import Player) required
        if not data.get("Name") and (not data.get("Import Player")
                                      or data.get("Import Player", "").lower() == "random"):
            issues.append("Name is empty (or set Import Player to clone a game player).")
        # Number range
        num = data.get("Number", "").strip()
        if num:
            try:
                n = int(num)
                if n < 1 or n > 99:
                    issues.append(f"Number {n} out of range (1–99).")
            except ValueError:
                issues.append(f"Number '{num}' is not a valid integer.")
        # Stat bounds
        stat_keys = getattr(self, "_stat_keys", [])
        for k in stat_keys:
            v = data.get(k, "").strip()
            if not v: continue
            try:
                val = float(v)
                if val < 0 or val > 999:
                    issues.append(f"{k} {val:g} out of range (0–999).")
            except ValueError:
                # Allow 'random(…)' — ignore
                if not v.lower().startswith("random"):
                    issues.append(f"{k} '{v}' is not numeric.")
        # RGB values — exclude fields that take string values like 'light/dark/random'
        for k, v in data.items():
            if k in self._NON_RGB_COLOR_FIELDS: continue
            if k.endswith("Color") or k.endswith(" Color"):
                if not v or v.lower().startswith("random"): continue
                if _parse_rgb(v) is None:
                    issues.append(f"{k} '{v}' is not a valid R, G, B triplet.")
        return issues

    # Color-named fields that DON'T take RGB (they take preset names like light/dark/random)
    _NON_RGB_COLOR_FIELDS = ("Skin Color",)

    # Player-override fields that use the "(use team default)" + "team colors" aliases
    _OVERRIDE_SKIN_FIELDS = ("Stick", "Helmet", "Helmet Away", "Body", "Body Away",
                              "Bicep", "Bicep Away", "Skates", "Skates Away")

    def _translate_override(self, val):
        """Convert UI alias back to config value."""
        if val == "(use team default)": return ""
        if val == "team colors":         return "standard"
        return val

    def _ui_alias(self, val):
        """Convert config value to UI alias."""
        if not val:             return "(use team default)"
        if val == "standard":   return "team colors"
        return val

    def save_file(self, team_path=None):
        """Save the player.

        ALWAYS writes to the library at library/players/<Name>.txt.
        If team_path is given, ALSO writes a copy there (for the team's slot).
        Returns (library_path, team_path_or_None). Raises ValueError if no Name.
        """
        data = self.get_data()
        # Translate override aliases back to real config values
        if not self.is_goalie:
            for f in self._OVERRIDE_SKIN_FIELDS:
                if f in data:
                    real = self._translate_override(data[f])
                    if real == "":
                        data.pop(f, None)
                    else:
                        data[f] = real
            # Gloves/Pants always 'standard' — only option in game
            data.setdefault("Gloves", "standard")
            data.setdefault("Pants", "standard")
        order = GOALIE_FIELD_ORDER if self.is_goalie else PLAYER_FIELD_ORDER

        # Figure out library name from Name (fallback to Import Player)
        lib_name = (data.get("Name") or "").strip()
        if not lib_name:
            imp = (data.get("Import Player") or "").strip()
            if imp and imp.lower() != "random":
                lib_name = imp
        if not lib_name:
            raise ValueError(
                "Can't save — need a Name (or an Import Player name to clone from).")
        safe = re.sub(r'[<>:"/\\|?*]', '_', lib_name).strip()
        os.makedirs(PLAYER_LIBRARY_DIR, exist_ok=True)
        lib_path = os.path.join(PLAYER_LIBRARY_DIR, safe + ".txt")

        # 1) Always write to library
        write_kv(lib_path, data, order=order)
        self.loaded_path = lib_path

        # 2) If a team slot path was given, also write a copy there.
        #    Safety net: never write into a base-game/auto-generated folder.
        if team_path:
            if is_base_game_path(team_path):
                team_path = None  # skip — user's copy will get it via library lookup
            else:
                try:
                    write_kv(team_path, data, order=order)
                except Exception as e:
                    print(f"[warn] team copy failed: {e}")
                    team_path = None
        return lib_path, team_path

    def load_file(self, path):
        self.loaded_path = path
        data = read_kv(path)
        # Translate saved values into UI aliases for skater override fields
        if not self.is_goalie:
            for f in self._OVERRIDE_SKIN_FIELDS:
                if f in data:
                    data[f] = self._ui_alias(data[f])
        else:
            # Old configs wrote "standard" for goalie team-colored options;
            # the GUI dropdown now labels that choice "team colors". Remap
            # so the combobox shows the correct selected item.
            for gf in ("Helmet Skin", "Skin", "Skin Away",
                       "Glove Skin", "Glove Away",
                       "Blocker Skin", "Blocker Away",
                       "Pads Skin", "Pads Away",
                       "Stick Skin", "Stick Away"):
                if data.get(gf) == _GOALIE_LEGACY_STANDARD:
                    data[gf] = "team colors"
        self.set_data(data)


# ============================================================
#   TEAM EDITOR
# ============================================================
TEAM_FIELD_ORDER = [
    "Team Name", "City", "Abbreviation", "Description", "Squad Head", "Logo From", "Import Team", "Stat Scale",
    "Jersey Primary", "Jersey Secondary", "Jersey Accent",
    "Away Primary", "Away Secondary", "Away Accent",
    "Number Color Home", "Number Color Away",
    "Helmet Color", "Helmet Secondary Color", "Helmet Tertiary Color",
    "Gloves Color", "Gloves Secondary Color", "Gloves Tertiary Color",
    "Pants Color", "Pants Secondary Color", "Pants Tertiary Color",
    "Skates Color", "Blade Color", "Laces Color",
    "Socks Color", "Socks Secondary Color", "Socks Tertiary Color",
    "Bicep Color", "Stick Color",
    "Transition Primary", "Transition Secondary", "Transition Tertiary",
    "Body", "Body Away", "Bicep", "Bicep Away",
    "Gloves", "Gloves Away", "Pants", "Pants Away",
    "Skates", "Skates Away", "Helmet", "Helmet Away", "Stick",
    "Team Relics", "Team Random Talents", "Team Random Pool",
    "Bench Size", "Bench Head",
]


class TeamEditor(ttk.Frame):
    def __init__(self, parent, is_player_team=False):
        super().__init__(parent)
        self.widgets = {}
        self.loaded_dir = None
        # Squad Head / Description are only read by the DLL for player-selectable
        # custom squads (shown in the Choose Your Squad menu). For regular
        # campaign teams they have no effect, so we hide them to avoid confusion.
        self.is_player_team = is_player_team

        # Validation banner at the very top
        self.validation = ValidationBanner(self)
        self.validation.pack(fill="x", padx=4, pady=(4, 2))

        # Split: scrollable fields (left) + jersey preview (right, home + away)
        body = ttk.Frame(self)
        body.pack(fill="both", expand=True)

        # Pack the preview column FIRST on the right so it keeps its natural
        # width when the window is narrow and the left scroll area absorbs
        # the squeeze instead of the preview getting clipped off.
        right = ttk.Frame(body)
        right.pack(side="right", fill="y", padx=(4, 6), pady=4)
        ttk.Label(right, text="Uniform Preview", font=("", 9, "bold")).pack(anchor="w")
        self.preview = JerseyPreview(right, has_away=True)
        self.preview.pack()
        ttk.Label(right, text="Updates as you pick colors",
                  foreground="#777", font=("", 8)).pack(pady=(2, 0))

        left = ttk.Frame(body)
        left.pack(side="left", fill="both", expand=True)

        # Vertical + horizontal scroll so no field is cut off at narrow widths.
        canvas = tk.Canvas(left, highlightthickness=0)
        vscroll = ttk.Scrollbar(left, orient="vertical", command=canvas.yview)
        hscroll = ttk.Scrollbar(left, orient="horizontal", command=canvas.xview)
        scroll_frame = ttk.Frame(canvas)
        scroll_frame.bind("<Configure>",
            lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.create_window((0, 0), window=scroll_frame, anchor="nw")
        canvas.configure(yscrollcommand=vscroll.set, xscrollcommand=hscroll.set)
        canvas.grid(row=0, column=0, sticky="nsew")
        vscroll.grid(row=0, column=1, sticky="ns")
        hscroll.grid(row=1, column=0, sticky="ew")
        left.rowconfigure(0, weight=1)
        left.columnconfigure(0, weight=1)
        def _on_wheel(e, c=canvas): c.yview_scroll(int(-1*(e.delta/120)), "units")
        canvas.bind("<Enter>", lambda e, c=canvas: c.bind_all("<MouseWheel>", _on_wheel))
        canvas.bind("<Leave>", lambda e, c=canvas: c.unbind_all("<MouseWheel>"))

        self.build_fields(scroll_frame)
        # Bulk color toolbar under the validation banner
        toolbar = _build_color_toolbar(self, self, is_team=True)
        toolbar.pack(fill="x", padx=4, pady=(0, 4), before=body)

        self._install_live_updates()
        self._refresh_live()

    def _install_live_updates(self):
        def hook(var):
            try:
                var.trace_add("write", lambda *a: self._refresh_live())
            except Exception: pass
        for w in self.widgets.values():
            if hasattr(w, "var") and isinstance(getattr(w, "var", None), tk.StringVar):
                hook(w.var)

    def _refresh_live(self):
        try:
            self.validation.set_issues(self._validate())
        except Exception: pass
        try:
            self.preview.update_colors(lambda lbl: (self.widgets.get(lbl).get()
                                                     if lbl in self.widgets else ""))
        except Exception: pass

    def _validate(self):
        issues = []
        data = {k: w.get() for k, w in self.widgets.items() if w.get()}
        if not data.get("Team Name"):
            issues.append("Team Name is empty.")
        ab = data.get("Abbreviation", "").strip()
        if ab and len(ab) > 4:
            issues.append(f"Abbreviation '{ab}' is longer than 4 chars — in-game fits ~3.")
        for k, v in data.items():
            if k.endswith("Color") or k.endswith("Primary") or k.endswith("Secondary") or k.endswith("Accent"):
                if not v or v.lower().startswith("random"): continue
                if _parse_rgb(v) is None:
                    issues.append(f"{k} '{v}' is not a valid R, G, B triplet.")
        return issues

    def build_fields(self, parent):
        def section(title):
            ttk.Label(parent, text=title, font=("", 10, "bold")).pack(anchor="w", pady=(10, 2))

        # === IMPORT FIRST (at the top — obvious shortcut) ===
        ttk.Label(parent, text="Quick option: Import an existing in-game team",
                  font=("", 10, "bold")).pack(anchor="w", pady=(4, 2))
        has_team_list = bool(get_game_team_names())
        if has_team_list:
            ttk.Label(parent,
                text=(
                    "Pick a team from the dropdown to pre-fill EVERYTHING — players, colors, logo,\n"
                    "uniform skins, the whole team. This is the ONLY field you need for a clone.\n"
                    "Leave everything else blank and click Save.\n"
                    "Use 'RANDOM' for a random team, 'PLAYER' for the player's own team.\n"
                    "Any field you fill in below will override the imported value.\n"
                    "NOTE: Import is resolved when you LAUNCH THE GAME — save here, then play."
                ),
                foreground="#555", font=("", 8), justify="left", wraplength=700
            ).pack(anchor="w", padx=4, pady=(0, 4))
        else:
            tk.Label(parent,
                text=(
                    "  Team list not yet available.\n"
                    "  Launch Tape to Tape once with the mod installed — it auto-scans all teams.\n"
                    "  Then reopen this editor and the dropdown will be populated.\n"
                    "  (You can still type a name manually if you know it.)"
                ),
                background="#fff3c4", foreground="#553300", font=("", 9, "bold"),
                justify="left", anchor="w", padx=8, pady=4
            ).pack(fill="x", padx=4, pady=(0, 4))
        # Import Team: combo populated from game dump + library teams
        team_names = get_all_team_names()
        if team_names:
            self.add_combo(parent, "Import Team", ["RANDOM", "PLAYER"] + team_names,
                           "Pick a game or custom team, or RANDOM / PLAYER")
        else:
            self.add_entry(parent, "Import Team",
                           "Team name or RANDOM / PLAYER — run game once + create teams to populate this list")

        section("Identity")
        self.add_entry(parent, "Team Name")
        self.add_entry(parent, "City")
        self.add_entry(parent, "Abbreviation", "3 letters, e.g. VAN")
        if self.is_player_team:
            # These two are only consumed by the DLL for player-selectable
            # custom squads (shown in the Choose Your Squad menu). Regular
            # campaign teams ignore them, so we only surface them here.
            self.add_entry(parent, "Description",
                           "Shown under the Zamboni in the Choose Your Squad menu")
            self.add_combo(parent, "Squad Head", FACES,
                           "Face for the key player shown on this squad's tile icon")
        # Logo From: union of (1) in-game team logos dumped by the DLL and
        # (2) any PNGs the user has added to the game's CustomLogos folder.
        # Fallback to the combined team list if the dump file is missing
        # (e.g. game hasn't been launched yet with the mod installed).
        dumped = get_game_team_logos()
        custom = get_custom_logo_names()
        merged = sorted(set((dumped or []) + (custom or [])), key=lambda s: s.lower())
        logo_names = merged or get_all_team_names()
        if logo_names:
            self.add_combo(parent, "Logo From",
                           ["", "RANDOM"] + logo_names,
                           f"Borrow logo from an in-game team OR a PNG in CustomLogos/ "
                           f"({len(custom)} custom logo(s) detected)")
        else:
            self.add_entry(parent, "Logo From", "Borrow logo from an in-game team")
        self.add_entry(parent, "Stat Scale", "1.0 = normal")

        section("Home Jersey Colors")
        self.add_color(parent, "Jersey Primary")
        self.add_color(parent, "Jersey Secondary")
        self.add_color(parent, "Jersey Accent")

        section("Away Jersey Colors")
        self.add_color(parent, "Away Primary")
        self.add_color(parent, "Away Secondary")
        self.add_color(parent, "Away Accent")

        section("Number Colors")
        self.add_color(parent, "Number Color Home")
        self.add_color(parent, "Number Color Away")

        section("Equipment Colors")
        for c in ["Helmet Color", "Helmet Secondary Color", "Helmet Tertiary Color",
                  "Gloves Color", "Gloves Secondary Color", "Gloves Tertiary Color",
                  "Pants Color", "Pants Secondary Color", "Pants Tertiary Color",
                  "Skates Color", "Blade Color", "Laces Color",
                  "Socks Color", "Socks Secondary Color", "Socks Tertiary Color",
                  "Bicep Color", "Stick Color"]:
            self.add_color(parent, c)

        section("Transition Colors (screen wipe)")
        self.add_color(parent, "Transition Primary")
        self.add_color(parent, "Transition Secondary")
        self.add_color(parent, "Transition Tertiary")

        section("Uniform Skins  (model picker — colors above only apply to 'standard' / 'team colors')")
        ttk.Label(parent,
            text="⚠ Picking a non-'standard' option (e.g. 'hockey fc', 'tycoons') uses that\n"
                 "   model's BAKED-IN colors — your color pickers above are ignored for that piece.",
            foreground="#a05000", font=("", 8), justify="left"
        ).pack(anchor="w", padx=4, pady=(0, 4))
        self.add_combo(parent, "Body", BODY_SKINS, "'standard' = uses Jersey colors")
        self.add_combo(parent, "Body Away", BODY_SKINS, "'standard' = uses Away colors")
        self.add_combo(parent, "Bicep", BICEP_SKINS, "'standard' = uses Bicep Color")
        self.add_combo(parent, "Bicep Away", BICEP_SKINS, "'standard' = uses Bicep Color")
        # Gloves and Pants: only 'standard' exists — auto-set on save
        ttk.Label(parent,
            text="Gloves & Pants: always 'standard' — set Gloves Color / Pants Color above",
            foreground="#777", font=("", 8)).pack(anchor="w", padx=4, pady=2)
        self.add_combo(parent, "Skates", SKATE_SKINS, "'standard' = uses Skates/Blade/Laces colors")
        self.add_combo(parent, "Skates Away", SKATE_SKINS, "'standard' = uses Skates/Blade/Laces colors")
        self.add_combo(parent, "Helmet", HELMET_SKINS,
                       "'team colors' = uses Helmet colors. 'none' = no helmet (Helmet colors ignored)")
        self.add_combo(parent, "Helmet Away", HELMET_SKINS,
                       "'team colors' = uses Helmet colors. 'none' = no helmet")
        self.add_combo(parent, "Stick", STICK_SKINS, "'team stick' = uses Stick Color")

        section("Starting Relics + Random Talents")
        self.add_listpicker(parent, "Team Relics", RELICS,
            f"Click + Add to pick from {len(RELICS)} relics (with descriptions)",
            supports_level=True)
        self.add_combo(parent, "Team Random Talents",
            [str(n) for n in range(0, 11)],
            "Every player on this team gets N random talents (0 = none)")
        self.add_listpicker(parent, "Team Random Pool", TALENTS,
            "Click 'Set all' for any talent, or pick specific talents to restrict the pool",
            is_pool=True)

        section("Bench")
        self.add_entry(parent, "Bench Size", "Scale of the bench boss (coach behind the bench). 2 = huge, 0.5 = tiny")
        self.add_combo(parent, "Bench Head", FACES, "Face/head for the bench boss (coach behind the bench)")

    def add_entry(self, parent, label, hint=None):
        w = LabeledEntry(parent, label, hint)
        w.pack(anchor="w", pady=1)
        self.widgets[label] = w

    def add_combo(self, parent, label, values, hint=None):
        w = LabeledCombo(parent, label, values, hint)
        w.pack(anchor="w", pady=1)
        self.widgets[label] = w

    def add_color(self, parent, label, hint=None):
        w = ColorPicker(parent, label, hint=hint)
        w.pack(anchor="w", pady=1)
        self.widgets[label] = w

    def add_listpicker(self, parent, label, entries, hint=None,
                        supports_level=False, is_pool=False):
        w = ListPicker(parent, label, entries=entries, hint=hint,
                        supports_level=supports_level, is_pool=is_pool)
        w.pack(anchor="w", pady=3, fill="x")
        self.widgets[label] = w

    def get_data(self):
        return {k: w.get() for k, w in self.widgets.items() if w.get()}

    def set_data(self, data):
        for k, w in self.widgets.items():
            if k in data:
                w.set(data[k])
        self._refresh_live()

    def load_dir(self, team_dir):
        self.loaded_dir = team_dir
        self.set_data(read_kv(os.path.join(team_dir, "team.txt")))

    def save_dir(self, team_dir):
        # Safety net: never write into a base game / auto-generated folder.
        # If caller asks us to, redirect the write to the user's library folder.
        if is_base_game_path(team_dir):
            team_dir = auto_copy_to_library(team_dir, is_team=True)
        os.makedirs(os.path.join(team_dir, "players"), exist_ok=True)
        data = self.get_data()
        data.setdefault("Gloves", "standard")
        data.setdefault("Pants", "standard")
        write_kv(os.path.join(team_dir, "team.txt"), data,
                 order=TEAM_FIELD_ORDER)
        self.loaded_dir = team_dir
        return team_dir


# ============================================================
#   CAMPAIGN EDITOR
# ============================================================
class CampaignEditor(ttk.Frame):
    def __init__(self, parent):
        super().__init__(parent)
        self.loaded_dir = None
        self.widgets = {}
        self._drag_idx = None
        self.build()

    def build(self):
        # Validation banner at the top — OUTSIDE the scroll area so it stays
        # visible no matter how far the user scrolls.
        self.validation = ValidationBanner(self)
        self.validation.pack(fill="x", padx=4, pady=(4, 2))

        # Whole editor body scrolls: the campaign settings, team list, player
        # teams section, custom squads and draft pool all stack vertically
        # and run past the window bottom at small heights. Wrap them in a
        # Canvas + vertical Scrollbar so nothing gets cut off.
        _scroll_host = ttk.Frame(self)
        _scroll_host.pack(fill="both", expand=True)
        self._editor_canvas = tk.Canvas(_scroll_host, highlightthickness=0)
        _editor_vscroll = ttk.Scrollbar(_scroll_host, orient="vertical",
                                        command=self._editor_canvas.yview)
        self._editor_canvas.configure(yscrollcommand=_editor_vscroll.set)
        self._editor_canvas.pack(side="left", fill="both", expand=True)
        _editor_vscroll.pack(side="right", fill="y")
        body = ttk.Frame(self._editor_canvas)
        self._editor_body_window = self._editor_canvas.create_window(
            (0, 0), window=body, anchor="nw")
        def _sync_scrollregion(_e=None):
            self._editor_canvas.configure(scrollregion=self._editor_canvas.bbox("all"))
        body.bind("<Configure>", _sync_scrollregion)
        # Make the inner frame match the canvas width so pack fill="x" works.
        def _sync_width(e):
            self._editor_canvas.itemconfigure(self._editor_body_window, width=e.width)
        self._editor_canvas.bind("<Configure>", _sync_width)
        # Mousewheel scrolls only while the pointer is over the editor.
        def _on_wheel(e):
            self._editor_canvas.yview_scroll(int(-1 * (e.delta / 120)), "units")
        self._editor_canvas.bind("<Enter>",
            lambda e: self._editor_canvas.bind_all("<MouseWheel>", _on_wheel))
        self._editor_canvas.bind("<Leave>",
            lambda e: self._editor_canvas.unbind_all("<MouseWheel>"))

        top = ttk.Frame(body)
        top.pack(fill="x", pady=5)

        ttk.Label(top, text="Campaign Settings", font=("", 11, "bold")).pack(anchor="w", pady=2)

        # ACT SEQUENCE — friendly per-slot builder (replaces raw text field)
        act_builder = ActSequenceBuilder(top, label="Act Sequence",
            hint=("Build the campaign map-by-map. 1 = Act 1 (easy), 2 = Act 2 (medium), "
                  "3 = Act 3 (boss — must be the final map).\n"
                  "Click a preset to start fast, then tweak."),
            on_change=lambda: self._refresh_live())
        act_builder.pack(anchor="w", fill="x", pady=(2, 6))
        self.widgets["Act Sequence"] = act_builder
        try:
            act_builder.var.trace_add("write", lambda *a: self._refresh_live())
        except Exception: pass

        # Replace Challenges is now handled per-Act-1-map inside the ActSequenceBuilder
        # (checkboxes on each Act 1 cell). Wire a proxy so save/load still sees
        # widgets["Replace Challenges"] with .get()/.set().
        class _RCProxy:
            def __init__(self, builder):
                self._builder = builder
                self.var = tk.StringVar()
            def get(self):
                return self._builder.get_replace_challenges()
            def set(self, s):
                self._builder.set_replace_challenges(s)
        self.widgets["Replace Challenges"] = _RCProxy(act_builder)

        # Remaining simple fields — rendered as checkboxes. `get()` still
        # returns "yes"/"no" strings so the save/load pipeline is unchanged.
        for k, hint in [
            ("Replace Soccer Ball", "Turn soccer balls back into regular pucks"),
            ("Replace Golf Ball", "Turn golf balls back into regular pucks"),
            ("Use Player Teams", "Enable player_teams/ folder (lets you mod the player's squad)"),
        ]:
            w = LabeledCheckbox(top, k, hint)
            w.pack(anchor="w", pady=1)
            self.widgets[k] = w
            try:
                w.var.trace_add("write", lambda *a: self._refresh_live())
            except Exception: pass

        # Live team-count readout that factors in Replace Challenges
        self._game_count_label = ttk.Label(top,
            text="Build the act sequence above to see how many teams you need.",
            font=("", 9, "bold"), foreground="#0066aa",
            wraplength=700, justify="left")
        self._game_count_label.pack(anchor="w", padx=4, pady=(6, 0))

        hdr = ttk.Frame(body)
        hdr.pack(fill="x", pady=(10, 2))
        ttk.Label(hdr, text="Teams (play order = folder name prefix)",
                  font=("", 11, "bold")).pack(side="left")
        ttk.Label(hdr, text="   drag to reorder",
                  foreground="#777", font=("", 8)).pack(side="left")

        row = ttk.Frame(body)
        row.pack(fill="both", expand=True)
        self.teams_list = tk.Listbox(row, height=14)
        self.teams_list.pack(side="left", fill="both", expand=True, padx=(0, 5))
        # Drag-to-reorder bindings
        self.teams_list.bind("<Button-1>", self._on_drag_start)
        self.teams_list.bind("<B1-Motion>", self._on_drag_motion)
        self.teams_list.bind("<ButtonRelease-1>", self._on_drag_drop)
        self.teams_list.bind("<Double-Button-1>", lambda e: self.edit_team())

        btns = ttk.Frame(row)
        btns.pack(side="left", fill="y")
        ttk.Button(btns, text="Add New", command=self.add_new_team, width=14).pack(pady=2)
        ttk.Button(btns, text="Import Team", command=self.import_team, width=14).pack(pady=2)
        ttk.Button(btns, text="Edit", command=self.edit_team, width=14).pack(pady=2)
        ttk.Button(btns, text="Remove", command=self.remove_team, width=14).pack(pady=2)
        ttk.Button(btns, text="Move Up", command=lambda: self.move_team(-1), width=14).pack(pady=2)
        ttk.Button(btns, text="Move Down", command=lambda: self.move_team(1), width=14).pack(pady=2)

        # === PLAYER TEAMS SECTION (starting teams + draft pool) ===
        pt_frame = ttk.LabelFrame(body, text=" Starting Teams (player picks at run start) ")
        pt_frame.pack(fill="x", padx=4, pady=(10, 4))

        ttk.Label(pt_frame,
            text="Edit the 4 built-in starting teams (Defense, Speedy, Basic, Trios),\n"
                 "OR add your own custom squads — each extra folder in player_teams/\n"
                 "shows up as a 5th, 6th, … option in the campaign squad-select menu.\n"
                 "Preset edits require 'Use Player Teams = yes' above; custom squads\n"
                 "are always available (additive — they never overwrite vanilla teams).",
            foreground="#555", font=("", 8), justify="left"
        ).pack(anchor="w", padx=8, pady=(4, 4))

        pt_btns = ttk.Frame(pt_frame)
        pt_btns.pack(fill="x", padx=8, pady=(0, 4))

        STARTING_TEAMS = ["Defense", "Speedy", "Basic", "Trios"]
        for team_name in STARTING_TEAMS:
            ttk.Button(pt_btns, text=f"Edit {team_name}",
                       command=lambda t=team_name: self._edit_player_team(t),
                       width=14).pack(side="left", padx=2)

        # Custom squads — list every non-preset folder under player_teams/,
        # with an Add button to create a new one and per-entry Edit buttons.
        custom_row = ttk.Frame(pt_frame)
        custom_row.pack(fill="x", padx=8, pady=(4, 2))
        ttk.Label(custom_row, text="Custom Squads:", font=("", 9, "bold")).pack(side="left")
        self._custom_squad_list = tk.Listbox(custom_row, height=5, width=40)
        self._custom_squad_list.pack(side="left", padx=6, fill="x", expand=True)
        cs_btns = ttk.Frame(custom_row)
        cs_btns.pack(side="left")
        ttk.Button(cs_btns, text="Add", command=self._add_custom_squad, width=10).pack(pady=1)
        ttk.Button(cs_btns, text="Edit", command=self._edit_custom_squad, width=10).pack(pady=1)
        ttk.Button(cs_btns, text="Remove", command=self._remove_custom_squad, width=10).pack(pady=1)

        # Draft pool
        dp_row = ttk.Frame(pt_frame)
        dp_row.pack(fill="x", padx=8, pady=(4, 4))
        ttk.Label(dp_row, text="Draft Pool:", font=("", 9, "bold")).pack(side="left")
        self._draft_list = tk.Listbox(dp_row, height=10, width=40)
        self._draft_list.pack(side="left", padx=6, fill="x", expand=True)

        dp_btns = ttk.Frame(dp_row)
        dp_btns.pack(side="left")
        # Free agents: the draft pool is fixed to the 7 vanilla free agents
        # that the game spawns. Names can't be changed (the DLL intentionally
        # skips name assignment for free agents so the rest of the mods
        # apply), and you can't add or remove entries — only edit stats,
        # skins, talents, and abilities on the existing 7.
        ttk.Button(dp_btns, text="Edit", command=self._edit_draft_player, width=8).pack(pady=1)
        ttk.Label(dp_btns,
                  text="(locked: 7 free agents\nno add/remove/rename)",
                  foreground="#aa5500", font=("", 8), justify="left"
                  ).pack(pady=(4, 1))

        self._refresh_live()

    # Drag-to-reorder handlers
    def _on_drag_start(self, event):
        idx = self.teams_list.nearest(event.y)
        if idx < 0 or idx >= self.teams_list.size():
            self._drag_idx = None
            return
        self._drag_idx = idx
        self._drag_moved = False  # track whether the user actually dragged

    def _on_drag_motion(self, event):
        if self._drag_idx is None: return
        cur = self.teams_list.nearest(event.y)
        if cur < 0 or cur == self._drag_idx: return
        self._drag_moved = True
        # Live-swap: move the dragged item to the new position
        item = self.teams_list.get(self._drag_idx)
        self.teams_list.delete(self._drag_idx)
        self.teams_list.insert(cur, item)
        self.teams_list.selection_clear(0, "end")
        self.teams_list.selection_set(cur)
        self._drag_idx = cur

    def _on_drag_drop(self, event):
        if not self._drag_moved or self._drag_idx is None or not self.loaded_dir:
            self._drag_idx = None
            return
        final_idx = self._drag_idx
        teams_now = [self.teams_list.get(i) for i in range(self.teams_list.size())]
        teams_dir = os.path.join(self.loaded_dir, "teams")

        import time
        stamp = int(time.time() * 1000) % 1000000
        tmp_prefix = f"__tdrag{stamp}_"

        # Build rename plan — only folders that actually exist on disk
        plan = []  # (src_path, tmp_path, final_path, original_name)
        for i, t in enumerate(teams_now):
            src = os.path.join(teams_dir, t)
            if not os.path.isdir(src):
                continue
            base = re.sub(r"^\d+\s+", "", t)
            tmp = os.path.join(teams_dir, f"{tmp_prefix}{i:03d}__")
            final = os.path.join(teams_dir, f"{i+1:02d} {base}")
            plan.append((src, tmp, final, t))

        renamed_p1 = []  # (tmp_path, orig_src) — for rollback
        try:
            for src, tmp, final, _ in plan:
                os.rename(src, tmp)
                renamed_p1.append((tmp, src))
            for src, tmp, final, _ in plan:
                os.rename(tmp, final)
            renamed_p1.clear()
        except Exception as e:
            # Rollback: restore any phase-1 renames
            for tmp_path, orig_src in reversed(renamed_p1):
                try:
                    if not os.path.isdir(orig_src):
                        os.rename(tmp_path, orig_src)
                except Exception:
                    pass
            messagebox.showerror("Reorder failed",
                f"{type(e).__name__}: {e}\n\nThe campaign was restored to its original order.")

        self._drag_idx = None
        self._drag_moved = False
        self.refresh_list()
        if 0 <= final_idx < self.teams_list.size():
            self.teams_list.selection_set(final_idx)

    def _refresh_live(self):
        try: self.validation.set_issues(self._validate())
        except Exception: pass
        # Update the live team-count readout from Act Sequence + Replace Challenges.
        # Challenge maps (Spartan / soccer / golf) only count as "games played" when
        # challenges are replaced for that act — which is the ONLY situation that
        # requires a team slot for that map.
        try:
            if not hasattr(self, "_game_count_label"):
                return
            seq = self.widgets["Act Sequence"].get().strip()
            if not seq:
                self._game_count_label.configure(
                    text="Build the act sequence above to see how many teams you need.")
                return
            try:
                acts = [int(p.strip()) for p in seq.split(",") if p.strip()]
            except ValueError:
                acts = []
            if not acts:
                self._game_count_label.configure(
                    text="Build the act sequence above to see how many teams you need.")
                return

            # The ActSequenceBuilder already computes accurate per-map game counts
            # (including per-map Spartan replacement state). Its summary label is
            # built into the widget itself. The game-count label below it shows the
            # team-count conclusion for the user.
            ab = self.widgets.get("Act Sequence")
            if ab and hasattr(ab, "get_replace_challenges"):
                rc_val = ab.get_replace_challenges()
                if rc_val == "yes":
                    sp_note = "All Act 1 Spartans replaced — each Act 1 map = 4 games."
                elif rc_val == "no":
                    sp_note = "No Spartans replaced — each Act 1 map = 3 games."
                else:
                    sp_note = f"Per-map Spartan replacement: {rc_val}"
            else:
                sp_note = ""
            self._game_count_label.configure(text=sp_note)
        except Exception: pass

    def _validate(self):
        issues = []
        data = {k: w.get() for k, w in self.widgets.items() if w.get()}
        seq = data.get("Act Sequence", "").strip()
        if seq:
            try:
                acts = [int(p.strip()) for p in seq.split(",") if p.strip()]
                if not acts:
                    issues.append("Act Sequence is empty.")
                else:
                    if acts[-1] != 3:
                        issues.append(f"Act Sequence should end in 3 (boss act); currently ends in {acts[-1]}.")
                    for a in acts:
                        if a < 1 or a > 3:
                            issues.append(f"Act Sequence value {a} is outside 1–3.")
                            break
            except ValueError:
                issues.append(f"Act Sequence '{seq}' has non-integer values.")
        else:
            issues.append("Act Sequence is empty — required to play the campaign.")
        # yes/no fields
        for k in ("Replace Soccer Ball", "Replace Golf Ball", "Use Player Teams"):
            v = data.get(k, "").strip().lower()
            if v and v not in ("yes", "no"):
                issues.append(f"{k} should be 'yes' or 'no' (got '{v}').")
        return issues

    # --- Player Teams (starting team editor) ---
    def _pt_dir(self):
        if not self.loaded_dir: return None
        return os.path.join(self.loaded_dir, "player_teams")

    def _ensure_pt_dir(self, team_name=None):
        """Create player_teams/ and optional team subfolder."""
        pt = self._pt_dir()
        if not pt:
            messagebox.showwarning("Save first", "Save the campaign before editing player teams.")
            return None
        os.makedirs(pt, exist_ok=True)
        if team_name:
            td = os.path.join(pt, team_name)
            os.makedirs(os.path.join(td, "players"), exist_ok=True)
            if not os.path.isfile(os.path.join(td, "team.txt")):
                write_kv(os.path.join(td, "team.txt"),
                         {"Team Name": team_name, "Import Team": team_name},
                         order=TEAM_FIELD_ORDER)
            return td
        return pt

    def _edit_player_team(self, team_name):
        td = self._ensure_pt_dir(team_name)
        if td:
            open_team_editor(td)

    # --- Custom squads (extra player-team folders that become 5th+ options) ---
    _PRESET_SQUAD_KEYS = {"basic", "defense", "speedy", "speed", "trios", "trio", "draft_pool", "draft pool"}

    def _refresh_custom_squads(self):
        if not hasattr(self, "_custom_squad_list"): return
        self._custom_squad_list.delete(0, "end")
        pt = self._pt_dir()
        if not pt or not os.path.isdir(pt): return
        for name in sorted(os.listdir(pt)):
            full = os.path.join(pt, name)
            if not os.path.isdir(full): continue
            if name.lower() in self._PRESET_SQUAD_KEYS: continue
            self._custom_squad_list.insert("end", name)

    def _add_custom_squad(self):
        pt = self._ensure_pt_dir()
        if not pt: return
        name = _prompt_string("New Custom Squad", "Squad folder name (no slashes):")
        if not name: return
        safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
        if not safe: return
        if safe.lower() in self._PRESET_SQUAD_KEYS:
            messagebox.showwarning("Reserved name",
                f"'{safe}' is a built-in squad preset. Pick a different name.")
            return
        target = os.path.join(pt, safe)
        if os.path.exists(target):
            messagebox.showwarning("Already exists", f"A squad folder named '{safe}' already exists.")
            return
        os.makedirs(os.path.join(target, "players"), exist_ok=True)
        write_kv(os.path.join(target, "team.txt"),
                 {"Team Name": safe, "Import Team": "Greasy Lettuce"},
                 order=TEAM_FIELD_ORDER)
        self._refresh_custom_squads()
        open_team_editor(target)

    def _edit_custom_squad(self):
        sel = self._custom_squad_list.curselection()
        if not sel: return
        name = self._custom_squad_list.get(sel[0])
        pt = self._pt_dir()
        if not pt: return
        target = os.path.join(pt, name)
        if os.path.isdir(target):
            open_team_editor(target)

    def _remove_custom_squad(self):
        sel = self._custom_squad_list.curselection()
        if not sel: return
        name = self._custom_squad_list.get(sel[0])
        pt = self._pt_dir()
        if not pt: return
        target = os.path.join(pt, name)
        if not os.path.isdir(target): return
        if not messagebox.askyesno("Delete Custom Squad",
                f"Delete the custom squad '{name}' and all its players?\nThis cannot be undone."):
            return
        import shutil
        try:
            shutil.rmtree(target)
        except Exception as e:
            messagebox.showerror("Delete failed", str(e))
        self._refresh_custom_squads()

    def _refresh_draft_list(self):
        self._draft_list.delete(0, "end")
        pt = self._pt_dir()
        if not pt: return
        dp = os.path.join(pt, "draft_pool")
        if not os.path.isdir(dp): return
        for f in sorted(os.listdir(dp)):
            if f.endswith(".txt"):
                self._draft_list.insert("end", f[:-4])

    def _edit_draft_player(self):
        sel = self._draft_list.curselection()
        if not sel: return
        name = self._draft_list.get(sel[0])
        pt = self._pt_dir()
        if not pt: return
        path = os.path.join(pt, "draft_pool", name + ".txt")
        open_player_editor(path, on_save=self._refresh_draft_list)

    def _add_draft_player(self):
        pt = self._ensure_pt_dir()
        if not pt: return
        dp = os.path.join(pt, "draft_pool")
        os.makedirs(dp, exist_ok=True)
        name = _prompt_string("New Draft Player", "Player name:")
        if not name: return
        safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
        path = os.path.join(dp, safe + ".txt")
        if not os.path.exists(path):
            write_kv(path, {"Name": name}, order=PLAYER_FIELD_ORDER)
        open_player_editor(path, on_save=self._refresh_draft_list)

    def _remove_draft_player(self):
        sel = self._draft_list.curselection()
        if not sel: return
        name = self._draft_list.get(sel[0])
        if not messagebox.askyesno("Remove", f"Delete draft player '{name}'?"):
            return
        pt = self._pt_dir()
        if not pt: return
        path = os.path.join(pt, "draft_pool", name + ".txt")
        try: os.remove(path)
        except Exception: pass
        self._refresh_draft_list()

    def load_dir(self, campaign_dir):
        self.loaded_dir = campaign_dir
        self.set_data(read_kv(os.path.join(campaign_dir, "campaign.txt")))
        self.refresh_list()
        self._refresh_draft_list()
        self._refresh_custom_squads()

    def set_data(self, data):
        for k, w in self.widgets.items():
            if k in data:
                w.set(data[k])
        self._refresh_live()

    def refresh_list(self):
        self.teams_list.delete(0, "end")
        if not self.loaded_dir:
            self._refresh_live()
            return
        teams_dir = os.path.join(self.loaded_dir, "teams")
        if not os.path.isdir(teams_dir):
            self._refresh_live()
            return
        for t in sorted(os.listdir(teams_dir)):
            if os.path.isdir(os.path.join(teams_dir, t)):
                self.teams_list.insert("end", t)
        self._refresh_live()

    def _next_prefix(self):
        """Return next 'NN ' prefix for a new team folder."""
        if not self.loaded_dir: return "01 "
        teams_dir = os.path.join(self.loaded_dir, "teams")
        if not os.path.isdir(teams_dir): return "01 "
        n = len([d for d in os.listdir(teams_dir)
                 if os.path.isdir(os.path.join(teams_dir, d))])
        return f"{n + 1:02d} "

    def add_new_team(self):
        if not self.loaded_dir:
            messagebox.showwarning("No Campaign", "Save the campaign first.")
            return
        name = _prompt_string("New Team", "Team folder name (e.g. Vancouver):")
        if not name: return
        folder = self._next_prefix() + name
        team_dir = os.path.join(self.loaded_dir, "teams", folder)
        os.makedirs(os.path.join(team_dir, "players"), exist_ok=True)
        # Create a skeleton team.txt
        with open(os.path.join(team_dir, "team.txt"), "w", encoding="utf-8") as f:
            f.write(f"Team Name               = {name}\n")
        self.refresh_list()
        # Open editor
        open_team_editor(team_dir, on_save=self.refresh_list)

    def import_team(self):
        """Copy one or many teams from the library/base game/custom folders."""
        if not self.loaded_dir:
            messagebox.showwarning("No Campaign", "Save the campaign first.")
            return
        open_multi_team_browser(on_pick=self._do_import_many)

    def _do_import_many(self, picks):
        """picks = list of (src_campaign, src_team) tuples."""
        import shutil
        imported = []
        failed = []
        for src_campaign, src_team in picks:
            try:
                if src_campaign == LIBRARY_SOURCE:
                    src = resolve_library_team_dir(src_team) or os.path.join(TEAM_LIBRARY_DIR, src_team)
                else:
                    src = os.path.join(CAMPAIGNS_DIR, src_campaign, "teams", src_team)
                if not os.path.isdir(src):
                    failed.append(f"{src_team}: not found"); continue
                m = re.match(r"^\d+\s+(.*)$", src_team)
                base_name = m.group(1) if m else src_team
                dst_name = self._next_prefix() + base_name
                dst = os.path.join(self.loaded_dir, "teams", dst_name)
                shutil.copytree(src, dst)
                imported.append(dst_name)
            except Exception as e:
                failed.append(f"{src_team}: {type(e).__name__}: {e}")
        self.refresh_list()
        msg = f"Imported {len(imported)} team(s):\n  " + "\n  ".join(imported)
        if failed:
            msg += f"\n\nFailed:\n  " + "\n  ".join(failed)
        messagebox.showinfo("Import Complete", msg)

    def edit_team(self):
        sel = self.teams_list.curselection()
        if not sel: return
        team = self.teams_list.get(sel[0])
        team_dir = os.path.join(self.loaded_dir, "teams", team)
        open_team_editor(team_dir, on_save=self.refresh_list)

    def remove_team(self):
        sel = self.teams_list.curselection()
        if not sel: return
        team = self.teams_list.get(sel[0])
        if not messagebox.askyesno("Remove Team", f"Delete '{team}' and all its players?"):
            return
        import shutil
        shutil.rmtree(os.path.join(self.loaded_dir, "teams", team))
        self.refresh_list()

    def move_team(self, direction):
        """Renumber team folders to reorder."""
        sel = self.teams_list.curselection()
        if not sel: return
        idx = sel[0]
        teams = [self.teams_list.get(i) for i in range(self.teams_list.size())]
        new_idx = idx + direction
        if new_idx < 0 or new_idx >= len(teams): return
        teams[idx], teams[new_idx] = teams[new_idx], teams[idx]
        # Renumber all
        teams_dir = os.path.join(self.loaded_dir, "teams")
        # Rename to temp names first to avoid collisions
        for i, t in enumerate(teams):
            src = os.path.join(teams_dir, t)
            tmp = os.path.join(teams_dir, f"__tmp_{i}__")
            os.rename(src, tmp)
        for i, t in enumerate(teams):
            tmp = os.path.join(teams_dir, f"__tmp_{i}__")
            base = re.sub(r"^\d+\s+", "", t)
            new_name = f"{i+1:02d} {base}"
            os.rename(tmp, os.path.join(teams_dir, new_name))
        self.refresh_list()
        self.teams_list.selection_set(new_idx)

    def save_dir(self, campaign_dir):
        # Sanitize: strip trailing whitespace from path components (Windows fails silently otherwise)
        parent, leaf = os.path.split(campaign_dir.rstrip())
        leaf = leaf.strip()
        if not leaf:
            raise ValueError("Campaign folder name is empty after stripping whitespace.")
        campaign_dir = os.path.join(parent, leaf) if parent else leaf
        os.makedirs(campaign_dir, exist_ok=True)
        if not os.path.isdir(campaign_dir):
            raise OSError(f"Failed to create campaign folder: {campaign_dir}")
        data = {k: w.get() for k, w in self.widgets.items() if w.get()}
        path = os.path.join(campaign_dir, "campaign.txt")
        with open(path, "w", encoding="utf-8") as f:
            f.write("# Campaign Settings\n")
            for k in ["Act Sequence", "Replace Challenges", "Replace Soccer Ball",
                      "Replace Golf Ball", "Use Player Teams"]:
                if k in data:
                    f.write(f"{k:24s}= {data[k]}\n")
        os.makedirs(os.path.join(campaign_dir, "teams"), exist_ok=True)
        self.loaded_dir = campaign_dir


# ============================================================
#   EDITOR HOST  (wraps a Toplevel OR a tab in the main Notebook)
# ============================================================
_TAB_HOST = None  # Set by TabbedMainMenu to a ttk.Notebook; None = use Toplevels


def _fit_geometry(win, w, h):
    """Set window geometry but never larger than 95% of the user's screen.
    Use this everywhere we open a Toplevel so Steam Deck (1280x720) and
    laptops with low resolution still see the full UI."""
    try:
        sw = win.winfo_screenwidth()
        sh = win.winfo_screenheight()
        w = min(int(w), int(sw * 0.95))
        h = min(int(h), int(sh * 0.92))
        win.geometry(f"{w}x{h}")
    except Exception:
        win.geometry(f"{w}x{h}")


class _EditorHost:
    """Host for one editor window. If a notebook is registered globally
       (_TAB_HOST), adds a tab and returns its content Frame as .container.
       Otherwise spawns a Toplevel and .container is that Toplevel."""
    def __init__(self, title, size=None):
        self._title = title
        if _TAB_HOST is not None:
            self._tab = True
            self._notebook = _TAB_HOST
            self._outer = ttk.Frame(self._notebook)
            # Tab text uses ×-suffix so user can click to close.
            # Right-click on any tab also closes it via the bind set up by MainMenu.
            self._notebook.add(self._outer, text=f"{title}  ×")
            self._notebook.select(self._outer)
            self.container = ttk.Frame(self._outer)
            self.container.pack(fill="both", expand=True)
        else:
            self._tab = False
            self._toplevel = tk.Toplevel()
            self._toplevel.title(title)
            if size:
                # Cap size to 95% of screen so editors don't open off-screen
                # on smaller laptops / Steam Deck.
                try:
                    w, h = (int(x) for x in size.lower().split("x"))
                    sw = self._toplevel.winfo_screenwidth()
                    sh = self._toplevel.winfo_screenheight()
                    w = min(w, int(sw * 0.95))
                    h = min(h, int(sh * 0.92))
                    self._toplevel.geometry(f"{w}x{h}")
                except Exception:
                    self._toplevel.geometry(size)
            self.container = self._toplevel

    def set_title(self, t):
        self._title = t
        try:
            if self._tab:
                self._notebook.tab(self._outer, text=t)
            else:
                self._toplevel.title(t)
        except Exception: pass

    def destroy(self):
        try:
            if self._tab:
                self._notebook.forget(self._outer)
                self._outer.destroy()
            else:
                self._toplevel.destroy()
        except Exception: pass

    def attach_tracker(self, title, save_fn=None):
        """Create a DirtyTracker for this editor and mount it on the tab
        widget (or Toplevel) so the close handlers can find it. Returns
        the tracker so the caller can wire save_fn / mark_clean."""
        tracker = DirtyTracker(title=title)
        if save_fn is not None:
            tracker.save_fn = save_fn
        target = self._outer if self._tab else self._toplevel
        try: target._dirty_tracker = tracker
        except Exception: pass
        if not self._tab:
            # Toplevel: intercept window close to check dirty state.
            def _on_close():
                if not confirm_tab_close(self._toplevel, self._toplevel): return
                try: self._toplevel.destroy()
                except Exception: pass
            try: self._toplevel.protocol("WM_DELETE_WINDOW", _on_close)
            except Exception: pass
        return tracker

    def finalize_tracking(self, tracker):
        """Call AFTER the form is built and initial values are loaded.
        Walks widgets to attach mark_dirty bindings, then resets dirty=False."""
        target = self._outer if self._tab else self._toplevel
        try:
            attach_dirty_tracking(target, tracker)
            # Give tk a tick to process any pending trace callbacks from the
            # initial load, then reset the flag.
            target.after(50, tracker.mark_clean)
        except Exception: pass


# ============================================================
#   WINDOW OPENERS
# ============================================================
def _prompt_string(title, prompt):
    from tkinter.simpledialog import askstring
    return askstring(title, prompt)


def _ask_pick(title, prompt, items, parent=None):
    """Modal picker with a searchable listbox. Returns the picked item or None."""
    dlg = tk.Toplevel(parent)
    dlg.title(title)
    _fit_geometry(dlg, 460, 460)
    if parent: dlg.transient(parent)
    dlg.grab_set()

    ttk.Label(dlg, text=prompt, font=("", 10, "bold")).pack(
        anchor="w", padx=10, pady=(10, 2))

    search_var = tk.StringVar()
    search_row = ttk.Frame(dlg)
    search_row.pack(fill="x", padx=10)
    ttk.Label(search_row, text="Filter:").pack(side="left")
    ttk.Entry(search_row, textvariable=search_var).pack(
        side="left", fill="x", expand=True, padx=4)

    lst = tk.Listbox(dlg, height=16)
    lst.pack(fill="both", expand=True, padx=10, pady=6)

    def refresh(*a):
        q = search_var.get().strip().lower()
        lst.delete(0, "end")
        for it in items:
            if not q or q in it.lower():
                lst.insert("end", it)
    search_var.trace_add("write", refresh)
    refresh()

    result = {"v": None}
    def ok(*a):
        sel = lst.curselection()
        if not sel: return
        result["v"] = lst.get(sel[0])
        dlg.destroy()

    btns = ttk.Frame(dlg)
    btns.pack(fill="x", pady=8)
    ttk.Button(btns, text="Open", command=ok, width=14).pack(side="right", padx=8)
    ttk.Button(btns, text="Cancel", command=dlg.destroy, width=14).pack(side="right")
    lst.bind("<Double-Button-1>", ok)
    dlg.bind("<Return>", ok)
    dlg.wait_window()
    return result["v"]


def _ask_choice(title, prompt, options, parent=None):
    """Modal dialog that returns the user's selection from a list, or None."""
    dlg = tk.Toplevel(parent)
    dlg.title(title)
    _fit_geometry(dlg, 420, 340)
    if parent: dlg.transient(parent)
    dlg.grab_set()

    ttk.Label(dlg, text=prompt, wraplength=380).pack(padx=10, pady=8, anchor="w")

    var = tk.StringVar(value=options[0] if options else "")
    combo = ttk.Combobox(dlg, textvariable=var, values=options, state="readonly", width=30)
    combo.pack(padx=10, pady=4)

    result = {"v": None}
    def ok():
        result["v"] = var.get().strip()
        dlg.destroy()
    btns = ttk.Frame(dlg)
    btns.pack(fill="x", pady=10)
    ttk.Button(btns, text="OK", command=ok, width=14).pack(side="right", padx=8)
    ttk.Button(btns, text="Cancel", command=dlg.destroy, width=14).pack(side="right")
    dlg.bind("<Return>", lambda e: ok())
    dlg.wait_window()
    return result["v"]


LIBRARY_DIR = os.path.join(SCRIPT_DIR, "library")
PLAYER_LIBRARY_DIR = os.path.join(LIBRARY_DIR, "players")
TEAM_LIBRARY_DIR = os.path.join(LIBRARY_DIR, "teams")


# ============================================================
#   EXPORT TO PLAY NOW (custom players / teams in-game editor)
# ============================================================

def find_game_save_dir():
    """Find Tape to Tape's TeamDataModels folder (where Play Now reads
    CustomForward/CustomGoalie/CustomTeam .json from)."""
    import platform
    home = os.path.expanduser("~")
    candidates = []
    if platform.system() == "Windows":
        candidates.append(os.path.join(
            home, "AppData", "LocalLow", "Excellent Rectangle",
            "Tape to Tape", "TeamDataModels"))
    # Linux / Proton path
    candidates.append(os.path.join(
        home, ".local", "share", "Steam", "steamapps", "compatdata",
        "1795640", "pfx", "drive_c", "users", "steamuser", "AppData",
        "LocalLow", "Excellent Rectangle", "Tape to Tape", "TeamDataModels"))
    candidates.append(os.path.join(
        home, "Library", "Application Support", "Excellent Rectangle",
        "Tape to Tape", "TeamDataModels"))
    for c in candidates:
        if os.path.isdir(c):
            return c
    return None


def find_custom_logos_dir():
    """Find Tape to Tape's CustomLogos folder — sibling of TeamDataModels
    under the LocalLow save root. Contains user-added PNGs plus DLL-dumped
    in-game team logos."""
    tdm = find_game_save_dir()
    if tdm:
        candidate = os.path.join(os.path.dirname(tdm), "CustomLogos")
        if os.path.isdir(candidate):
            return candidate
    import platform
    home = os.path.expanduser("~")
    candidates = []
    if platform.system() == "Windows":
        candidates.append(os.path.join(
            home, "AppData", "LocalLow", "Excellent Rectangle",
            "Tape to Tape", "CustomLogos"))
    candidates.append(os.path.join(
        home, ".local", "share", "Steam", "steamapps", "compatdata",
        "1795640", "pfx", "drive_c", "users", "steamuser", "AppData",
        "LocalLow", "Excellent Rectangle", "Tape to Tape", "CustomLogos"))
    candidates.append(os.path.join(
        home, "Library", "Application Support", "Excellent Rectangle",
        "Tape to Tape", "CustomLogos"))
    for c in candidates:
        if os.path.isdir(c):
            return c
    return None


def get_custom_logo_names():
    """Return sorted list of PNG basenames (no extension) from the game's
    CustomLogos folder. These are the logos the game will recognise when a
    team references them by name."""
    d = find_custom_logos_dir()
    if not d:
        return []
    try:
        out = []
        for f in os.listdir(d):
            if f.lower().endswith(".png"):
                out.append(os.path.splitext(f)[0])
        return sorted(out, key=lambda s: s.lower())
    except Exception:
        return []


def _rgb_to_color_dict(rgb_str):
    """Convert '128, 64, 200' or [r,g,b] to {'r':float,'g':float,'b':float,'a':1.0}."""
    if not rgb_str:
        return {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
    try:
        if isinstance(rgb_str, str):
            parts = [int(p.strip()) for p in rgb_str.split(",") if p.strip().isdigit()]
        else:
            parts = list(rgb_str)
        if len(parts) >= 3:
            return {"r": parts[0]/255.0, "g": parts[1]/255.0, "b": parts[2]/255.0, "a": 1.0}
    except Exception:
        pass
    return {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}


_FACE_FULL_PATHS = [
    "Faces/Twinfalls/Wiener", "Faces/Twinfalls/Haggis", "Faces/Twinfalls/Jerky", "Faces/Twinfalls/Crockett",
    "Faces/Toronto/Mathieu", "Faces/Toronto/Jelly", "Faces/Toronto/Kilmore", "Faces/Toronto/Spark",
    "Faces/Toronto/Dord", "Faces/Toronto/Popping",
    "Faces/Chicago/Angus", "Faces/Chicago/Rory", "Faces/Chicago/Grohl", "Faces/Chicago/Chicos",
    "Faces/Chicago/Chapstick", "Faces/Chicago/Louder", "Faces/Chicago/Angus_Pixel",
    "Faces/Canadians/Captain", "Faces/Canadians/Poule", "Faces/Canadians/Gratz",
    "Faces/Midwest/Brie", "Faces/Midwest/Amber", "Faces/Midwest/Mental", "Faces/Midwest/Rochefort",
    "Faces/Moutaineers/Krupp", "Faces/Moutaineers/Wurst", "Faces/Moutaineers/Torte",
    "Faces/Moutaineers/Furter", "Faces/Moutaineers/Pianist",
    "Faces/Princess/Joan", "Faces/Princess/Clementine", "Faces/Princess/Boni",
    "Faces/Tycoons/Tycoons_Large", "Faces/Tycoons/Tycoons_Small",
    "Faces/Tycoons/Tycoons_Elite", "Faces/Tycoons/Tycoons_Lady",
    "Faces/Prisoners/Dalton", "Faces/Prisoners/Ma", "Faces/Prisoners/Averell", "Faces/Prisoners/Joe",
    "Faces/Knights/Prince", "Faces/Knights/Lancelov",
    "Faces/Cultists/Cultist", "Faces/Cultists/Jelly_Evil", "Faces/Cultists/Rory_Evil", "Faces/Cultists/Dord_Evil",
    "Faces/HockeyFC/Knudribble", "Faces/HockeyFC/Zidanejad", "Faces/HockeyFC/OHenry",
    "Faces/HockeyFC/Icekicks", "Faces/HockeyFC/Ronaldo", "Faces/HockeyFC/Messier",
    "Faces/HockeyFC/Maroondona", "Faces/HockeyFC/Backham", "Faces/HockeyFC/Ehrhoffaldo",
    "Faces/Golfers/Golfer_Lady", "Faces/Golfers/Golfer_Ramirez",
    "Faces/Golfers/Golfer_Elite", "Faces/Golfers/Golfer_Whacker", "Faces/Golfers/Golfer_Gillman",
    "Faces/Disco/Oioioi", "Faces/Referees/Gedeon", "Faces/Custom/Helmet_Face",
    "Faces/Anyteam/Nasher", "Faces/Anyteam/Chickensneeze", "Faces/Anyteam/Onepunch",
    "Faces/Anyteam/Bench_Kovalski", "Faces/Anyteam/Bench_Bench",
    "Faces/Anyteam/Bench_Brewster", "Faces/Anyteam/Bench_Kirby",
    "Faces/Anyteam/Bench_Buttface", "Faces/Anyteam/Bench_Stumple",
]
# Short-name → full path lookup (mirrors DLL ResolveSkin face table)
_FACE_LOOKUP = {p.rsplit("/", 1)[1].lower(): p for p in _FACE_FULL_PATHS}


def _face_skin_path(face_name, default=""):
    """Resolve a short face name to its full Faces/Team/Name path.
    Mirrors Plugin.ResolveSkin face lookup so Play Now exports write the
    same paths the game engine expects."""
    if not face_name:
        return default
    s = face_name.strip()
    if "/" in s:
        return s
    lo = s.lower()
    # Exact match
    if lo in _FACE_LOOKUP:
        return _FACE_LOOKUP[lo]
    # Underscore/space-normalised match
    lo_norm = lo.replace(" ", "_")
    for key, path in _FACE_LOOKUP.items():
        if lo_norm == key or lo.replace("_", "") == key.replace("_", ""):
            return path
    # Not in table — return as-is (game may handle it directly)
    return s


# Goalie helmet friendly name -> full asset path. Mirrors
# Plugin.ResolveGoalieSkin("helmet", ...) so Play Now JSON exports write
# real paths the game engine recognises. Campaign configs are still
# written as friendly names and resolved by the DLL at runtime.
GOALIE_HELMET_PATHS = {
    "standard": "Helmet/Helmet_Customization_colors",
    "team colors": "Helmet/Helmet_Customization_colors",
    "default": "Helmet/Helmet_Customization_colors",
    "colored": "Helmet/Helmet_Customization_colors",
    "canadians": "Helmet/Helmet_Canadians",
    "cheese": "Helmet/Helmet_Cheese",
    "cultists": "Helmet/Helmet_Cultists",
    "disco": "Helmet/Helmet_Disco",
    "figure_skaters": "Helmet/Helmet_Figure_Skaters",
    "figure skaters": "Helmet/Helmet_Figure_Skaters",
    "golfers": "Helmet/Helmet_Golfers",
    "hockey_fc": "Helmet/Helmet_HockeyFC",
    "hockey fc": "Helmet/Helmet_HockeyFC",
    "hockeyfc": "Helmet/Helmet_HockeyFC",
    "knights": "Helmet/Helmet_Knights",
    "meatballs": "Helmet/Helmet_Meatballs",
    "mountaineers": "Helmet/Helmet_Mountaineers",
    "princess": "Helmet/Helmet_Princess",
    "prisoners": "Helmet/Helmet_Prisoners",
    "referees": "Helmet/Helmet_Referees",
    "referee": "Helmet/Helmet_Referees",
    "toronto": "Helmet/Helmet_Toronto",
    "tycoons": "Helmet/Helmet_Tycoons",
}


def _resolve_goalie_helmet(val):
    """Translate a GUI friendly name (e.g. 'standard', 'canadians') into
    the full Helmet/Helmet_* asset path. Pass-through for already-resolved
    paths; empty input returns the team-tinted default."""
    if not val:
        return "Helmet/Helmet_Customization_colors"
    s = val.strip()
    if "/" in s:
        return s
    return GOALIE_HELMET_PATHS.get(s.lower().replace("_", " "),
                                   GOALIE_HELMET_PATHS.get(s.lower(),
                                   "Helmet/Helmet_Customization_colors"))


def _resolve_fwd_skin(val, slot="body"):
    """Resolve a forward skin friendly name to the full asset path.
    Mirrors Plugin.ResolveSkin for Play Now JSON export."""
    if not val:
        return {"body": "Body/Customization/Customization_colors",
                "stick": "Sticks/Customization/Customization_colors"}.get(slot, "")
    s = val.strip()
    if "/" in s:
        return s
    lo = s.lower().replace("_", " ")
    if lo in ("standard", "team colors", "default", "team stick", "colored stick"):
        if slot == "stick":
            return "Sticks/Customization/Customization_colors"
        return "Body/Customization/Customization_colors"
    if slot == "body":
        return {
            "tycoons":          "Body/Tycoons/Tycoons",
            "princess":         "Body/Princess/Princess",
            "golfers":          "Body/Golfers/Golfers",
            "prisoners":        "Body/Prisoners/Prisoners",
            "mountaineers":     "Body/Mountaineers/Mountaineers",
            "mountaineers beer":"Body/Mountaineers/Mountaineers_Beer",
            "hockey fc":        "Body/HockeyFC/HockeyFC",
            "figure skaters":   "Body/Figure_Skaters/Figure_Skaters",
            "referee":          "Body/Alumni/Ref_Alumni",
        }.get(lo, "Body/Customization/Customization_colors")
    if slot == "stick":
        return {
            "sword": "Sticks/Sword",
            "golf":  "Sticks/Golf_Iron",
        }.get(lo, "Sticks/Customization/Customization_colors")
    return s


def _resolve_gk_skin(val, slot):
    """Resolve a goalie skin friendly name to the full asset path.
    Mirrors Plugin.ResolveGoalieSkin for Play Now JSON export."""
    _defaults = {
        "helmet":  "Helmet/Helmet_Customization_colors",
        "body":    "Body/Customization_colors",
        "glove":   "Body_Glove/Customization/Customization_colors",
        "blocker": "Body_Blocker/Customization/Customization_colors",
        "pads":    "Body_Pads/Customization/Customization_colors",
        "stick":   "Body_Stick/Customization/Customization_colors",
    }
    if not val:
        return _defaults.get(slot, "")
    s = val.strip()
    if "/" in s:
        return s
    lo = s.lower().replace("_", " ")
    if lo in ("standard", "team colors", "default", "colored"):
        return _defaults.get(slot, "")
    if slot == "helmet":
        return {
            "canadians":    "Helmet/Helmet_Canadians",
            "cheese":       "Helmet/Helmet_Cheese",
            "cultists":     "Helmet/Helmet_Cultists",
            "disco":        "Helmet/Helmet_Disco",
            "figure skaters":"Helmet/Helmet_Figure_Skaters",
            "golfers":      "Helmet/Helmet_Golfers",
            "hockey fc":    "Helmet/Helmet_HockeyFC",
            "hockeyfc":     "Helmet/Helmet_HockeyFC",
            "knights":      "Helmet/Helmet_Knights",
            "meatballs":    "Helmet/Helmet_Meatballs",
            "mountaineers": "Helmet/Helmet_Mountaineers",
            "princess":     "Helmet/Helmet_Princess",
            "prisoners":    "Helmet/Helmet_Prisoners",
            "referees":     "Helmet/Helmet_Referees",
            "referee":      "Helmet/Helmet_Referees",
            "toronto":      "Helmet/Helmet_Toronto",
            "tycoons":      "Helmet/Helmet_Tycoons",
        }.get(lo, _defaults["helmet"])
    if slot == "body":
        return {
            "figure skaters":"Body/Figure_Skaters",
            "golfers":       "Body/Golfers",
            "hockey fc":     "Body/HockeyFC",
            "hockeyfc":      "Body/HockeyFC",
            "knights":       "Body/Knights",
            "mountaineers":  "Body/Mountaineers",
            "princess":      "Body/Princess",
            "prisoners":     "Body/Prisoners",
            "referees":      "Body/Referees",
            "referee":       "Body/Referees",
            "tycoons":       "Body/Tycoons",
        }.get(lo, _defaults["body"])
    if slot == "glove":
        return {
            "brown":         "Body_Glove/Brown",
            "figure skaters":"Body_Glove/Figure_Skaters",
            "golfers":       "Body_Glove/Golfers",
            "hockey fc":     "Body_Glove/Hockey_FC",
            "hockeyfc":      "Body_Glove/Hockey_FC",
            "knights":       "Body_Glove/Knights",
            "tycoons":       "Body_Glove/Tycoons",
        }.get(lo, _defaults["glove"])
    if slot == "blocker":
        return {
            "brown":         "Body_Blocker/Brown",
            "figure skaters":"Body_Blocker/Figure_Skaters",
            "golfers":       "Body_Blocker/Golfers",
            "knights":       "Body_Blocker/Knights",
            "tycoons":       "Body_Blocker/Tycoons",
        }.get(lo, _defaults["blocker"])
    if slot == "pads":
        return {
            "brown":         "Body_Pads/Brown",
            "figure skaters":"Body_Pads/Figure_Skaters",
            "hockey fc":     "Body_Pads/Hockey_FC",
            "hockeyfc":      "Body_Pads/Hockey_FC",
            "tycoons":       "Body_Pads/Tycoons",
        }.get(lo, _defaults["pads"])
    if slot == "stick":
        return {
            "figure skaters":"Body_Stick/Figure_Skaters",
            "tycoons":       "Body_Stick/Tycoons",
        }.get(lo, _defaults["stick"])
    return s


def _player_data_to_custom_forward(data, import_id=None, import_number=67):
    """Convert our key=value player data dict into the game's CustomForward
    JSON format. Skips talent/ability IDs — those require GUIDs that aren't
    in the file (the game's asset registry). The player will load without
    them, which is good enough for Play Now exports."""
    import uuid
    fid = import_id or str(uuid.uuid4())
    name = (data.get("Name") or "").strip()
    parts = name.split(" ", 1)
    first = parts[0] if parts else ""
    last = parts[1] if len(parts) > 1 else ""
    size_str = (data.get("Size") or "Medium").strip().lower()
    size_map = {"extrasmall": 0, "small": 1, "medium": 2, "big": 3,
                "extrabig": 4, "extraextrabig": 5}
    return {
        "checking": int(data.get("Checking") or 50),
        "speed": int(data.get("Speed") or 50),
        "bodySkin": _resolve_fwd_skin(data.get("Body"), "body"),
        "forwardRarity": 1,
        "headSkin": _face_skin_path(data.get("Face") or ""),
        "isBlack": (data.get("Skin Color") or "").lower() == "dark",
        "isLefty": (data.get("Left Handed") or "").lower() == "yes",
        "logoSkin": data.get("Logo Skin") or "Team_Logo/Calaveras",
        "numberSkin": data.get("Number Skin") or "Numbers/Number_88LH",
        "shotAccuracy": int(data.get("Accuracy") or 50),
        "shotPower": int(data.get("Shot Power") or 50),
        "skaterSize": size_map.get(size_str, 2),
        "stickSkin": _resolve_fwd_skin(data.get("Stick"), "stick"),
        "bodyAwaySkin": _resolve_fwd_skin(data.get("Body Away"), "body"),
        "defaultSkaterType": 4,
        "abilityId": "",
        "id": fid,
        "name": "defaultCustomForwardData(Clone)",
        "number": int(data.get("Number") or import_number),
        "talentIds": [],
        "firstName": first,
        "lastName": last,
    }


def _goalie_data_to_custom_goalie(data, import_id=None):
    import uuid
    gid = import_id or str(uuid.uuid4())
    name = (data.get("Name") or "").strip()
    parts = name.split(" ", 1)
    first = parts[0] if parts else ""
    last = parts[1] if len(parts) > 1 else ""
    def stat(k, d=50):
        try: return int(data.get(k) or d)
        except Exception: return d
    # Widget labels and config-file keys use bare names ("Skin", "Helmet Skin",
    # "Glove Skin", etc.) — NOT the old "Goalie " prefix that was here before.
    return {
        "skin":          _resolve_gk_skin(data.get("Skin"), "body"),
        "awaySkin":      _resolve_gk_skin(data.get("Skin Away"), "body"),
        "logoSkin":      data.get("Logo Skin") or "Team_Logo/Calaveras",
        "helmetSkin":    _resolve_gk_skin(data.get("Helmet Skin"), "helmet"),
        # Goalies don't have a Face field — default to the helmet-head face so
        # the goalie isn't headless in Play Now.
        "headSkin":      _face_skin_path(data.get("Face") or "", default="Faces/Custom/Helmet_Face"),
        "blockerSkin":   _resolve_gk_skin(data.get("Blocker Skin"), "blocker"),
        "awayBlockerSkin": _resolve_gk_skin(data.get("Blocker Away"), "blocker"),
        "stickSkin":     _resolve_gk_skin(data.get("Stick Skin"), "stick"),
        "awayStickSkin": _resolve_gk_skin(data.get("Stick Away"), "stick"),
        "gloveSkin":     _resolve_gk_skin(data.get("Glove Skin"), "glove"),
        "awayGloveSkin": _resolve_gk_skin(data.get("Glove Away"), "glove"),
        "padsSkin":      _resolve_gk_skin(data.get("Pads Skin"), "pads"),
        "awayPadsSkin":  _resolve_gk_skin(data.get("Pads Away"), "pads"),
        "abilityId": "",
        "id": gid,
        "number": stat("Number", 30),
        "talentIds": [],
        "firstName": first,
        "lastName": last,
        "skill":          stat("Skill"),
        "catchingSkill":  stat("Catching"),
        "gloveSkill":     stat("Glove"),
        "blockerSkill":   stat("Blocker"),
        "fiveHoleSkill":  stat("Five Hole"),
        "standingSpeed":  stat("Standing Speed"),
        "butterflySpeed": stat("Butterfly Speed"),
        "recoverySkill":  stat("Recovery"),
        "passPower":      stat("Pass Power"),
        "shotPower":      stat("Shot Power"),
        "pokecheckSkill": stat("Poke Check"),
        "depth":          stat("Depth"),
        "controlSkill":   stat("Control"),
    }


def export_player_to_play_now(ed, is_goalie=False, parent=None):
    """Write the current editor's player/goalie as a CustomForward or
    CustomGoalie JSON into the game's TeamDataModels folder so it shows
    up in Play Now → Custom Players."""
    import json
    save_dir = find_game_save_dir()
    if not save_dir:
        messagebox.showwarning("Game save folder not found",
            "Could not locate the Tape to Tape TeamDataModels folder.\n"
            "Expected at: %AppData%\\..\\LocalLow\\Excellent Rectangle\\Tape to Tape\\TeamDataModels\n"
            "Launch the game once and try again.",
            parent=parent)
        return
    data = {k: w.get() for k, w in ed.widgets.items() if hasattr(w, "get")}
    if not (data.get("Name") or "").strip():
        messagebox.showwarning("Need a name",
            "Fill in the player's Name before exporting.",
            parent=parent)
        return
    try:
        if is_goalie:
            obj = _goalie_data_to_custom_goalie(data)
            prefix = "CustomGoalie"
        else:
            obj = _player_data_to_custom_forward(data)
            prefix = "CustomForward"
        out_path = os.path.join(save_dir, f"{prefix}-{obj['id']}.json")
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(obj, f, separators=(",", ":"))
        messagebox.showinfo("Exported",
            f"Exported to Play Now as:\n{os.path.basename(out_path)}\n\n"
            f"Restart the game — the player will appear in Play Now → Custom Players.\n"
            f"Note: talents & abilities are not exported (they require in-game asset IDs).",
            parent=parent)
    except Exception as e:
        messagebox.showerror("Export failed", f"Could not write file:\n{e}", parent=parent)


def _team_data_to_custom_team(ed, player_data_list, goalie_data, team_id=None):
    """Build a CustomTeam JSON referencing the 5 forward IDs + goalie ID.
    player_data_list is a list of 5 (player_data, forward_id) tuples for
    LW/RW/C/LD/RD. Missing slots use empty string."""
    import uuid
    tid = team_id or str(uuid.uuid4())
    data = {k: w.get() for k, w in ed.widgets.items() if hasattr(w, "get")}
    home_primary = _rgb_to_color_dict(data.get("Jersey Primary"))
    home_secondary = _rgb_to_color_dict(data.get("Jersey Secondary"))
    home_accent = _rgb_to_color_dict(data.get("Jersey Accent"))
    away_primary = _rgb_to_color_dict(data.get("Away Primary"))
    away_secondary = _rgb_to_color_dict(data.get("Away Secondary"))
    away_accent = _rgb_to_color_dict(data.get("Away Accent"))
    def get_fwd_id(idx):
        if idx < len(player_data_list) and player_data_list[idx]:
            return player_data_list[idx][1]
        return ""
    return {
        "Id": tid,
        "TeamName": data.get("Team Name") or "Custom Team",
        "City": data.get("City") or "",
        "primaryColorPlayer": home_primary,
        "secondaryColorPlayer": home_secondary,
        "homeColors": {
            "jerseyScheme": {"primaryColor": home_primary, "secondaryColor": home_secondary, "tertiaryColor": home_accent},
            "numberScheme": {"primaryColor": _rgb_to_color_dict(data.get("Number Color Home")),
                             "secondaryColor": home_secondary, "tertiaryColor": home_accent},
            "pantsScheme": {"primaryColor": _rgb_to_color_dict(data.get("Pants Color")),
                            "secondaryColor": _rgb_to_color_dict(data.get("Pants Secondary Color")),
                            "tertiaryColor": _rgb_to_color_dict(data.get("Pants Tertiary Color"))},
            "glovesScheme": {"primaryColor": _rgb_to_color_dict(data.get("Gloves Color")),
                             "secondaryColor": _rgb_to_color_dict(data.get("Gloves Secondary Color")),
                             "tertiaryColor": _rgb_to_color_dict(data.get("Gloves Tertiary Color"))},
            "stickScheme": {"primaryColor": _rgb_to_color_dict(data.get("Stick Color")),
                            "secondaryColor": {"r":0,"g":0,"b":0,"a":0},
                            "tertiaryColor": {"r":0,"g":0,"b":0,"a":0}},
            "helmetScheme": {"primaryColor": _rgb_to_color_dict(data.get("Helmet Color")),
                             "secondaryColor": _rgb_to_color_dict(data.get("Helmet Secondary Color")),
                             "tertiaryColor": _rgb_to_color_dict(data.get("Helmet Tertiary Color"))},
            "socksScheme": {"primaryColor": _rgb_to_color_dict(data.get("Socks Color")),
                            "secondaryColor": _rgb_to_color_dict(data.get("Socks Secondary Color")),
                            "tertiaryColor": _rgb_to_color_dict(data.get("Socks Tertiary Color"))},
            "skatesScheme": {"primaryColor": _rgb_to_color_dict(data.get("Skates Color")),
                             "secondaryColor": _rgb_to_color_dict(data.get("Blade Color")),
                             "tertiaryColor": _rgb_to_color_dict(data.get("Laces Color"))},
        },
        "awayColors": {
            "jerseyScheme": {"primaryColor": away_primary, "secondaryColor": away_secondary, "tertiaryColor": away_accent},
            "numberScheme": {"primaryColor": _rgb_to_color_dict(data.get("Number Color Away")),
                             "secondaryColor": away_secondary, "tertiaryColor": away_accent},
            "pantsScheme": {"primaryColor": _rgb_to_color_dict(data.get("Pants Color")),
                            "secondaryColor": _rgb_to_color_dict(data.get("Pants Secondary Color")),
                            "tertiaryColor": _rgb_to_color_dict(data.get("Pants Tertiary Color"))},
            "glovesScheme": {"primaryColor": _rgb_to_color_dict(data.get("Gloves Color")),
                             "secondaryColor": _rgb_to_color_dict(data.get("Gloves Secondary Color")),
                             "tertiaryColor": _rgb_to_color_dict(data.get("Gloves Tertiary Color"))},
            "stickScheme": {"primaryColor": _rgb_to_color_dict(data.get("Stick Color")),
                            "secondaryColor": {"r":0,"g":0,"b":0,"a":0},
                            "tertiaryColor": {"r":0,"g":0,"b":0,"a":0}},
            "helmetScheme": {"primaryColor": _rgb_to_color_dict(data.get("Helmet Color")),
                             "secondaryColor": _rgb_to_color_dict(data.get("Helmet Secondary Color")),
                             "tertiaryColor": _rgb_to_color_dict(data.get("Helmet Tertiary Color"))},
            "socksScheme": {"primaryColor": _rgb_to_color_dict(data.get("Socks Color")),
                            "secondaryColor": _rgb_to_color_dict(data.get("Socks Secondary Color")),
                            "tertiaryColor": _rgb_to_color_dict(data.get("Socks Tertiary Color"))},
            "skatesScheme": {"primaryColor": _rgb_to_color_dict(data.get("Skates Color")),
                             "secondaryColor": _rgb_to_color_dict(data.get("Blade Color")),
                             "tertiaryColor": _rgb_to_color_dict(data.get("Laces Color"))},
        },
        "primaryColorTransition": _rgb_to_color_dict(data.get("Transition Primary")) if data.get("Transition Primary") else home_primary,
        "secondaryColorTransition": _rgb_to_color_dict(data.get("Transition Secondary")) if data.get("Transition Secondary") else home_secondary,
        "tertiaryColorTransition": _rgb_to_color_dict(data.get("Transition Tertiary")) if data.get("Transition Tertiary") else home_accent,
        "JerseyAwayNumberColor": _rgb_to_color_dict(data.get("Number Color Away")),
        "JerseyHomeNumberColor": _rgb_to_color_dict(data.get("Number Color Home")),
        "nickname": data.get("Abbreviation") or "NEW",
        "vanillaBenchPlayerHead": data.get("Bench Head") or "Faces/Anyteam/Black_Helmet",
        "hasBigLogo": True,
        "hatTrickObjectId": "Calaveras_hattrick_cap",
        "boardTextureId": "",
        "arenaNameTextureId": "",
        "logoId": data.get("Logo From") or "Calaveras",
        "isLocked": False,
        "leftWingerId": get_fwd_id(0),
        "rightWingerId": get_fwd_id(1),
        "centerId": get_fwd_id(2),
        "leftDefensemenId": get_fwd_id(3),
        "rightDefensemenId": get_fwd_id(4),
        "goalieId": goalie_data[1] if goalie_data else "",
        "relics": [],
    }


def export_team_to_play_now(ed, team_dir, parent=None):
    """Export the current team + its 5 starting forwards + goalie as a
    CustomTeam set. Reads player files from <team_dir>/players/."""
    import json, uuid
    save_dir = find_game_save_dir()
    if not save_dir:
        messagebox.showwarning("Game save folder not found",
            "Could not locate the Tape to Tape TeamDataModels folder.\n"
            "Launch the game once and try again.",
            parent=parent)
        return
    if not team_dir or not os.path.isdir(team_dir):
        messagebox.showwarning("Team not saved",
            "Save the team to disk first (it needs a folder with a players/ subfolder).",
            parent=parent)
        return
    players_dir = os.path.join(team_dir, "players")
    if not os.path.isdir(players_dir):
        messagebox.showwarning("No players folder",
            f"Expected: {players_dir}\nCreate 5 player files + 1 goalie before exporting.",
            parent=parent)
        return
    # Walk player files; identify by position prefix.
    pos_slots = {"left wing": 0, "right wing": 1, "center": 2,
                 "left defense": 3, "right defense": 4}
    forwards = [None]*5
    goalie = None
    exported = []
    try:
        for fname in os.listdir(players_dir):
            if not fname.endswith(".txt"): continue
            path = os.path.join(players_dir, fname)
            pdata = read_kv(path)
            pos_raw = (os.path.splitext(fname)[0].split(" - ", 1)[0]
                       if " - " in fname else
                       os.path.splitext(fname)[0]).strip().lower()
            if pos_raw == "goalie":
                gid = str(uuid.uuid4())
                gobj = _goalie_data_to_custom_goalie(pdata, import_id=gid)
                out_path = os.path.join(save_dir, f"CustomGoalie-{gid}.json")
                with open(out_path, "w", encoding="utf-8") as f:
                    json.dump(gobj, f, separators=(",", ":"))
                goalie = (pdata, gid)
                exported.append(os.path.basename(out_path))
            elif pos_raw in pos_slots:
                fid = str(uuid.uuid4())
                fobj = _player_data_to_custom_forward(pdata, import_id=fid)
                out_path = os.path.join(save_dir, f"CustomForward-{fid}.json")
                with open(out_path, "w", encoding="utf-8") as f:
                    json.dump(fobj, f, separators=(",", ":"))
                forwards[pos_slots[pos_raw]] = (pdata, fid)
                exported.append(os.path.basename(out_path))
        team_obj = _team_data_to_custom_team(ed, forwards, goalie)
        tid = team_obj["Id"]
        team_path = os.path.join(save_dir, f"CustomTeam-{tid}.json")
        with open(team_path, "w", encoding="utf-8") as f:
            json.dump(team_obj, f, separators=(",", ":"))
        exported.append(os.path.basename(team_path))
        messagebox.showinfo("Exported",
            f"Exported {len(exported)} files to Play Now:\n\n" + "\n".join(exported[-5:]) +
            (f"\n(+{len(exported)-5} more)" if len(exported) > 5 else "") +
            "\n\nRestart the game — the team will appear in Play Now → Custom Teams.\n"
            "Talents & abilities are not exported (they need in-game GUIDs).",
            parent=parent)
    except Exception as e:
        messagebox.showerror("Export failed", f"{e}", parent=parent)


def _ask_library_save(is_goalie=False, parent=None):
    """Pops a small dialog to save a player to the library.
       Returns the full file path, or None if cancelled.
       Creates library/players/<Name>.txt."""
    dlg = tk.Toplevel(parent)
    dlg.title("Save to Library")
    _fit_geometry(dlg, 460, 220)
    if parent:
        dlg.transient(parent)
    dlg.grab_set()

    ttk.Label(dlg, text="Save player to the library",
              font=("", 11, "bold")).pack(padx=10, pady=(12, 2))
    ttk.Label(dlg,
        text=("Library players can be imported into any team.\n"
              "Saved to library/players/<Name>.txt"),
        foreground="#555", justify="left"
    ).pack(padx=10, pady=(0, 8))

    form = ttk.Frame(dlg)
    form.pack(padx=10, pady=4, fill="x")

    ttk.Label(form, text="Name:", width=10, anchor="w").grid(row=0, column=0, pady=4, sticky="w")
    name_var = tk.StringVar()
    name_entry = ttk.Entry(form, textvariable=name_var, width=30)
    name_entry.grid(row=0, column=1, pady=4)
    name_entry.focus_set()

    result = {"path": None}

    def ok():
        name = name_var.get().strip()
        if not name:
            messagebox.showwarning("Missing", "Enter a name.")
            return
        safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
        os.makedirs(PLAYER_LIBRARY_DIR, exist_ok=True)
        path = os.path.join(PLAYER_LIBRARY_DIR, safe + ".txt")
        if os.path.exists(path):
            if not messagebox.askyesno("Overwrite",
                f"'{safe}.txt' already exists in the library. Overwrite?"):
                return
        result["path"] = path
        dlg.destroy()

    btns = ttk.Frame(dlg)
    btns.pack(fill="x", pady=10)
    ttk.Button(btns, text="Save", command=ok, width=14).pack(side="right", padx=8)
    ttk.Button(btns, text="Cancel", command=dlg.destroy, width=14).pack(side="right")

    dlg.bind("<Return>", lambda e: ok())
    dlg.wait_window()
    return result["path"]


def _ask_target_position(src_file, parent=None):
    """Ask which position slot to assign a player to when importing into a team.
       Pre-fills from the source file's 'Preferred Position' header if present.
       Returns the position string (becomes the filename), or None if cancelled."""
    positions = [
        "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense",
        "Goalie",
        "Line 2 Left Wing", "Line 2 Right Wing", "Line 2 Center",
        "Line 2 Left Defense", "Line 2 Right Defense",
    ]
    preferred = get_preferred_position(src_file)

    dlg = tk.Toplevel(parent)
    dlg.title("Assign to slot")
    _fit_geometry(dlg, 500, 220)
    if parent:
        dlg.transient(parent)
    dlg.grab_set()

    ttk.Label(dlg, text="Which slot should this player fill?",
              font=("", 11, "bold")).pack(padx=10, pady=(12, 2))
    ttk.Label(dlg,
        text=("The slot is the player's ROLE on the team — Left Wing, Goalie, etc.\n"
              "After picking, the player editor opens with this player loaded.\n"
              "Review/tweak anything you want, then click Save to add them to the team.\n"
              "(Nothing is written to disk until you click Save.)"),
        foreground="#555", justify="left"
    ).pack(padx=10, pady=(0, 8))

    form = ttk.Frame(dlg)
    form.pack(padx=10, pady=4, fill="x")
    ttk.Label(form, text="Slot:", width=10, anchor="w").grid(row=0, column=0, pady=4, sticky="w")
    pos_var = tk.StringVar()
    pos_combo = ttk.Combobox(form, textvariable=pos_var, values=positions,
                              width=30, state="readonly")
    pos_combo.grid(row=0, column=1, pady=4, sticky="w")
    # Default to preferred position if valid
    default_pos = preferred if preferred in positions else positions[0]
    pos_var.set(default_pos)

    if preferred:
        ttk.Label(dlg, text=f"(Player's preferred position: {preferred})",
                  foreground="#777", font=("", 8)).pack(padx=10)

    result = {"pos": None}

    def ok():
        pos = pos_var.get().strip()
        if not pos: return
        result["pos"] = pos
        dlg.destroy()

    btns = ttk.Frame(dlg)
    btns.pack(fill="x", pady=10)
    ttk.Button(btns, text="Assign", command=ok, width=14).pack(side="right", padx=8)
    ttk.Button(btns, text="Cancel", command=dlg.destroy, width=14).pack(side="right")

    dlg.bind("<Return>", lambda e: ok())
    dlg.wait_window()
    return result["pos"]


def _ask_team_library_save(parent=None):
    """Pops a dialog to save a team to the library.
       Returns the full folder path for the team, or None if cancelled.
       Creates library/teams/<Name>/."""
    dlg = tk.Toplevel(parent)
    dlg.title("Save Team to Library")
    _fit_geometry(dlg, 460, 220)
    if parent:
        dlg.transient(parent)
    dlg.grab_set()

    ttk.Label(dlg, text="Save team to the library",
              font=("", 11, "bold")).pack(padx=10, pady=(12, 2))
    ttk.Label(dlg,
        text=("Library teams can be imported into any campaign.\n"
              "Saved to library/teams/<Name>/ (folder with team.txt + players/)"),
        foreground="#555", justify="left"
    ).pack(padx=10, pady=(0, 8))

    form = ttk.Frame(dlg)
    form.pack(padx=10, pady=4, fill="x")

    ttk.Label(form, text="Team name:", width=10, anchor="w").grid(row=0, column=0, pady=4, sticky="w")
    name_var = tk.StringVar()
    name_entry = ttk.Entry(form, textvariable=name_var, width=30)
    name_entry.grid(row=0, column=1, pady=4)
    name_entry.focus_set()

    result = {"path": None}

    def ok():
        name = name_var.get().strip()
        if not name:
            messagebox.showwarning("Missing", "Enter a team name.")
            return
        safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
        path = os.path.join(TEAM_LIBRARY_DIR, safe)
        if os.path.exists(path):
            if not messagebox.askyesno("Overwrite",
                f"'{safe}' already exists in the team library. Overwrite?"):
                return
        os.makedirs(os.path.join(path, "players"), exist_ok=True)
        result["path"] = path
        dlg.destroy()

    btns = ttk.Frame(dlg)
    btns.pack(fill="x", pady=10)
    ttk.Button(btns, text="Save", command=ok, width=14).pack(side="right", padx=8)
    ttk.Button(btns, text="Cancel", command=dlg.destroy, width=14).pack(side="right")

    dlg.bind("<Return>", lambda e: ok())
    dlg.wait_window()
    return result["path"]


def open_player_editor(path=None, is_goalie=None, on_save=None,
                        preload_from=None, position_hint=None,
                        team_colors=None):
    """Open the player editor window.

    path:           If set, save target. File is NOT created until Save is clicked.
    is_goalie:      Force goalie mode. Auto-detected from path filename otherwise.
    preload_from:   Path to a source player file whose data should be preloaded
                    (e.g. for importing — user can review before saving).
    position_hint:  Pre-selects the Position dropdown (used when importing into a slot).
    """
    if is_goalie is None:
        is_goalie = bool(path and "Goalie" in os.path.basename(path))

    # Detect draft-pool context from path — file lives under player_teams/draft_pool/.
    # Draft-pool entries CANNOT be renamed at runtime (the DLL skips the name
    # assignment for free agents so name changes don't break customization),
    # so we lock the Name field to read-only here to match.
    is_draft_pool = False
    if path:
        norm = os.path.normpath(path)
        is_draft_pool = (os.sep + "player_teams" + os.sep + "draft_pool" + os.sep) in norm

    kind = "Goalie" if is_goalie else "Skater"
    title = f"{kind} Editor"
    if is_draft_pool:
        title += " (Free Agent — name locked)"
    if preload_from:
        title += f" — Importing {os.path.basename(preload_from)[:-4] if preload_from.endswith('.txt') else os.path.basename(preload_from)}"
    elif path:
        title += f" — {os.path.basename(path)}"

    host = _EditorHost(title, size="950x780")
    win = host.container  # parent for all children; could be Toplevel or Frame
    tracker = host.attach_tracker(title)

    ed = PlayerEditor(win, is_goalie=is_goalie, team_colors=team_colors, is_draft_pool=is_draft_pool)
    ed.pack(fill="both", expand=True)

    # Banner explaining what mode this is
    banner_text = None
    if preload_from:
        slot_name = position_hint or (path and os.path.basename(path)[:-4]) or "this slot"
        banner_text = (
            f"  IMPORTING into {slot_name}.\n"
            f"  Loaded from {os.path.basename(preload_from)[:-4]}. Review, then click Save.\n"
            f"  On Save: written to the All Players library AND copied into the team's {slot_name} slot."
        )
    elif path and is_base_game_path(path):
        banner_text = (
            "  READ-ONLY BASE GAME PLAYER.\n"
            "  Saving here will NOT modify the base game file.\n"
            "  Your changes will be saved as a custom copy in your library."
        )
    elif path:
        slot_name = position_hint or parse_position_from_filename(os.path.basename(path))
        banner_text = (
            f"  TEAM PLAYER ({slot_name}).\n"
            f"  On Save: written to the All Players library AND this team's {slot_name} slot.\n"
            f"  (Edits to this player propagate to the library automatically.)"
        )
    else:
        banner_text = (
            "  NEW PLAYER. Fill in fields (or set Import Player to clone a game player).\n"
            "  On Save: written to the All Players library. Import it into any team from the Team editor."
        )
    if banner_text:
        # Note: ttk.Label doesn't support padx/pady kwargs on Py 3.14+ — use tk.Label here
        # so the warm-yellow banner can have internal padding.
        tk.Label(win, text=banner_text, background="#fff3c4",
                 foreground="#553300", font=("", 9, "bold"),
                 justify="left", anchor="w", padx=8, pady=4).pack(fill="x")

    # Load preload data (overrides path-based load)
    if preload_from and os.path.isfile(preload_from):
        ed.load_file(preload_from)
    elif path and os.path.isfile(path):
        ed.load_file(path)

    if position_hint and hasattr(ed, "position_var"):
        ed.position_var.set(position_hint)

    btns = ttk.Frame(win)
    btns.pack(fill="x", pady=5)

    def save():
        # Get player name (required — it's the library filename)
        name = ed.widgets.get("Name").get().strip() if "Name" in ed.widgets else ""
        if not name:
            imp = ed.widgets.get("Import Player").get().strip() if "Import Player" in ed.widgets else ""
            if imp and imp.lower() != "random":
                name = imp
        if not name:
            messagebox.showwarning("Need a name",
                "Either fill in a Name OR set Import Player to a game player's name.\n"
                "(The Name is used as the library filename and the team slot.)")
            return
        safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()

        # Build team_path if we're in team context.
        # Team context = path is inside a folder literally named "players"
        # but NOT the library's own players folder.
        # Draft pool / player team context = path is inside player_teams/.../.
        team_path = None
        if path:
            parent_dir = os.path.basename(os.path.dirname(path))
            is_team_ctx = (parent_dir == "players"
                           and os.path.normpath(os.path.dirname(path)) != os.path.normpath(PLAYER_LIBRARY_DIR)
                           and not is_base_game_path(path))
            # Draft pool: path is campaign/player_teams/draft_pool/<name>.txt
            # Player team: path is campaign/player_teams/<Team>/players/<Position>.txt
            # In both cases, write back to the same path the user loaded from.
            norm_path = os.path.normpath(path)
            is_draft_ctx = (os.sep + "player_teams" + os.sep + "draft_pool" + os.sep) in norm_path
            is_player_team_ctx = ((os.sep + "player_teams" + os.sep) in norm_path
                                   and parent_dir == "players" and not is_draft_ctx)

            if is_draft_ctx:
                # Overwrite the draft pool file — rename if user changed the Name field
                draft_dir = os.path.dirname(path)
                team_path = os.path.join(draft_dir, safe + ".txt")
                # If the name changed, delete the old file so we don't end up with duplicates
                if os.path.normpath(team_path) != norm_path and os.path.isfile(path):
                    try: os.remove(path)
                    except Exception: pass
            elif is_player_team_ctx:
                # Preserve position prefix in filename (Left Wing - <name>.txt)
                pos = position_hint or parse_position_from_filename(os.path.basename(path))
                team_players_dir = os.path.dirname(path)
                if pos and pos in VALID_POSITIONS:
                    team_path = os.path.join(team_players_dir, f"{pos} - {safe}.txt")
                    team_dir = os.path.dirname(team_players_dir)
                    old = _existing_slot_path(team_dir, pos)
                    if old and os.path.normpath(old) != os.path.normpath(team_path):
                        try: os.remove(old)
                        except Exception: pass
                else:
                    team_path = os.path.join(team_players_dir, safe + ".txt")
            elif is_team_ctx:
                pos = position_hint or parse_position_from_filename(os.path.basename(path))
                if pos and pos in VALID_POSITIONS:
                    team_players_dir = os.path.dirname(path)
                    team_path = os.path.join(team_players_dir, f"{pos} - {safe}.txt")
                    team_dir = os.path.dirname(team_players_dir)
                    old = _existing_slot_path(team_dir, pos)
                    if old and os.path.normpath(old) != os.path.normpath(team_path):
                        try: os.remove(old)
                        except Exception: pass

        # Confirm overwrite if library already has someone by this name
        lib_path = os.path.join(PLAYER_LIBRARY_DIR, safe + ".txt")
        if os.path.exists(lib_path) and os.path.normpath(lib_path) != os.path.normpath(ed.loaded_path or ""):
            if not messagebox.askyesno("Overwrite library",
                f"The All Players library already has '{safe}.txt'.\n"
                f"Overwrite? (The existing library copy will be replaced with these settings.)"):
                return

        try:
            written_lib, written_team = ed.save_file(team_path=team_path)
        except ValueError as e:
            messagebox.showwarning("Can't save", str(e))
            return
        except Exception as e:
            messagebox.showerror("Save failed", f"{type(e).__name__}: {e}")
            return

        msg = f"Saved to library:\n{written_lib}"
        if written_team:
            msg += f"\n\nAlso copied into team slot:\n{written_team}"
        messagebox.showinfo("Saved", msg)
        tracker.mark_clean()
        if on_save: on_save()
        host.destroy()
        return True

    def reveal():
        # Show the current file on disk. If unsaved, show the library folder instead.
        target = ed.loaded_path if ed.loaded_path and os.path.exists(ed.loaded_path) else PLAYER_LIBRARY_DIR
        open_in_file_explorer(target)

    ttk.Button(btns, text="Save", command=save, width=14).pack(side="right", padx=5)
    ttk.Button(btns, text="Show on Disk", command=reveal, width=14).pack(side="right", padx=5)
    ttk.Button(btns, text="Export to Play Now",
               command=lambda: export_player_to_play_now(ed, is_goalie=ed.is_goalie, parent=host),
               width=18).pack(side="right", padx=5)
    ttk.Button(btns, text="Cancel", command=host.destroy, width=14).pack(side="right")

    tracker.save_fn = save
    host.finalize_tracking(tracker)


def open_team_editor(team_dir=None, on_save=None):
    """Open team editor — team fields + player list with edit buttons."""
    title = "Team Editor" + (f" — {os.path.basename(team_dir)}" if team_dir else "")
    host = _EditorHost(title, size="1300x900")
    win = host.container

    # Split pane: team fields left (wider — form has many fields), roster right.
    # Weights control how extra space is allocated when the window is resized;
    # initial sash position is set after the window lays out so left gets ~65%.
    paned = ttk.PanedWindow(win, orient="horizontal")
    paned.pack(fill="both", expand=True)

    tracker = host.attach_tracker(title)

    # Player-selectable squads live under .../player_teams/. Squad Head and
    # Description fields only apply to those, so hide them for regular teams.
    is_player_team = False
    if team_dir:
        norm_td = os.path.normpath(team_dir)
        is_player_team = (os.sep + "player_teams" + os.sep) in (norm_td + os.sep)

    left = ttk.Frame(paned)
    ed = TeamEditor(left, is_player_team=is_player_team)
    ed.pack(fill="both", expand=True)
    paned.add(left, weight=3)

    right_outer = ttk.Frame(paned)
    paned.add(right_outer, weight=2)

    # Place the sash so the team-customisation form gets the larger slice.
    def _set_sash():
        try:
            total = paned.winfo_width()
            if total > 100:
                paned.sashpos(0, int(total * 0.62))
        except Exception: pass
    win.after(100, _set_sash)
    # Scrollable right panel so Line 2 doesn't overflow
    right_canvas = tk.Canvas(right_outer, highlightthickness=0)
    right_scroll = ttk.Scrollbar(right_outer, orient="vertical", command=right_canvas.yview)
    right = ttk.Frame(right_canvas)
    right.bind("<Configure>",
        lambda e: right_canvas.configure(scrollregion=right_canvas.bbox("all")))
    right_canvas.create_window((0, 0), window=right, anchor="nw")
    right_canvas.configure(yscrollcommand=right_scroll.set)
    right_canvas.pack(side="left", fill="both", expand=True)
    right_scroll.pack(side="right", fill="y")
    def _on_right_wheel(e): right_canvas.yview_scroll(int(-1*(e.delta/120)), "units")
    right_canvas.bind("<Enter>", lambda e: right_canvas.bind_all("<MouseWheel>", _on_right_wheel))
    right_canvas.bind("<Leave>", lambda e: right_canvas.unbind_all("<MouseWheel>"))

    def _get_team_colors():
        """Read current team color values from the team editor for player preview."""
        colors = {}
        for k, w in ed.widgets.items():
            try:
                v = w.get()
                if v: colors[k] = v
            except Exception: pass
        return colors

    ttk.Label(right, text="Roster", font=("", 11, "bold")).pack(anchor="w", pady=(4, 2))
    ttk.Label(right, text="Click a position to add/edit. Right-click for more options.",
              foreground="#777", font=("", 8)).pack(anchor="w", padx=4)

    # Team relics (read straight from team.txt's --- Team Relics --- section).
    # Shown here so the user can see at a glance what the team starts with
    # without scrolling through the form.
    relics_box = ttk.LabelFrame(right, text=" Team Relics ")
    relics_box.pack(fill="x", padx=4, pady=(4, 4))
    relics_lbl = ttk.Label(relics_box, text="(none)", font=("", 8),
                            foreground="#555", wraplength=260, justify="left")
    relics_lbl.pack(anchor="w", padx=6, pady=4)

    def _read_team_relics():
        if not team_dir: return []
        tp = os.path.join(team_dir, "team.txt")
        if not os.path.isfile(tp): return []
        try:
            with open(tp, encoding="utf-8") as f: raw = f.readlines()
        except Exception: return []
        out, in_section = [], False
        for ln in raw:
            s = ln.strip()
            if s.startswith("---"):
                header = s.replace("-", "").strip().lower()
                in_section = header.startswith("team relics") or header.startswith("relics")
                continue
            if in_section and s and not s.startswith("#") and "=" not in s:
                out.append(s)
        return out

    def _compute_overall(pdata, is_goalie):
        """Rough overall: skater = avg(Speed, Shot Power, Accuracy, Checking).
        Goalie = avg of the 8 main goalie stats."""
        def _n(key):
            v = (pdata.get(key) or "").strip()
            # Handle 'random(40, 90)' — use midpoint
            if v.lower().startswith("random"):
                try:
                    inside = v[v.index("(")+1:v.index(")")]
                    lo, hi = [float(x.strip()) for x in inside.split(",")[:2]]
                    return (lo + hi) / 2
                except Exception: return 0
            try: return float(v)
            except Exception: return 0
        if is_goalie:
            keys = ["Catching", "Glove", "Blocker", "Five Hole",
                    "Standing Speed", "Butterfly Speed", "Control", "Recovery"]
        else:
            keys = ["Speed", "Shot Power", "Accuracy", "Checking"]
        vals = [_n(k) for k in keys]
        vals = [v for v in vals if v > 0]
        if not vals: return 0
        return int(round(sum(vals) / len(vals)))

    # Visual lineup — 2-row rink formation. Goalie sits between the D pair
    # (mirrors defensive-zone positioning) so no cell is empty.
    LINEUP_POSITIONS = [
        # (row, col, position_name, is_goalie, colspan)
        (0, 0, "Left Wing", False, 1),
        (0, 1, "Center", False, 1),
        (0, 2, "Right Wing", False, 1),
        (1, 0, "Left Defense", False, 1),
        (1, 1, "Goalie", True, 1),
        (1, 2, "Right Defense", False, 1),
    ]
    # Jersey preview scale for the roster tiles. Canvas dims scale with this.
    # 1.4× is a compromise — tiles are visibly bigger than the original 1.0×
    # but still fit inside the narrower right panel now that the team form
    # takes ~62% of the editor width.
    _JERSEY_SCALE = 1.4
    lineup_frame = ttk.Frame(right)
    lineup_frame.pack(fill="x", padx=4, pady=6)
    _slot_labels = {}  # position → (name_label, btn, mini_canvas, remove_btn)

    def _get_player_at(pos):
        """Return (filename, display_name) for a position, or (None, None)."""
        if not team_dir: return None, None
        existing = _existing_slot_path(team_dir, pos)
        if not existing: return None, None
        fname = os.path.basename(existing)
        name = ""
        try:
            pdata = read_kv(existing)
            name = (pdata.get("Name") or pdata.get("Import Player") or "").strip()
        except Exception: pass
        if not name and " - " in fname[:-4]:
            name = fname[:-4].split(" - ", 1)[1].strip()
        return fname, name or "(unnamed)"

    def _draw_one_jersey(canvas, colors, x_off, y_off, is_away=False, scale=1.0):
        """Draw one jersey at (x_off, y_off). Shows all color channels.
        scale>1 enlarges the drawing proportionally so roster tiles fill their
        allocated cell instead of leaving most of the column empty."""
        def pick(*labels, default="#808080"):
            for lbl in labels:
                v = colors.get(lbl, "")
                c = _parse_rgb(v)
                if c: return c
            return default
        if is_away:
            body = pick("Away Primary", "Jersey Color")
            body2 = pick("Away Secondary", "Jersey Secondary Color")
            body3 = pick("Away Accent", "Jersey Accent Color")
            num = pick("Number Color Away", "Number Color")
        else:
            body = pick("Jersey Primary", "Jersey Color")
            body2 = pick("Jersey Secondary", "Jersey Secondary Color")
            body3 = pick("Jersey Accent", "Jersey Accent Color")
            num = pick("Number Color Home", "Number Color")
        helm = pick("Helmet Color")
        helm2 = pick("Helmet Secondary Color")
        gloves = pick("Gloves Color")
        pants = pick("Pants Color")
        pants2 = pick("Pants Secondary Color")
        socks = pick("Socks Color")
        socks2 = pick("Socks Secondary Color")
        skates = pick("Skates Color")
        bicep = pick("Bicep Color")
        s = float(scale)
        def X(n): return x_off + n * s
        def Y(n): return y_off + n * s
        W = max(1, int(s))
        num_font = ("", max(6, int(6 * s)), "bold")
        # Helmet (primary + secondary stripe)
        canvas.create_oval(X(8), Y(0), X(22), Y(8), fill=helm, outline="#333")
        canvas.create_line(X(10), Y(4), X(20), Y(4), fill=helm2, width=W)
        # Jersey body
        canvas.create_rectangle(X(2), Y(10), X(28), Y(28), fill=body, outline="#333")
        # Bicep accents
        canvas.create_rectangle(X(2), Y(10), X(5), Y(16), fill=bicep, outline="")
        canvas.create_rectangle(X(25), Y(10), X(28), Y(16), fill=bicep, outline="")
        # Jersey stripe + accent
        canvas.create_rectangle(X(2), Y(24), X(28), Y(26), fill=body2, outline="")
        canvas.create_rectangle(X(2), Y(10), X(28), Y(11), fill=body3, outline="")
        # Number
        canvas.create_text(X(15), Y(18), text="8", fill=num, font=num_font)
        # Gloves
        canvas.create_rectangle(X(0), Y(20), X(3), Y(25), fill=gloves, outline="#333")
        canvas.create_rectangle(X(27), Y(20), X(30), Y(25), fill=gloves, outline="#333")
        # Pants (primary + secondary stripe)
        canvas.create_rectangle(X(5), Y(30), X(25), Y(36), fill=pants, outline="#333")
        canvas.create_rectangle(X(5), Y(34), X(25), Y(35), fill=pants2, outline="")
        # Socks
        canvas.create_rectangle(X(7), Y(37), X(13), Y(40), fill=socks, outline="#333")
        canvas.create_rectangle(X(17), Y(37), X(23), Y(40), fill=socks, outline="#333")
        canvas.create_line(X(7), Y(39), X(13), Y(39), fill=socks2, width=W)
        canvas.create_line(X(17), Y(39), X(23), Y(39), fill=socks2, width=W)
        # Skates
        canvas.create_rectangle(X(5), Y(40), X(15), Y(43), fill=skates, outline="#333")
        canvas.create_rectangle(X(15), Y(40), X(25), Y(43), fill=skates, outline="#333")

    def _draw_mini_jersey(canvas, colors):
        """Draw home + away jerseys stacked vertically — scaled up per
        _JERSEY_SCALE so the roster tiles actually use the column width."""
        canvas.delete("all")
        s = _JERSEY_SCALE
        # Top-of-jersey labels scale with the drawing
        lbl_font = ("", max(5, int(6 * s)))
        canvas.create_text(15 * s, 4 * s, text="H", fill="#666", font=lbl_font)
        _draw_one_jersey(canvas, colors, 0, 8 * s, is_away=False, scale=s)
        canvas.create_text(15 * s, 50 * s, text="A", fill="#666", font=lbl_font)
        _draw_one_jersey(canvas, colors, 0, 54 * s, is_away=True, scale=s)

    def refresh_players():
        team_cols = _get_team_colors()
        # Update team relics list
        try:
            relics = _read_team_relics()
            relics_lbl.configure(text=(", ".join(relics) if relics else "(none)"),
                                  foreground=("#000" if relics else "#888"))
        except Exception: pass
        for pos, entry in _slot_labels.items():
            name_lbl, slot_btn, mini_cv, remove_btn, info_lbl = entry
            fname, name = _get_player_at(pos)
            try:
                remove_btn.configure(state=("normal" if fname else "disabled"))
            except Exception: pass
            if fname:
                name_lbl.configure(text=name, foreground="#000")
                # Read player colors + stats + ability/talents for preview + info
                player_path = os.path.join(team_dir, "players", fname)
                merged = dict(team_cols)
                info_parts = []
                try:
                    pdata = read_kv(player_path)
                    is_goalie_slot = (pos == "Goalie")
                    ovr = _compute_overall(pdata, is_goalie_slot)
                    if ovr: info_parts.append(f"OVR {ovr}")
                    ability = (pdata.get("Ability") or "").strip()
                    if ability and ability.lower() != "none":
                        info_parts.append(f"Ab: {ability}")
                    talents = (pdata.get("Talents") or "").strip()
                    if talents and talents.lower() != "none":
                        # Truncate long talent lists so the tile doesn't balloon
                        tshort = talents if len(talents) < 60 else talents[:57] + "..."
                        info_parts.append(f"Tal: {tshort}")
                    # Map player color fields to team-style field names
                    player_to_team = {
                        "Jersey Color": "Jersey Primary",
                        "Jersey Secondary Color": "Jersey Secondary",
                        "Jersey Accent Color": "Jersey Accent",
                    }
                    for pk, pv in pdata.items():
                        if pv:
                            tk_key = player_to_team.get(pk, pk)
                            merged[tk_key] = pv
                            merged[pk] = pv
                except Exception: pass
                _draw_mini_jersey(mini_cv, merged)
                try: info_lbl.configure(text="  |  ".join(info_parts) or "")
                except Exception: pass
            else:
                name_lbl.configure(text="Empty", foreground="#999")
                _draw_mini_jersey(mini_cv, team_cols)
                try: info_lbl.configure(text="")
                except Exception: pass

    def _slot_click(pos, is_goalie):
        if not team_dir:
            messagebox.showwarning("Save First", "Save the team first.")
            return
        fname, _ = _get_player_at(pos)
        if fname:
            # Player exists — open for editing with team colors for preview
            open_player_editor(os.path.join(team_dir, "players", fname),
                               on_save=refresh_players,
                               team_colors=_get_team_colors())
        else:
            # Empty slot — show options
            _slot_add_menu(pos, is_goalie)

    def _slot_right_click(event, pos, is_goalie):
        menu = tk.Menu(right, tearoff=0)
        fname, name = _get_player_at(pos)
        if fname:
            menu.add_command(label=f"Edit {name}",
                command=lambda: open_player_editor(
                    os.path.join(team_dir, "players", fname),
                    on_save=refresh_players,
                    team_colors=_get_team_colors()))
            menu.add_command(label=f"Duplicate {name}",
                command=lambda: _duplicate_at_slot(fname))
            menu.add_command(label="Replace — New Player",
                command=lambda: _new_at_slot(pos, is_goalie))
            menu.add_command(label="Replace — Import Player",
                command=lambda: _import_at_slot(pos, is_goalie))
            menu.add_separator()
            menu.add_command(label="Remove",
                command=lambda: _remove_at_slot(pos, fname))
        else:
            menu.add_command(label="New Player",
                command=lambda: _new_at_slot(pos, is_goalie))
            menu.add_command(label="Import Player",
                command=lambda: _import_at_slot(pos, is_goalie))
        try: menu.tk_popup(event.x_root, event.y_root)
        finally: menu.grab_release()

    def _slot_add_menu(pos, is_goalie):
        """Show add options for an empty slot."""
        result = _ask_choice("Add Player", f"How to fill {pos}?",
            ["New Player (blank)", "Import from library/campaign"], parent=win)
        if not result: return
        if result.startswith("New"):
            _new_at_slot(pos, is_goalie)
        else:
            _import_at_slot(pos, is_goalie)

    def _new_at_slot(pos, is_goalie):
        existing = _existing_slot_path(team_dir, pos)
        if existing:
            try: os.remove(existing)
            except Exception: pass
        path = os.path.join(team_dir, "players", pos + ".txt")
        open_player_editor(path, is_goalie=is_goalie, on_save=refresh_players,
                            position_hint=pos, team_colors=_get_team_colors())

    def _import_at_slot(pos, is_goalie):
        def on_pick(src_camp, src_team, src_file):
            if src_camp == LIBRARY_SOURCE:
                src = os.path.join(PLAYER_LIBRARY_DIR, src_file)
            else:
                src = os.path.join(CAMPAIGNS_DIR, src_camp, "teams", src_team, "players", src_file)
            try:
                src_data = read_kv(src)
                player_name = (src_data.get("Name") or src_data.get("Import Player") or "Player").strip()
            except Exception:
                player_name = "Player"
            safe_name = re.sub(r'[<>:"/\\|?*]', '_', player_name).strip() or "Player"
            dst_name = f"{pos} - {safe_name}.txt"
            existing = _existing_slot_path(team_dir, pos)
            if existing:
                try: os.remove(existing)
                except Exception: pass
            dst = os.path.join(team_dir, "players", dst_name)
            open_player_editor(dst, is_goalie=is_goalie, on_save=refresh_players,
                                preload_from=src, position_hint=pos,
                                team_colors=_get_team_colors())
        open_player_browser(on_pick, button_label="Import")

    def _duplicate_at_slot(fname):
        """Duplicate a player file into the library with an auto-incremented name."""
        src = os.path.join(team_dir, "players", fname)
        try:
            data = read_kv(src)
            name = (data.get("Name") or data.get("Import Player") or "Player").strip()
            new_name = deduplicate_name(name, PLAYER_LIBRARY_DIR)
            data["Name"] = new_name
            os.makedirs(PLAYER_LIBRARY_DIR, exist_ok=True)
            order = GOALIE_FIELD_ORDER if "Goalie" in fname else PLAYER_FIELD_ORDER
            dst = os.path.join(PLAYER_LIBRARY_DIR, new_name + ".txt")
            write_kv(dst, data, order=order)
            messagebox.showinfo("Duplicated", f"'{new_name}' saved to library.\n{dst}")
        except Exception as e:
            messagebox.showerror("Duplicate failed", f"{type(e).__name__}: {e}")

    def _remove_at_slot(pos, fname):
        if messagebox.askyesno("Remove", f"Remove {pos} player?"):
            try: os.remove(os.path.join(team_dir, "players", fname))
            except Exception: pass
            refresh_players()

    def _remove_click(p):
        fname, _ = _get_player_at(p)
        if fname:
            _remove_at_slot(p, fname)

    # Build the formation grid with mini jersey previews.
    # Right-side positions (RW, RD) align their preview to the right edge of
    # their cell so the formation reads like a real rink layout: LW/LD stay
    # left, C/Goalie centered, RW/RD flush right.
    def _slot_anchors(col):
        # (cell_sticky, pack_anchor) for this grid column
        if col == 0: return "nsew", "w"
        if col == 2: return "nsew", "e"
        return "nsew", "center"

    for row, col, pos, is_g, colspan in LINEUP_POSITIONS:
        cell_sticky, pack_anchor = _slot_anchors(col)
        cell = ttk.Frame(lineup_frame)
        cell.grid(row=row, column=col, padx=4, pady=4, sticky=cell_sticky, columnspan=colspan)
        lineup_frame.columnconfigure(col, weight=1)

        pos_lbl = ttk.Label(cell, text=pos, font=("", 8, "bold"),
                             foreground="#0066aa")
        pos_lbl.pack(anchor=pack_anchor)

        # Mini jersey preview canvas
        mini_cv = tk.Canvas(cell, width=int(32 * _JERSEY_SCALE), height=int(94 * _JERSEY_SCALE),
                             bg="#f0f0f0", highlightthickness=0)
        mini_cv.pack(anchor=pack_anchor, pady=2)

        name_lbl = ttk.Label(cell, text="Empty", foreground="#999", font=("", 9))
        name_lbl.pack(anchor=pack_anchor)

        btn_row = ttk.Frame(cell)
        btn_row.pack(anchor=pack_anchor, fill="x", padx=2, pady=2)
        slot_btn = ttk.Button(btn_row, text="Click to edit",
                               command=lambda p=pos, g=is_g: _slot_click(p, g))
        slot_btn.pack(side="left", fill="x", expand=True)
        slot_btn.bind("<Button-3>",
                       lambda e, p=pos, g=is_g: _slot_right_click(e, p, g))
        remove_btn = ttk.Button(btn_row, text="x", width=2,
                                 command=lambda p=pos: _remove_click(p))
        remove_btn.pack(side="left", padx=(2, 0))

        info_lbl = ttk.Label(cell, text="", font=("", 7), foreground="#555",
                              wraplength=int(32 * _JERSEY_SCALE * 4),
                              justify="left")
        info_lbl.pack(anchor=pack_anchor, padx=2, pady=(0, 2))

        _slot_labels[pos] = (name_lbl, slot_btn, mini_cv, remove_btn, info_lbl)

    # Separator + Line 2 (optional, collapsible)
    ttk.Separator(right, orient="horizontal").pack(fill="x", padx=4, pady=6)
    line2_var = tk.BooleanVar(value=False)
    ttk.Checkbutton(right, text="Show Line 2 (bench players)",
                     variable=line2_var,
                     command=lambda: _toggle_line2()).pack(anchor="w", padx=4)
    line2_frame = ttk.Frame(right)
    LINE2_POSITIONS = [
        (0, 0, "Line 2 Left Wing", False, 1),
        (0, 1, "Line 2 Center", False, 1),
        (0, 2, "Line 2 Right Wing", False, 1),
        (1, 0, "Line 2 Left Defense", False, 1),
        (1, 2, "Line 2 Right Defense", False, 1),
    ]
    for row, col, pos, is_g, colspan in LINE2_POSITIONS:
        cell_sticky, pack_anchor = _slot_anchors(col)
        cell = ttk.Frame(line2_frame)
        cell.grid(row=row, column=col, padx=4, pady=3, sticky=cell_sticky, columnspan=colspan)
        line2_frame.columnconfigure(col, weight=1)
        short = pos.replace("Line 2 ", "L2 ")
        ttk.Label(cell, text=short, font=("", 7, "bold"), foreground="#666").pack(anchor=pack_anchor)
        mini_cv = tk.Canvas(cell, width=int(32 * _JERSEY_SCALE), height=int(94 * _JERSEY_SCALE),
                             bg="#f0f0f0", highlightthickness=0)
        mini_cv.pack(anchor=pack_anchor, pady=1)
        name_lbl = ttk.Label(cell, text="Empty", foreground="#999", font=("", 8))
        name_lbl.pack(anchor=pack_anchor)
        btn_row2 = ttk.Frame(cell)
        btn_row2.pack(anchor=pack_anchor, fill="x", padx=2)
        slot_btn = ttk.Button(btn_row2, text="Edit",
                               command=lambda p=pos, g=is_g: _slot_click(p, g))
        slot_btn.pack(side="left", fill="x", expand=True)
        slot_btn.bind("<Button-3>",
                       lambda e, p=pos, g=is_g: _slot_right_click(e, p, g))
        remove_btn2 = ttk.Button(btn_row2, text="x", width=2,
                                  command=lambda p=pos: _remove_click(p))
        remove_btn2.pack(side="left", padx=(2, 0))
        info_lbl2 = ttk.Label(cell, text="", font=("", 7), foreground="#555",
                               wraplength=int(32 * _JERSEY_SCALE * 4),
                               justify="left")
        info_lbl2.pack(anchor=pack_anchor, padx=2, pady=(0, 2))
        _slot_labels[pos] = (name_lbl, slot_btn, mini_cv, remove_btn2, info_lbl2)

    def _toggle_line2():
        if line2_var.get():
            line2_frame.pack(fill="x", padx=4, pady=4)
        else:
            line2_frame.pack_forget()
        refresh_players()

    # Save/Cancel at bottom
    btns = ttk.Frame(win)
    btns.pack(fill="x", pady=5)

    def save():
        nonlocal team_dir
        if not team_dir:
            # Auto-save to the team library — use Team Name from the form
            name = ed.widgets.get("Team Name").get().strip() if "Team Name" in ed.widgets else ""
            if not name:
                messagebox.showwarning("Need a name", "Enter a Team Name before saving.")
                return
            safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
            team_dir = os.path.join(TEAM_LIBRARY_DIR, safe)
            if os.path.exists(team_dir):
                if not messagebox.askyesno("Overwrite",
                    f"'{safe}' already exists in the team library. Overwrite?"):
                    return
        redirected = ed.save_dir(team_dir)
        if redirected and redirected != team_dir:
            team_dir = redirected
            host.set_title(f"Team Editor — {os.path.basename(team_dir)}")
            messagebox.showinfo("Redirected",
                f"Base game team — saved to your library copy instead:\n{team_dir}")
        refresh_players()
        messagebox.showinfo("Saved", f"Team saved to:\n{team_dir}")
        tracker.mark_clean()
        if on_save: on_save()
        return True

    def rename_team():
        nonlocal team_dir
        if not team_dir:
            messagebox.showwarning("Save first",
                "Save the team once before renaming — nothing on disk to rename yet.")
            return
        old_name = os.path.basename(team_dir)
        # Detect if the team is inside a campaign (teams_dir parent = teams/) or library
        parent_dir = os.path.dirname(team_dir)
        is_in_campaign = os.path.basename(parent_dir) == "teams"
        # If in campaign, strip numeric prefix for user-friendly prompt
        base_for_prompt = old_name
        prefix = ""
        if is_in_campaign:
            m = re.match(r"^(\d+\s+)(.+)$", old_name)
            if m:
                prefix, base_for_prompt = m.group(1), m.group(2)
        new_name = _prompt_string("Rename Team",
            f"New name for '{base_for_prompt}':")
        if not new_name: return
        safe = re.sub(r'[<>:"/\\|?*]', '_', new_name).strip()
        if not safe:
            messagebox.showwarning("Invalid name", "Name can't be empty or only special chars.")
            return
        new_basename = (prefix + safe) if is_in_campaign else safe
        new_dir = os.path.join(parent_dir, new_basename)
        if os.path.normpath(new_dir) == os.path.normpath(team_dir):
            return  # no-op
        if os.path.exists(new_dir):
            messagebox.showerror("Exists", f"A team named '{new_basename}' already exists.")
            return
        try:
            os.rename(team_dir, new_dir)
        except Exception as e:
            messagebox.showerror("Rename failed", f"{type(e).__name__}: {e}")
            return
        team_dir = new_dir
        # Also update the Team Name field in the editor for consistency
        try: ed.widgets["Team Name"].set(safe)
        except Exception: pass
        ed.save_dir(team_dir)
        host.set_title(f"Team Editor — {new_basename}")
        messagebox.showinfo("Renamed", f"Team renamed to:\n{team_dir}")
        if on_save: on_save()

    def reveal():
        if team_dir:
            open_in_file_explorer(team_dir)
        else:
            messagebox.showinfo("Not saved yet",
                "Save the team once before opening its folder.")

    ttk.Button(btns, text="Save Team", command=save, width=14).pack(side="right", padx=5)
    ttk.Button(btns, text="Rename", command=rename_team, width=10).pack(side="right", padx=5)
    ttk.Button(btns, text="Open Folder", command=reveal, width=12).pack(side="right", padx=5)
    ttk.Button(btns, text="Export to Play Now",
               command=lambda: export_team_to_play_now(ed, team_dir, parent=host),
               width=18).pack(side="right", padx=5)
    ttk.Button(btns, text="Close", command=host.destroy, width=14).pack(side="right")

    if team_dir and os.path.isdir(team_dir):
        ed.load_dir(team_dir)
        refresh_players()

    tracker.save_fn = save
    host.finalize_tracking(tracker)


def open_campaign_editor(campaign_dir=None, on_save=None):
    """Open campaign editor — settings + team list."""
    title = "Campaign Editor" + (f" — {os.path.basename(campaign_dir)}" if campaign_dir else "")
    host = _EditorHost(title, size="750x700")
    win = host.container
    tracker = host.attach_tracker(title)

    ed = CampaignEditor(win)
    ed.pack(fill="both", expand=True, padx=10, pady=10)

    if campaign_dir and os.path.isdir(campaign_dir):
        ed.load_dir(campaign_dir)

    btns = ttk.Frame(win)
    btns.pack(fill="x", pady=5)

    def save():
        nonlocal campaign_dir
        if not campaign_dir:
            name = _prompt_string("New Campaign", "Campaign folder name:")
            if not name: return
            # Strip trailing whitespace and sanitize — Windows can't reliably create
            # directories that end with spaces or contain forbidden characters
            name = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
            if not name:
                messagebox.showwarning("Invalid name", "Campaign name can't be empty or only special characters.")
                return
            campaign_dir = os.path.join(CAMPAIGNS_DIR, name)
        ed.save_dir(campaign_dir)
        messagebox.showinfo("Saved", f"Campaign saved to {campaign_dir}")
        tracker.mark_clean()
        if on_save: on_save()
        return True

    def rename_campaign():
        nonlocal campaign_dir
        if not campaign_dir:
            messagebox.showwarning("Save first",
                "Save the campaign once before renaming — nothing on disk to rename yet.")
            return
        old_name = os.path.basename(campaign_dir)
        new_name = _prompt_string("Rename Campaign", f"New name for '{old_name}':")
        if not new_name: return
        safe = re.sub(r'[<>:"/\\|?*]', '_', new_name).strip()
        if not safe:
            messagebox.showwarning("Invalid name", "Name can't be empty or only special chars.")
            return
        parent = os.path.dirname(campaign_dir)
        new_dir = os.path.join(parent, safe)
        if os.path.normpath(new_dir) == os.path.normpath(campaign_dir):
            return
        if os.path.exists(new_dir):
            messagebox.showerror("Exists", f"A campaign named '{safe}' already exists.")
            return
        try:
            os.rename(campaign_dir, new_dir)
        except Exception as e:
            messagebox.showerror("Rename failed", f"{type(e).__name__}: {e}")
            return
        # If this campaign was the active one, rewrite active.txt
        if read_active_campaign() == old_name:
            try: write_active_campaign(safe)
            except Exception: pass
        campaign_dir = new_dir
        ed.loaded_dir = campaign_dir
        ed.refresh_list()
        host.set_title(f"Campaign Editor — {safe}")
        messagebox.showinfo("Renamed", f"Campaign renamed to:\n{campaign_dir}")
        if on_save: on_save()

    def reveal():
        if campaign_dir:
            open_in_file_explorer(campaign_dir)
        else:
            messagebox.showinfo("Not saved yet",
                "Save the campaign once before opening its folder.")

    def edit_reward_pools():
        nonlocal campaign_dir
        if not campaign_dir:
            messagebox.showwarning("Save first",
                "Save the campaign once before editing reward pools (need a folder to write to).")
            return
        open_reward_pools_editor(campaign_dir)

    ttk.Button(btns, text="Save Settings", command=save, width=16).pack(side="right", padx=5)
    ttk.Button(btns, text="Reward Pools", command=edit_reward_pools, width=14).pack(side="right", padx=5)
    ttk.Button(btns, text="Rename", command=rename_campaign, width=10).pack(side="right", padx=5)
    ttk.Button(btns, text="Open Folder", command=reveal, width=12).pack(side="right", padx=5)
    ttk.Button(btns, text="Close", command=host.destroy, width=14).pack(side="right")

    tracker.save_fn = save
    host.finalize_tracking(tracker)


def open_reward_pools_editor(campaign_dir):
    """Two-tab dialog (Relics / Talents) with per-item checkboxes.
    Checkbox checked = INCLUDED (available in random rewards).
    Unchecked = EXCLUDED (saved into reward_pools.txt so the DLL filters it out).

    If the DLL's dump files are missing (game hasn't been launched yet with
    the mod installed) we show a warning — the list is keyed off those files."""
    relics = load_reward_relic_list()
    talents = load_reward_talent_list()

    win = tk.Toplevel()
    win.title(f"Reward Pools — {os.path.basename(campaign_dir)}")
    _fit_geometry(win, 780, 640)

    if not relics and not talents:
        ttk.Label(win,
            text=("No _reward_relics.txt / _reward_talents.txt found.\n"
                  "Launch the game once with the mod installed to dump the lists, then reopen."),
            foreground="#a00000", wraplength=700, justify="left"
        ).pack(padx=20, pady=40)
        ttk.Button(win, text="Close", command=win.destroy, width=14).pack()
        return

    ex_relics, ex_talents = read_reward_pools(campaign_dir)
    has_saved_pools = os.path.isfile(os.path.join(campaign_dir, "reward_pools.txt"))

    notebook = ttk.Notebook(win)
    notebook.pack(fill="both", expand=True, padx=8, pady=(8, 2))

    # Shared builder for a scrollable checklist tab.
    def _build_checklist_tab(parent_nb, tab_title, items, excluded_set, has_category, has_saved):
        """items: list of tuples. First element is id, second is display name.
        If has_category: 3rd is category, 4th is in_default_pool (bool).
        Else: 3rd is in_default_pool (bool).

        Default state logic: if the user already saved a reward_pools.txt
        (has_saved=True) we trust that file — anything not in excluded_set
        is checked, anything in it is unchecked. If no save yet, we seed the
        checkboxes from in_default_pool so the user starts with the game's
        normal pool state (boss/customization relics pre-unchecked)."""
        tab = ttk.Frame(parent_nb)
        parent_nb.add(tab, text=tab_title)

        # Header: count + filter box + include/exclude-all buttons
        hdr = ttk.Frame(tab)
        hdr.pack(fill="x", padx=6, pady=6)
        count_var = tk.StringVar()
        ttk.Label(hdr, textvariable=count_var, font=("", 9, "bold"), foreground="#0066aa").pack(side="left")
        filter_var = tk.StringVar()
        ttk.Label(hdr, text="  Filter:").pack(side="left")
        filter_entry = ttk.Entry(hdr, textvariable=filter_var, width=20)
        filter_entry.pack(side="left", padx=4)
        cat_var = tk.StringVar(value="All")
        cat_combo = None
        if has_category:
            cats = ["All"] + sorted(set(it[2] for it in items if len(it) >= 3 and it[2]))
            ttk.Label(hdr, text="  Category:").pack(side="left")
            cat_combo = ttk.Combobox(hdr, textvariable=cat_var, values=cats, width=12, state="readonly")
            cat_combo.pack(side="left")

        include_all_btn = ttk.Button(hdr, text="Include All", width=12)
        include_all_btn.pack(side="right", padx=2)
        exclude_all_btn = ttk.Button(hdr, text="Exclude All", width=12)
        exclude_all_btn.pack(side="right", padx=2)

        # Scrollable checklist area.
        body = ttk.Frame(tab)
        body.pack(fill="both", expand=True, padx=6, pady=(0, 6))
        canvas = tk.Canvas(body, highlightthickness=0)
        vs = ttk.Scrollbar(body, orient="vertical", command=canvas.yview)
        canvas.configure(yscrollcommand=vs.set)
        canvas.pack(side="left", fill="both", expand=True)
        vs.pack(side="right", fill="y")
        inner = ttk.Frame(canvas)
        inner_win = canvas.create_window((0, 0), window=inner, anchor="nw")
        def _sync(_e=None): canvas.configure(scrollregion=canvas.bbox("all"))
        inner.bind("<Configure>", _sync)
        def _sync_w(e): canvas.itemconfigure(inner_win, width=e.width)
        canvas.bind("<Configure>", _sync_w)
        def _on_wheel(e): canvas.yview_scroll(int(-1 * (e.delta / 120)), "units")
        canvas.bind("<Enter>", lambda e: canvas.bind_all("<MouseWheel>", _on_wheel))
        canvas.bind("<Leave>", lambda e: canvas.unbind_all("<MouseWheel>"))

        # Build one BooleanVar per item. Checked = INCLUDED.
        # Default state depends on whether the user already saved reward_pools.txt:
        # - saved: honour the excluded set (checked = not in exclusions)
        # - first time: seed from in_default_pool (checked = normally in pool)
        vars_by_id = {}
        frames_by_id = {}
        for it in items:
            iid = it[0]
            in_pool = it[3] if has_category and len(it) >= 4 else (it[2] if not has_category and len(it) >= 3 else True)
            if has_saved:
                default_on = iid not in excluded_set
            else:
                default_on = bool(in_pool)
            vars_by_id[iid] = tk.BooleanVar(value=default_on)

        def _refresh_display():
            # Clear and rebuild according to filter + category.
            for w in inner.winfo_children(): w.destroy()
            frames_by_id.clear()
            q = filter_var.get().strip().lower()
            cat = cat_var.get()
            visible_ids = []
            for it in items:
                iid = it[0]; name = it[1]
                c = it[2] if has_category else ""
                in_pool = it[3] if has_category and len(it) >= 4 else (it[2] if not has_category and len(it) >= 3 else True)
                if has_category and cat != "All" and c != cat: continue
                if q and q not in iid.lower() and q not in name.lower(): continue
                visible_ids.append(iid)
                row = ttk.Frame(inner)
                row.pack(fill="x", padx=4, pady=1, anchor="w")
                chk = ttk.Checkbutton(row, variable=vars_by_id[iid])
                chk.pack(side="left")
                label = f"{name}"
                if has_category and c: label = f"{name}  ·  {c}"
                if name.lower() != iid.lower(): label = f"{label}  [{iid}]"
                if not in_pool: label += "  (not in default pool)"
                lbl = ttk.Label(row, text=label)
                if not in_pool:
                    try: lbl.configure(foreground="#888")
                    except Exception: pass
                lbl.pack(side="left", padx=4)
                frames_by_id[iid] = row
            on = sum(1 for it in items if vars_by_id[it[0]].get())
            count_var.set(f"{on}/{len(items)} enabled  ({len(visible_ids)} shown)")

        def _apply_bulk(state):
            # Only affect the currently visible list.
            q = filter_var.get().strip().lower()
            cat = cat_var.get()
            for it in items:
                iid = it[0]; name = it[1]; c = it[2] if len(it) >= 3 else ""
                if has_category and cat != "All" and c != cat: continue
                if q and q not in iid.lower() and q not in name.lower(): continue
                vars_by_id[iid].set(state)
            _refresh_display()

        include_all_btn.configure(command=lambda: _apply_bulk(True))
        exclude_all_btn.configure(command=lambda: _apply_bulk(False))
        filter_var.trace_add("write", lambda *a: _refresh_display())
        if cat_combo is not None:
            cat_combo.bind("<<ComboboxSelected>>", lambda e: _refresh_display())
        _refresh_display()
        return vars_by_id

    relic_vars = _build_checklist_tab(notebook, f"Relics ({len(relics)})",
                                      relics, ex_relics,
                                      has_category=True, has_saved=has_saved_pools)
    talent_vars = _build_checklist_tab(notebook, f"Talents ({len(talents)})",
                                       talents, ex_talents,
                                       has_category=False, has_saved=has_saved_pools)

    status = ttk.Label(win, text="", foreground="#0066aa", font=("", 9))
    status.pack(anchor="w", padx=10)

    def save_and_close():
        excluded_r = {iid for iid, v in relic_vars.items() if not v.get()}
        excluded_t = {iid for iid, v in talent_vars.items() if not v.get()}
        try:
            write_reward_pools(campaign_dir, excluded_r, excluded_t)
            status.configure(
                text=f"Saved: {len(excluded_r)} relics / {len(excluded_t)} talents excluded "
                     f"-> {os.path.join(campaign_dir, 'reward_pools.txt')}")
            win.after(400, win.destroy)
        except Exception as e:
            messagebox.showerror("Save failed", f"{type(e).__name__}: {e}")

    btn_row = ttk.Frame(win)
    btn_row.pack(fill="x", padx=8, pady=8)
    ttk.Button(btn_row, text="Save & Close", command=save_and_close, width=16).pack(side="right", padx=4)
    ttk.Button(btn_row, text="Cancel", command=win.destroy, width=12).pack(side="right")


def open_team_browser(current_campaign_dir, on_pick, button_label="Open"):
    """Browse all campaigns/teams. Calls on_pick(campaign, team) when user picks.
       button_label customizes the action button (e.g. 'Import' when importing)."""
    win = tk.Toplevel()
    win.title("Browse Teams")
    _fit_geometry(win, 560, 560)

    ttk.Label(win, text="Pick a team", font=("", 11, "bold")).pack(
        anchor="w", padx=10, pady=(10, 2))

    # Display the library as "All Teams" in this context (not the generic "All Players" label)
    LIB_TEAM_LABEL = "All Teams"
    def _display_to_real(v):
        return LIBRARY_SOURCE if v == LIB_TEAM_LABEL else v
    def _real_to_display(v):
        return LIB_TEAM_LABEL if v == LIBRARY_SOURCE else v

    # Campaign dropdown
    row1 = ttk.Frame(win)
    row1.pack(fill="x", padx=10, pady=2)
    ttk.Label(row1, text="From:", width=8, anchor="w").pack(side="left")
    camps = [_real_to_display(c) for c in list_campaigns()]
    # Prefer the library by default when browsing teams
    default = LIB_TEAM_LABEL if LIB_TEAM_LABEL in camps else (camps[0] if camps else "")
    camp_var = tk.StringVar(value=default)
    camp_combo = ttk.Combobox(row1, textvariable=camp_var, values=camps,
                               width=40, state="readonly")
    camp_combo.pack(side="left", fill="x", expand=True)

    # Filter
    row2 = ttk.Frame(win)
    row2.pack(fill="x", padx=10, pady=4)
    ttk.Label(row2, text="Filter:", width=8, anchor="w").pack(side="left")
    filter_var = tk.StringVar()
    ttk.Entry(row2, textvariable=filter_var).pack(side="left", fill="x", expand=True)

    ttk.Label(win, text="Teams:", font=("", 9, "bold")).pack(
        anchor="w", padx=10, pady=(8, 0))
    lst = tk.Listbox(win, height=16)
    lst.pack(fill="both", expand=True, padx=10, pady=4)

    # Preview pane at bottom
    preview = ttk.LabelFrame(win, text=" Preview ")
    preview.pack(fill="x", padx=10, pady=(4, 0))
    preview_label = tk.Label(preview, text="(pick a team to preview)",
                             justify="left", anchor="w",
                             font=("", 9), padx=8, pady=6, background="#f5f5f5")
    preview_label.pack(fill="x")

    _all_teams = []
    def refresh_teams(*a):
        nonlocal _all_teams
        _all_teams = list_teams(_display_to_real(camp_var.get()))
        refresh_filter()
    def refresh_filter(*a):
        q = filter_var.get().strip().lower()
        lst.delete(0, "end")
        for t in _all_teams:
            if not q or q in t.lower():
                lst.insert("end", t)
        if lst.size() > 0:
            lst.selection_clear(0, "end")
            lst.selection_set(0)
            lst.event_generate("<<ListboxSelect>>")
    def on_select(*a):
        sel = lst.curselection()
        if not sel:
            preview_label.configure(text="(pick a team to preview)")
            return
        c = _display_to_real(camp_var.get())
        t = lst.get(sel[0])
        # Build preview: read team.txt + count players
        if c == LIBRARY_SOURCE:
            tdir = resolve_library_team_dir(t) or os.path.join(TEAM_LIBRARY_DIR, t)
        else:
            tdir = os.path.join(CAMPAIGNS_DIR, c, "teams", t)
        lines = [f"Folder: {t}"]
        try:
            td = read_kv(os.path.join(tdir, "team.txt"))
            for k in ("Team Name", "City", "Abbreviation"):
                if td.get(k): lines.append(f"  {k}: {td[k]}")
            pdir = os.path.join(tdir, "players")
            if os.path.isdir(pdir):
                n = sum(1 for f in os.listdir(pdir) if f.endswith(".txt"))
                lines.append(f"  Players: {n}")
        except Exception: pass
        preview_label.configure(text="\n".join(lines))

    camp_var.trace_add("write", refresh_teams)
    filter_var.trace_add("write", refresh_filter)
    lst.bind("<<ListboxSelect>>", on_select)

    def pick():
        c = _display_to_real(camp_var.get())
        if not c: return
        sel = lst.curselection()
        if not sel: return
        on_pick(c, lst.get(sel[0]))
        win.destroy()

    lst.bind("<Double-Button-1>", lambda e: pick())
    win.bind("<Return>", lambda e: pick())

    btns = ttk.Frame(win)
    btns.pack(fill="x", pady=8)
    ttk.Button(btns, text=button_label, command=pick, width=14).pack(side="right", padx=8)
    ttk.Button(btns, text="Cancel", command=win.destroy, width=14).pack(side="right")

    refresh_teams()


def open_multi_team_browser(on_pick):
    """Browse all teams across library/base game/custom/campaigns with multi-select.
       Calls on_pick([(camp, team), ...]) when user clicks Import."""
    win = tk.Toplevel()
    win.title("Import Teams")
    _fit_geometry(win, 640, 640)

    ttk.Label(win, text="Pick one or more teams to import",
              font=("", 11, "bold")).pack(anchor="w", padx=10, pady=(10, 2))
    ttk.Label(win,
        text="Ctrl+Click to add individual teams, Shift+Click for a range.",
        foreground="#555", font=("", 9)
    ).pack(anchor="w", padx=10, pady=(0, 6))

    LIB_TEAM_LABEL = "All Teams"
    def _display_to_real(v):
        return LIBRARY_SOURCE if v == LIB_TEAM_LABEL else v
    def _real_to_display(v):
        return LIB_TEAM_LABEL if v == LIBRARY_SOURCE else v

    row1 = ttk.Frame(win)
    row1.pack(fill="x", padx=10, pady=2)
    ttk.Label(row1, text="From:", width=8, anchor="w").pack(side="left")
    camps = [_real_to_display(c) for c in list_campaigns()]
    default = LIB_TEAM_LABEL if LIB_TEAM_LABEL in camps else (camps[0] if camps else "")
    camp_var = tk.StringVar(value=default)
    camp_combo = ttk.Combobox(row1, textvariable=camp_var, values=camps,
                               width=40, state="readonly")
    camp_combo.pack(side="left", fill="x", expand=True)

    row2 = ttk.Frame(win)
    row2.pack(fill="x", padx=10, pady=4)
    ttk.Label(row2, text="Filter:", width=8, anchor="w").pack(side="left")
    filter_var = tk.StringVar()
    ttk.Entry(row2, textvariable=filter_var).pack(side="left", fill="x", expand=True)

    ttk.Label(win, text="Teams (multi-select):", font=("", 9, "bold")).pack(
        anchor="w", padx=10, pady=(8, 0))
    lst = tk.Listbox(win, height=20, selectmode="extended")
    lst.pack(fill="both", expand=True, padx=10, pady=4)

    count_label = ttk.Label(win, text="0 selected", foreground="#666", font=("", 9))
    count_label.pack(anchor="w", padx=10)

    _all_teams = []
    def refresh_teams(*a):
        nonlocal _all_teams
        _all_teams = list_teams(_display_to_real(camp_var.get()))
        refresh_filter()
    def refresh_filter(*a):
        q = filter_var.get().strip().lower()
        lst.delete(0, "end")
        for t in _all_teams:
            if not q or q in t.lower():
                lst.insert("end", t)
        count_label.configure(text="0 selected")
    def on_select(*a):
        count_label.configure(text=f"{len(lst.curselection())} selected")

    camp_var.trace_add("write", refresh_teams)
    filter_var.trace_add("write", refresh_filter)
    lst.bind("<<ListboxSelect>>", on_select)

    def pick_all():
        lst.selection_set(0, "end")
        on_select()

    def pick():
        sel = lst.curselection()
        if not sel:
            messagebox.showwarning("No selection", "Select at least one team.")
            return
        c = _display_to_real(camp_var.get())
        picks = [(c, lst.get(i)) for i in sel]
        win.destroy()
        on_pick(picks)

    btns = ttk.Frame(win)
    btns.pack(fill="x", pady=8, padx=10)
    ttk.Button(btns, text="Select All", command=pick_all, width=12).pack(side="left")
    ttk.Button(btns, text="Import Selected", command=pick, width=16).pack(side="right", padx=(8, 0))
    ttk.Button(btns, text="Cancel", command=win.destroy, width=12).pack(side="right")

    refresh_teams()


def open_multi_player_browser(on_pick):
    """Multi-select player browser. Calls on_pick([(camp, team, filename), ...])."""
    win = tk.Toplevel()
    win.title("Import Players")
    _fit_geometry(win, 640, 640)

    ttk.Label(win, text="Pick one or more players to import",
              font=("", 11, "bold")).pack(anchor="w", padx=10, pady=(10, 2))
    ttk.Label(win,
        text="Ctrl+Click to add individual players, Shift+Click for a range.",
        foreground="#555", font=("", 9)
    ).pack(anchor="w", padx=10, pady=(0, 6))

    row1 = ttk.Frame(win)
    row1.pack(fill="x", padx=10, pady=2)
    ttk.Label(row1, text="From:", width=10, anchor="w").pack(side="left")
    camps = list_campaigns()
    camp_var = tk.StringVar(value=camps[0] if camps else "")
    camp_combo = ttk.Combobox(row1, textvariable=camp_var, values=camps,
                               width=44, state="readonly")
    camp_combo.pack(side="left", fill="x", expand=True)

    row2 = ttk.Frame(win)
    row2.pack(fill="x", padx=10, pady=2)
    ttk.Label(row2, text="Team:", width=10, anchor="w").pack(side="left")
    team_var = tk.StringVar()
    team_combo = ttk.Combobox(row2, textvariable=team_var, values=[],
                               width=44, state="readonly")
    team_combo.pack(side="left", fill="x", expand=True)

    row3 = ttk.Frame(win)
    row3.pack(fill="x", padx=10, pady=4)
    ttk.Label(row3, text="Filter:", width=10, anchor="w").pack(side="left")
    filter_var = tk.StringVar()
    ttk.Entry(row3, textvariable=filter_var).pack(side="left", fill="x", expand=True)

    ttk.Label(win, text="Players (multi-select):", font=("", 9, "bold")).pack(
        anchor="w", padx=10, pady=(8, 0))
    lst = tk.Listbox(win, height=18, selectmode="extended")
    lst.pack(fill="both", expand=True, padx=10, pady=4)

    count_label = ttk.Label(win, text="0 selected", foreground="#666", font=("", 9))
    count_label.pack(anchor="w", padx=10)

    _all_players = []
    def refresh_teams(*a):
        c = camp_var.get()
        if c == LIBRARY_SOURCE:
            team_combo["values"] = []
            team_var.set("")
            refresh_players()
        else:
            team_combo["values"] = list_teams(c) if c else []
            vals = team_combo["values"]
            if vals: team_var.set(vals[0])
            else: team_var.set("")
    def refresh_players(*a):
        nonlocal _all_players
        c, t = camp_var.get(), team_var.get()
        if c == LIBRARY_SOURCE: t = ""
        if c and (t or c == LIBRARY_SOURCE):
            _all_players = list_players(c, t)
        else:
            _all_players = []
        refresh_filter()
    def refresh_filter(*a):
        q = filter_var.get().strip().lower()
        lst.delete(0, "end")
        for p in _all_players:
            base = p[:-4] if p.endswith(".txt") else p
            if not q or q in base.lower():
                lst.insert("end", base)
        count_label.configure(text="0 selected")
    def on_select(*a):
        count_label.configure(text=f"{len(lst.curselection())} selected")

    camp_var.trace_add("write", refresh_teams)
    team_var.trace_add("write", refresh_players)
    filter_var.trace_add("write", refresh_filter)
    lst.bind("<<ListboxSelect>>", on_select)

    def pick_all():
        lst.selection_set(0, "end")
        on_select()

    def pick():
        sel = lst.curselection()
        if not sel:
            messagebox.showwarning("No selection", "Select at least one player.")
            return
        c, t = camp_var.get(), team_var.get()
        if c == LIBRARY_SOURCE: t = ""
        picks = [(c, t, lst.get(i) + ".txt") for i in sel]
        win.destroy()
        on_pick(picks)

    btns = ttk.Frame(win)
    btns.pack(fill="x", pady=8, padx=10)
    ttk.Button(btns, text="Select All", command=pick_all, width=12).pack(side="left")
    ttk.Button(btns, text="Import Selected", command=pick, width=16).pack(side="right", padx=(8, 0))
    ttk.Button(btns, text="Cancel", command=win.destroy, width=12).pack(side="right")

    refresh_teams()


def open_player_browser(on_pick, button_label="Open"):
    """Browse all campaigns/teams/players. Calls on_pick(campaign, team, filename).
       button_label lets callers use 'Import' vs 'Open' etc."""
    win = tk.Toplevel()
    win.title("Browse Players")
    _fit_geometry(win, 640, 660)

    ttk.Label(win, text="Pick a player", font=("", 11, "bold")).pack(
        anchor="w", padx=10, pady=(10, 2))
    ttk.Label(win,
        text="Browse the All Players library, or any campaign's roster.",
        foreground="#555", font=("", 9), wraplength=600
    ).pack(anchor="w", padx=10, pady=(0, 6))

    # Source campaign
    row1 = ttk.Frame(win)
    row1.pack(fill="x", padx=10, pady=2)
    ttk.Label(row1, text="From:", width=10, anchor="w").pack(side="left")
    camps = list_campaigns()
    camp_var = tk.StringVar(value=camps[0] if camps else "")
    camp_combo = ttk.Combobox(row1, textvariable=camp_var, values=camps,
                               width=44, state="readonly")
    camp_combo.pack(side="left", fill="x", expand=True)

    # Team (only relevant when not in library)
    row2 = ttk.Frame(win)
    row2.pack(fill="x", padx=10, pady=2)
    ttk.Label(row2, text="Team:", width=10, anchor="w").pack(side="left")
    team_var = tk.StringVar()
    team_combo = ttk.Combobox(row2, textvariable=team_var, values=[],
                               width=44, state="readonly")
    team_combo.pack(side="left", fill="x", expand=True)

    # Filter
    row3 = ttk.Frame(win)
    row3.pack(fill="x", padx=10, pady=4)
    ttk.Label(row3, text="Filter:", width=10, anchor="w").pack(side="left")
    filter_var = tk.StringVar()
    ttk.Entry(row3, textvariable=filter_var).pack(
        side="left", fill="x", expand=True)

    ttk.Label(win, text="Players:", font=("", 9, "bold")).pack(
        anchor="w", padx=10, pady=(8, 0))
    lst = tk.Listbox(win, height=14)
    lst.pack(fill="both", expand=True, padx=10, pady=4)

    # Preview pane
    preview = ttk.LabelFrame(win, text=" Preview ")
    preview.pack(fill="x", padx=10, pady=(4, 0))
    preview_label = tk.Label(preview, text="(pick a player to preview)",
                             justify="left", anchor="w",
                             font=("", 9), padx=8, pady=6, background="#f5f5f5")
    preview_label.pack(fill="x")

    _all_players = []
    def _current_src_dir():
        c, t = camp_var.get(), team_var.get()
        if c == LIBRARY_SOURCE:
            return PLAYER_LIBRARY_DIR  # primary; resolve_library_player_path handles others
        if c and t:
            return os.path.join(CAMPAIGNS_DIR, c, "teams", t, "players")
        return None
    def _resolve_player_path(filename):
        """Find actual path for a player file (checks all library subfolders)."""
        c = camp_var.get()
        if c == LIBRARY_SOURCE:
            return resolve_library_player_path(filename) or os.path.join(PLAYER_LIBRARY_DIR, filename)
        t = team_var.get()
        return os.path.join(CAMPAIGNS_DIR, c, "teams", t, "players", filename)
    def refresh_teams(*a):
        c = camp_var.get()
        if c == LIBRARY_SOURCE:
            team_combo["values"] = []
            team_var.set("")
            refresh_players()
        else:
            team_combo["values"] = list_teams(c) if c else []
            # Auto-pick first team
            vals = team_combo["values"]
            if vals: team_var.set(vals[0])
            else: team_var.set("")
    def refresh_players(*a):
        nonlocal _all_players
        c, t = camp_var.get(), team_var.get()
        if c == LIBRARY_SOURCE: t = ""
        if c and (t or c == LIBRARY_SOURCE):
            _all_players = list_players(c, t)
        else:
            _all_players = []
        refresh_filter()
    def refresh_filter(*a):
        q = filter_var.get().strip().lower()
        lst.delete(0, "end")
        for p in _all_players:
            base = p[:-4] if p.endswith(".txt") else p
            if not q or q in base.lower():
                lst.insert("end", base)
        if lst.size() > 0:
            lst.selection_clear(0, "end")
            lst.selection_set(0)
            lst.event_generate("<<ListboxSelect>>")
        else:
            preview_label.configure(text="(no players match)")
    def on_select(*a):
        sel = lst.curselection()
        if not sel:
            preview_label.configure(text="(pick a player to preview)")
            return
        base = lst.get(sel[0])
        path = _resolve_player_path(base + ".txt")
        lines = [f"File: {base}.txt"]
        try:
            d = read_kv(path)
            for k in ("Name", "Number", "Import Player", "Face", "Size", "Ability"):
                if d.get(k): lines.append(f"  {k}: {d[k]}")
            # Stats (skater or goalie)
            stat_keys = [s for s in ("Speed", "Shot Power", "Accuracy", "Checking",
                                      "Skill", "Catching", "Glove", "Blocker")
                          if d.get(s)]
            if stat_keys:
                vals = []
                for s in stat_keys:
                    vals.append(f"{s}={d[s]}")
                lines.append("  Stats: " + ", ".join(vals))
        except Exception: pass
        preview_label.configure(text="\n".join(lines))

    camp_var.trace_add("write", refresh_teams)
    team_var.trace_add("write", refresh_players)
    filter_var.trace_add("write", refresh_filter)
    lst.bind("<<ListboxSelect>>", on_select)

    def pick():
        c, t = camp_var.get(), team_var.get()
        if c == LIBRARY_SOURCE: t = ""
        if not c or (not t and c != LIBRARY_SOURCE): return
        sel = lst.curselection()
        if not sel: return
        base = lst.get(sel[0]).split("  —  ")[0].strip()
        on_pick(c, t, base + ".txt")
        win.destroy()

    lst.bind("<Double-Button-1>", lambda e: pick())
    win.bind("<Return>", lambda e: pick())

    btns = ttk.Frame(win)
    btns.pack(fill="x", pady=8)
    ttk.Button(btns, text=button_label, command=pick, width=14).pack(side="right", padx=8)
    ttk.Button(btns, text="Cancel", command=win.destroy, width=14).pack(side="right")

    refresh_teams()


# ============================================================
#   NEW-CAMPAIGN PLAYER-TEAM TEMPLATE
# ============================================================
# Copies the Example Campaign's player_teams/ folder into new campaigns so the
# user gets the real in-game starting players (and the 7 canonical free agents)
# ready to edit. If Example Campaign is missing, we skip silently — the user
# can still add to the draft pool or create player teams manually.

EXAMPLE_CAMPAIGN_DIR = os.path.join(CAMPAIGNS_DIR, "Example Campaign")


def _seed_player_teams_from_example(campaign_dir):
    """Copy player_teams/ from Example Campaign into a new campaign.
       Walks the source tree and copies anything the destination is missing —
       so previously-incomplete seeds (e.g. empty draft_pool) get filled in on
       next run instead of being permanently broken."""
    src = os.path.join(EXAMPLE_CAMPAIGN_DIR, "player_teams")
    if not os.path.isdir(src):
        return  # Example Campaign missing — skip
    dst = os.path.join(campaign_dir, "player_teams")
    os.makedirs(dst, exist_ok=True)
    import shutil
    try:
        for root, dirs, files in os.walk(src):
            rel = os.path.relpath(root, src)
            out_root = dst if rel == "." else os.path.join(dst, rel)
            os.makedirs(out_root, exist_ok=True)
            for f in files:
                s = os.path.join(root, f)
                d = os.path.join(out_root, f)
                if not os.path.exists(d):
                    shutil.copy2(s, d)
    except Exception as e:
        print(f"[warn] could not seed player_teams: {e}")


# ============================================================
#   MAIN MENU  (tabbed — Home + dynamically-added editor tabs)
# ============================================================
class MainMenu(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("T2T Campaign Creator")
        # Initial size adapts to the screen — caps at 1280x820 but never asks
        # for more than 95% of available pixels. Important on Steam Deck (720p)
        # and laptop screens where 1200x820 was too tall.
        try:
            sw = self.winfo_screenwidth()
            sh = self.winfo_screenheight()
            init_w = min(1280, int(sw * 0.95))
            init_h = min(820, int(sh * 0.92))
            self.geometry(f"{init_w}x{init_h}")
        except Exception:
            _fit_geometry(self, 1200, 820)
        # Allow shrinking down to a Steam Deck / small-laptop friendly size.
        # Inner content scrolls when it doesn't fit at this minimum.
        self.minsize(800, 520)

        # Top-level notebook that holds the Home tab plus every open editor.
        self.notebook = ttk.Notebook(self)
        self.notebook.pack(fill="both", expand=True)

        # Register this notebook so editor openers add tabs instead of Toplevels.
        global _TAB_HOST
        _TAB_HOST = self.notebook

        self._build_home_tab()

        # Keyboard: Ctrl+W closes the current (non-home) tab
        self.bind_all("<Control-w>", lambda e: self._close_current_tab())

        # Middle-click closes a tab anywhere on the label. Left-click on the
        # rightmost × region of the tab label closes that tab; left-click
        # elsewhere still triggers native tab switching (we use add="+" so
        # the native handler keeps running). Ctrl+W closes the current tab.
        self.notebook.bind("<Button-2>", self._close_clicked_tab)  # middle-click
        self.notebook.bind("<Button-1>", self._check_tab_close_click, add="+")

        # When the user switches back to the Home tab, refresh the tree
        # so new/renamed/deleted files show up without manual refresh.
        self.notebook.bind("<<NotebookTabChanged>>", self._on_tab_changed)

        # Hard-exit on window close so we don't leave a zombie process holding
        # the exe file open (tkinter can hang on mainloop with dangling Toplevels).
        self.protocol("WM_DELETE_WINDOW", self._on_app_close)

        # Silent update check 1.5s after startup (non-blocking, threaded).
        try: self.after(1500, lambda: check_for_updates_async(self, silent=True))
        except Exception: pass

    def _on_app_close(self):
        # Check every open tab for unsaved changes; let user abort close.
        try:
            dirty_titles = []
            for tab_id in self.notebook.tabs():
                tab = self.notebook.nametowidget(tab_id)
                tracker = getattr(tab, "_dirty_tracker", None)
                if tracker and tracker.is_dirty():
                    dirty_titles.append(tracker.title or "(untitled)")
            if dirty_titles:
                summary = "\n  - ".join(dirty_titles)
                ans = messagebox.askyesnocancel(
                    "Unsaved changes",
                    f"The following tabs have unsaved changes:\n\n  - {summary}\n\n"
                    f"Quit anyway?")
                # Yes = discard + quit; No/Cancel = stay open
                if ans is None or ans is False: return
        except Exception: pass
        try: self.quit()
        except Exception: pass
        try: self.destroy()
        except Exception: pass
        os._exit(0)

    def _on_tab_changed(self, event):
        try:
            if self.notebook.index(self.notebook.select()) == 0:
                self._refresh_tree()
                self._refresh_active_picker()
        except Exception: pass

    def _build_home_tab(self):
        home = ttk.Frame(self.notebook)
        self.notebook.add(home, text="🏠 Home")

        # Title
        ttk.Label(home, text="Custom Campaign Framework",
                  font=("", 14, "bold")).pack(pady=(12, 2))
        ttk.Label(home, text="Creator & Editor — everything in one place, no file-picker needed.",
                  foreground="#555").pack(pady=(0, 2))
        ver_row = ttk.Frame(home)
        ver_row.pack(pady=(0, 10))
        ttk.Label(ver_row, text=f"v{_read_local_version()}",
                  foreground="#888", font=("", 8)).pack(side="left", padx=(0, 8))
        ttk.Button(ver_row, text="Check for updates",
                   command=lambda: check_for_updates_async(self, silent=False)
                   ).pack(side="left")

        # Two-column layout: tree (left) + actions (right)
        body = ttk.Frame(home)
        body.pack(fill="both", expand=True, padx=12, pady=4)

        # --- LEFT: tree view of all content ---
        left = ttk.Frame(body)
        left.pack(side="left", fill="both", expand=True, padx=(0, 8))

        ttk.Label(left, text="Everything in your Campaigns folder:",
                  font=("", 10, "bold")).pack(anchor="w", pady=(0, 2))
        ttk.Label(left,
                  text="Double-click any item to edit it. Right-click for more actions.",
                  foreground="#777", font=("", 8)).pack(anchor="w")

        tree_frame = ttk.Frame(left)
        tree_frame.pack(fill="both", expand=True, pady=4)
        self._tree = ttk.Treeview(tree_frame, show="tree", selectmode="browse")
        tree_scroll = ttk.Scrollbar(tree_frame, orient="vertical",
                                     command=self._tree.yview)
        self._tree.configure(yscrollcommand=tree_scroll.set)
        self._tree.pack(side="left", fill="both", expand=True)
        tree_scroll.pack(side="right", fill="y")

        self._tree.bind("<Double-Button-1>", self._tree_double_click)
        self._tree.bind("<Button-3>", self._tree_right_click)  # right-click menu

        ttk.Button(left, text="↻ Refresh tree",
                   command=self._refresh_tree).pack(anchor="w", pady=(2, 0))

        # --- RIGHT: actions + active campaign picker ---
        # Wrap the right column in a scrollable canvas so on small screens
        # (Steam Deck 720p, narrow laptops) the user can still reach every
        # action group instead of having Community/Edit boxes clipped off.
        right_outer = ttk.Frame(body, width=300)
        right_outer.pack(side="right", fill="y")
        right_outer.pack_propagate(False)
        right_canvas = tk.Canvas(right_outer, highlightthickness=0, width=290)
        right_sb = ttk.Scrollbar(right_outer, orient="vertical",
                                  command=right_canvas.yview)
        right_canvas.configure(yscrollcommand=right_sb.set)
        right_sb.pack(side="right", fill="y")
        right_canvas.pack(side="left", fill="both", expand=True)
        right = ttk.Frame(right_canvas)
        right_window = right_canvas.create_window((0, 0), window=right, anchor="nw")
        def _on_right_resize(e=None):
            try:
                right_canvas.configure(scrollregion=right_canvas.bbox("all"))
                # Match inner frame width to canvas so children fill horizontally
                right_canvas.itemconfig(right_window, width=right_canvas.winfo_width())
            except Exception: pass
        right.bind("<Configure>", _on_right_resize)
        right_canvas.bind("<Configure>", _on_right_resize)
        # Mouse wheel scrolling while pointer is over the right column
        def _on_right_wheel(e):
            try: right_canvas.yview_scroll(int(-1 * (e.delta / 120)), "units")
            except Exception: pass
        right_canvas.bind("<Enter>", lambda e: right_canvas.bind_all("<MouseWheel>", _on_right_wheel))
        right_canvas.bind("<Leave>", lambda e: right_canvas.unbind_all("<MouseWheel>"))

        # Active campaign
        active_box = ttk.LabelFrame(right, text=" Active campaign (what the game will load) ")
        active_box.pack(pady=(0, 10), fill="x")
        cur = read_active_campaign() or "(none — uses base game)"
        self._active_label = ttk.Label(active_box, text=f"Currently: {cur}",
                                        font=("", 9, "bold"), foreground="#0066aa",
                                        wraplength=240, justify="left")
        self._active_label.pack(anchor="w", padx=8, pady=(6, 2))

        choices = [c for c in list_campaigns() if c != LIBRARY_SOURCE] + ["default"]
        self._active_var = tk.StringVar(value=read_active_campaign() or (choices[0] if choices else ""))
        self._active_combo = ttk.Combobox(active_box, textvariable=self._active_var,
                                           values=choices, width=28, state="readonly")
        self._active_combo.pack(padx=8, pady=2, fill="x")
        ar = ttk.Frame(active_box)
        ar.pack(anchor="w", padx=8, pady=(2, 6))
        ttk.Button(ar, text="Set Active", width=12,
                   command=self._set_active_campaign).pack(side="left", padx=(0, 4))
        ttk.Button(ar, text="↻", width=3,
                   command=self._refresh_active_picker).pack(side="left")
        ttk.Label(active_box,
                  text="'default' = disable mod, play vanilla.",
                  foreground="#777", font=("", 8), wraplength=240).pack(anchor="w", padx=8, pady=(0, 4))

        # Create new
        create_box = ttk.LabelFrame(right, text=" Create new ")
        create_box.pack(pady=(0, 10), fill="x")
        for lbl, cmd in [("➕ New Campaign", self.new_campaign),
                          ("➕ New Team", self.new_team),
                          ("➕ New Skater", self.new_player),
                          ("➕ New Goalie", self.new_goalie)]:
            ttk.Button(create_box, text=lbl, command=cmd, width=24).pack(
                padx=8, pady=2, anchor="w")

        import_box = ttk.LabelFrame(right, text=" Import from game ")
        import_box.pack(pady=(0, 10), fill="x")
        ttk.Button(import_box, text="📥 Import Game Team…",
                   command=self.import_game_team, width=24).pack(padx=8, pady=2, anchor="w")
        ttk.Label(import_box,
                  text="Run the game once first — it auto-dumps\nall teams + players to your library.\nUse this to create a custom copy\nof any game team for editing.",
                  foreground="#777", font=("", 8), justify="left").pack(padx=8, pady=(0, 4))

        # Edit existing (opens browsers — quick alternative to the tree)
        edit_box = ttk.LabelFrame(right, text=" Edit existing (browse) ")
        edit_box.pack(pady=(0, 10), fill="x")
        for lbl, cmd in [("✎ Edit Campaign…", self.edit_campaign),
                          ("✎ Edit Team…", self.edit_team),
                          ("✎ Edit Player…", self.edit_player)]:
            ttk.Button(edit_box, text=lbl, command=cmd, width=24).pack(
                padx=8, pady=2, anchor="w")

        # Community — download other people's campaigns or share your own.
        community_box = ttk.LabelFrame(right, text=" Community ")
        community_box.pack(pady=(0, 10), fill="x")
        ttk.Button(community_box, text="🌐 Browse Community Campaigns…",
                   command=self.browse_community, width=30).pack(padx=8, pady=2, anchor="w")
        ttk.Button(community_box, text="📤 Share Your Campaign…",
                   command=self.share_community, width=30).pack(padx=8, pady=2, anchor="w")
        ttk.Button(community_box, text="📦 Import Campaign from File…",
                   command=self.import_campaign_from_file, width=30).pack(padx=8, pady=2, anchor="w")
        ttk.Label(community_box,
                  text="Browse downloads straight into your\ncampaigns folder. Share zips the active\ncampaign and uploads it anonymously.",
                  foreground="#777", font=("", 8), justify="left"
                  ).pack(padx=8, pady=(0, 4))

        ttk.Label(home,
            text="Editors open as tabs — Ctrl+W closes current tab.",
            foreground="#555", font=("", 8)).pack(pady=(4, 0))
        ttk.Label(home, text=f"Folder: {SCRIPT_DIR}",
                  foreground="#666", font=("", 8)).pack(side="bottom", pady=6)

        # Build initial tree
        self._refresh_tree()

    # ---------- file-tree helpers ----------
    def _refresh_tree(self):
        """Rebuild the home-tab tree from disk. Node IDs encode (kind, path)."""
        t = self._tree
        t.delete(*t.get_children())
        active = read_active_campaign()

        # Library node
        lib_root = t.insert("", "end",
            text="📚 Library  (shared players & teams, reusable across campaigns)",
            values=("library_root", ""), open=True)
        # Library players
        lib_players = t.insert(lib_root, "end", text="👤 All Players",
            values=("lib_players_folder", ""), open=False)
        if os.path.isdir(PLAYER_LIBRARY_DIR):
            for f in sorted(os.listdir(PLAYER_LIBRARY_DIR)):
                if f.endswith(".txt"):
                    full = os.path.join(PLAYER_LIBRARY_DIR, f)
                    t.insert(lib_players, "end",
                        text=f[:-4],
                        values=("player_file", full))
        # Library teams
        lib_teams = t.insert(lib_root, "end", text="🏒 All Teams",
            values=("lib_teams_folder", ""), open=False)
        if os.path.isdir(TEAM_LIBRARY_DIR):
            for d in sorted(os.listdir(TEAM_LIBRARY_DIR)):
                tdir = os.path.join(TEAM_LIBRARY_DIR, d)
                if not os.path.isdir(tdir): continue
                tn = t.insert(lib_teams, "end", text=d,
                    values=("team_folder", tdir))
                self._populate_team_players(tn, tdir)

        # Auto-generated folders (base game + custom/in-game-editor) — READ-ONLY.
        # Double-click auto-copies to library before opening the editor.
        for folder_name, icon, label in [
            ("Base Game Teams", "🔒", "Base Game Teams"),
            ("Custom Teams (in-game editor)", "🔒", "Custom Teams (in-game editor)"),
        ]:
            fdir = os.path.join(LIBRARY_DIR, folder_name)
            if os.path.isdir(fdir):
                node = t.insert(lib_root, "end",
                    text=f"{icon} {label}  (read-only — click to copy into your library)",
                    values=("lib_teams_folder", ""), open=False)
                for d in sorted(os.listdir(fdir)):
                    tdir = os.path.join(fdir, d)
                    if not os.path.isdir(tdir): continue
                    tn = t.insert(node, "end", text=f"🔒 {d}",
                        values=("team_folder", tdir))
                    self._populate_team_players(tn, tdir, readonly=True)

        for folder_name, icon, label in [
            ("Base Game Players", "🔒", "Base Game Players"),
            ("Custom Players (in-game editor)", "🔒", "Custom Players (in-game editor)"),
        ]:
            fdir = os.path.join(LIBRARY_DIR, folder_name)
            if os.path.isdir(fdir):
                node = t.insert(lib_root, "end",
                    text=f"{icon} {label}  (read-only — click to copy into your library)",
                    values=("lib_players_folder", ""), open=False)
                for f in sorted(os.listdir(fdir)):
                    if f.endswith(".txt") and not f.startswith("_"):
                        full = os.path.join(fdir, f)
                        t.insert(node, "end",
                            text=f"🔒 {f[:-4]}",
                            values=("player_file", full))

        # Campaigns node
        camp_root = t.insert("", "end", text="📂 Campaigns",
            values=("campaigns_root", ""), open=True)
        campaigns = [c for c in list_campaigns() if c != LIBRARY_SOURCE]
        for c in campaigns:
            cdir = os.path.join(CAMPAIGNS_DIR, c)
            label = f"📌 {c}" if c == active else c
            if c == active:
                label += "   ← ACTIVE"
            cn = t.insert(camp_root, "end", text=label,
                values=("campaign_folder", cdir),
                open=(c == active))  # auto-expand the active one
            teams_dir = os.path.join(cdir, "teams")
            if os.path.isdir(teams_dir):
                for td in sorted(os.listdir(teams_dir)):
                    tdir = os.path.join(teams_dir, td)
                    if not os.path.isdir(tdir): continue
                    tn = t.insert(cn, "end", text=td,
                        values=("team_folder", tdir))
                    self._populate_team_players(tn, tdir)

    def _populate_team_players(self, parent_node, team_dir, readonly=False):
        pdir = os.path.join(team_dir, "players")
        if not os.path.isdir(pdir): return
        for p in sorted(os.listdir(pdir)):
            if not p.endswith(".txt"): continue
            full = os.path.join(pdir, p)
            label = f"🔒 {p[:-4]}" if readonly else p[:-4]
            self._tree.insert(parent_node, "end",
                text=label,
                values=("player_file", full))

    def _tree_node_info(self, item_id):
        """Return (kind, path) for a tree node."""
        vals = self._tree.item(item_id, "values")
        if not vals or len(vals) < 2: return (None, None)
        return vals[0], vals[1]

    def _tree_double_click(self, event):
        sel = self._tree.focus()
        if not sel: return
        kind, path = self._tree_node_info(sel)
        if kind == "campaign_folder":
            open_campaign_editor(path)
        elif kind == "team_folder":
            self._edit_auto_copy_team(path)
        elif kind == "player_file":
            self._edit_auto_copy_player(path)

    def _tree_right_click(self, event):
        """Right-click: context menu with open/delete/rename (kind-dependent)."""
        iid = self._tree.identify_row(event.y)
        if not iid: return
        self._tree.focus(iid)
        self._tree.selection_set(iid)
        kind, path = self._tree_node_info(iid)
        menu = tk.Menu(self, tearoff=0)
        if kind == "campaign_folder":
            menu.add_command(label="Edit",
                command=lambda: open_campaign_editor(path))
            menu.add_command(label="Set Active",
                command=lambda: self._set_active_from_tree(os.path.basename(path)))
            menu.add_command(label="Open folder on disk",
                command=lambda: open_in_file_explorer(path))
            menu.add_separator()
            menu.add_command(label="Delete",
                command=lambda: self._delete_from_tree(path, "campaign"))
        elif kind == "team_folder":
            menu.add_command(label="Edit (copies to library if base game)",
                command=lambda: self._edit_auto_copy_team(path))
            menu.add_command(label="Duplicate",
                command=lambda: self._duplicate_team(path))
            menu.add_command(label="Copy to Library",
                command=lambda: self._copy_team_to_library(path))
            menu.add_command(label="Open folder on disk",
                command=lambda: open_in_file_explorer(path))
            menu.add_separator()
            menu.add_command(label="Delete",
                command=lambda: self._delete_from_tree(path, "team"))
        elif kind == "player_file":
            menu.add_command(label="Edit (copies to library if base game)",
                command=lambda: self._edit_auto_copy_player(path))
            menu.add_command(label="Duplicate",
                command=lambda: self._duplicate_player(path))
            menu.add_command(label="Copy to Library",
                command=lambda: self._copy_player_to_library(path))
            menu.add_command(label="Show file on disk",
                command=lambda: open_in_file_explorer(path))
            menu.add_separator()
            menu.add_command(label="Delete",
                command=lambda: self._delete_from_tree(path, "player"))
        else:
            return
        try: menu.tk_popup(event.x_root, event.y_root)
        finally: menu.grab_release()

    def _edit_auto_copy_team(self, path):
        """Edit a team — if it's in a base game folder, auto-copy to library first."""
        new_path = auto_copy_to_library(path, is_team=True)
        if new_path != path:
            messagebox.showinfo("Copied",
                f"Base game team copied to your library for editing:\n{os.path.basename(new_path)}")
            self._refresh_tree()
        open_team_editor(new_path)

    def _edit_auto_copy_player(self, path):
        """Edit a player — if it's in a base game folder, auto-copy to library first."""
        new_path = auto_copy_to_library(path)
        if new_path != path:
            messagebox.showinfo("Copied",
                f"Base game player copied to your library for editing:\n{os.path.basename(new_path)}")
            self._refresh_tree()
        open_player_editor(new_path)

    def _set_active_from_tree(self, name):
        self._active_var.set(name)
        self._set_active_campaign()
        self._refresh_tree()

    def _duplicate_team(self, path):
        """Duplicate a team folder in the same parent directory with an incremented name."""
        try:
            parent = os.path.dirname(path)
            base = os.path.basename(path)
            # Strip numeric prefix if in a campaign
            m = re.match(r"^(\d+\s+)(.+)$", base)
            prefix, name = (m.group(1), m.group(2)) if m else ("", base)
            new_name = deduplicate_dir(name, parent)
            # If in campaign, give it the next number prefix
            if prefix:
                siblings = [d for d in os.listdir(parent) if os.path.isdir(os.path.join(parent, d))]
                next_num = len(siblings) + 1
                new_name = f"{next_num:02d} {new_name}"
            dst = os.path.join(parent, new_name)
            import shutil
            shutil.copytree(path, dst)
            messagebox.showinfo("Duplicated", f"Created copy:\n{new_name}")
            self._refresh_tree()
        except Exception as e:
            messagebox.showerror("Duplicate failed", f"{type(e).__name__}: {e}")

    def _duplicate_player(self, path):
        """Duplicate a player file in the same directory with an incremented name."""
        try:
            parent = os.path.dirname(path)
            data = read_kv(path)
            name = (data.get("Name") or data.get("Import Player") or "Player").strip()
            new_name = deduplicate_name(name, parent)
            # Update the Name field in the copy
            data["Name"] = new_name
            # Figure out position from filename
            fname = os.path.basename(path)
            pos = parse_position_from_filename(fname)
            if pos and pos in VALID_POSITIONS:
                new_fname = f"{pos} - {new_name}.txt"
            else:
                new_fname = new_name + ".txt"
            dst = os.path.join(parent, new_fname)
            order = GOALIE_FIELD_ORDER if "Goalie" in fname else PLAYER_FIELD_ORDER
            write_kv(dst, data, order=order)
            # Also mirror to library
            os.makedirs(PLAYER_LIBRARY_DIR, exist_ok=True)
            lib_name = deduplicate_name(new_name, PLAYER_LIBRARY_DIR)
            write_kv(os.path.join(PLAYER_LIBRARY_DIR, lib_name + ".txt"), data, order=order)
            messagebox.showinfo("Duplicated", f"Created copy: {new_name}")
            self._refresh_tree()
        except Exception as e:
            messagebox.showerror("Duplicate failed", f"{type(e).__name__}: {e}")

    def _copy_team_to_library(self, path):
        """Copy an entire team folder from a campaign into the shared team library."""
        try:
            name = os.path.basename(path)
            # Strip numeric prefix (e.g. "01 Vancouver" → "Vancouver")
            m = re.match(r"^\d+\s+(.+)$", name)
            base = m.group(1) if m else name
            safe = re.sub(r'[<>:"/\\|?*]', '_', base).strip()
            os.makedirs(TEAM_LIBRARY_DIR, exist_ok=True)
            dst = os.path.join(TEAM_LIBRARY_DIR, safe)
            if os.path.exists(dst):
                if not messagebox.askyesno("Overwrite",
                    f"'{safe}' already exists in the team library. Overwrite?"):
                    return
                import shutil
                shutil.rmtree(dst)
            import shutil
            shutil.copytree(path, dst)
            messagebox.showinfo("Copied",
                f"Team '{base}' (+ all players) copied to library.\n{dst}")
            self._refresh_tree()
        except Exception as e:
            messagebox.showerror("Copy failed", f"{type(e).__name__}: {e}")

    def _copy_player_to_library(self, path):
        """Copy a player file from a campaign into the shared library."""
        try:
            data = read_kv(path)
            name = (data.get("Name") or data.get("Import Player") or "").strip()
            if not name:
                messagebox.showwarning("No name",
                    "Can't copy — the player file has no Name or Import Player field.")
                return
            safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
            os.makedirs(PLAYER_LIBRARY_DIR, exist_ok=True)
            dst = os.path.join(PLAYER_LIBRARY_DIR, safe + ".txt")
            if os.path.exists(dst):
                if not messagebox.askyesno("Overwrite",
                    f"'{safe}.txt' already exists in the library. Overwrite?"):
                    return
            import shutil
            shutil.copy2(path, dst)
            messagebox.showinfo("Copied",
                f"'{name}' copied to library.\n{dst}")
            self._refresh_tree()
        except Exception as e:
            messagebox.showerror("Copy failed", f"{type(e).__name__}: {e}")

    def _delete_from_tree(self, path, kind):
        name = os.path.basename(path)
        if not messagebox.askyesno("Delete",
            f"Permanently delete this {kind}?\n\n{name}\n\n"
            f"This cannot be undone."):
            return
        try:
            if os.path.isdir(path):
                import shutil
                shutil.rmtree(path)
            else:
                os.remove(path)
        except Exception as e:
            messagebox.showerror("Delete failed", f"{type(e).__name__}: {e}")
            return
        self._refresh_tree()
        self._refresh_active_picker()

    def _set_active_campaign(self):
        name = self._active_var.get().strip()
        if not name:
            messagebox.showwarning("No selection", "Pick a campaign first.")
            return
        try:
            write_active_campaign(name)
        except Exception as e:
            messagebox.showerror("Couldn't write active.txt", f"{type(e).__name__}: {e}")
            return
        self._active_label.configure(text=f"Currently active: {name}")
        messagebox.showinfo("Active campaign set",
            f"active.txt now points to:\n  {name}\n\n"
            f"Restart the game (or hit Ctrl+R in BepInEx console if reloadable) "
            f"to load this campaign.")

    def _refresh_active_picker(self):
        """Re-scan campaign folders + reread active.txt — useful after creating a new campaign."""
        choices = [c for c in list_campaigns() if c != LIBRARY_SOURCE]
        choices += ["default"]
        self._active_combo["values"] = choices
        cur = read_active_campaign()
        if cur:
            self._active_var.set(cur)
            self._active_label.configure(text=f"Currently active: {cur}")
        else:
            self._active_label.configure(text="Currently active: (none — uses base game)")

    def _close_clicked_tab(self, event):
        """Close the tab under the mouse pointer (middle/right click)."""
        try:
            clicked = self.notebook.identify(event.x, event.y)
            if not clicked: return
            idx = self.notebook.index(f"@{event.x},{event.y}")
            if idx == 0: return  # Home — don't close
            tab = self.notebook.nametowidget(self.notebook.tabs()[idx])
            if not confirm_tab_close(self, tab): return
            self.notebook.forget(idx)
            tab.destroy()
        except Exception: pass

    def _check_tab_close_click(self, event):
        """If the user left-clicked the rightmost '×' region of a tab label, close it.

        ttk.Notebook doesn't expose per-tab bounding boxes reliably, so we
        find the right edge of the clicked tab by walking the cursor right
        until identify() reports a DIFFERENT tab index (or nothing). That
        pixel is the tab's right edge; if the click was within the last
        22px of that edge, the × was hit and we close the tab.
        """
        try:
            element = self.notebook.identify(event.x, event.y)
            if "label" not in str(element): return
            idx = self.notebook.index(f"@{event.x},{event.y}")
            if idx == 0: return  # Home — never close
            tab_id = self.notebook.tabs()[idx]
            text = self.notebook.tab(tab_id, "text")
            if not text.endswith("×"): return

            # Walk right until we leave the clicked tab; record that x.
            right_edge = event.x
            limit = event.x + 300  # safety cap (tab label width)
            x = event.x + 1
            while x < limit:
                try:
                    probe_idx = self.notebook.index(f"@{x},{event.y}")
                    if probe_idx != idx: break
                    right_edge = x
                    x += 2
                except Exception:
                    break

            if event.x >= right_edge - 22:
                tab = self.notebook.nametowidget(tab_id)
                if not confirm_tab_close(self, tab):
                    return "break"
                self.notebook.forget(idx)
                tab.destroy()
                return "break"  # suppress the native tab-select on close
        except Exception: pass

    def _close_current_tab(self):
        """Close the currently-selected tab (unless it's Home)."""
        try:
            cur = self.notebook.select()
            if not cur: return
            idx = self.notebook.index(cur)
            if idx == 0: return  # Home — don't close
            tab = self.notebook.nametowidget(cur)
            if not confirm_tab_close(self, tab): return
            self.notebook.forget(cur)
            tab.destroy()
        except Exception: pass

    def new_player(self):
        open_player_editor(is_goalie=False)

    def new_goalie(self):
        open_player_editor(is_goalie=True)

    def new_team(self):
        # Name-first: prompt, create folder in team library, then open editor.
        name = _prompt_string("New Team", "Team name (e.g. 'Vancouver Canucks'):")
        if not name: return
        safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
        if not safe:
            messagebox.showwarning("Invalid name",
                "Team name can't be empty or only special characters.")
            return
        team_dir = os.path.join(TEAM_LIBRARY_DIR, safe)
        if os.path.exists(team_dir):
            if not messagebox.askyesno("Already exists",
                f"Team '{safe}' already exists in the library. Open it?"):
                return
        else:
            os.makedirs(os.path.join(team_dir, "players"), exist_ok=True)
            with open(os.path.join(team_dir, "team.txt"), "w", encoding="utf-8") as f:
                f.write(f"Team Name               = {name}\n")
        open_team_editor(team_dir)

    def new_campaign(self):
        # Name-first: prompt, create folder, then open editor.
        name = _prompt_string("New Campaign", "Campaign name (e.g. 'Winter 2024'):")
        if not name: return
        safe = re.sub(r'[<>:"/\\|?*]', '_', name).strip()
        if not safe:
            messagebox.showwarning("Invalid name",
                "Campaign name can't be empty or only special characters.")
            return
        campaign_dir = os.path.join(CAMPAIGNS_DIR, safe)
        if os.path.exists(campaign_dir):
            if not messagebox.askyesno("Already exists",
                f"Campaign '{safe}' already exists. Open it?"):
                return
        else:
            os.makedirs(os.path.join(campaign_dir, "teams"), exist_ok=True)
            with open(os.path.join(campaign_dir, "campaign.txt"), "w", encoding="utf-8") as f:
                f.write("# Campaign Settings\n")
                f.write("Act Sequence             = 1, 2, 3\n")
                f.write("Use Player Teams         = no\n")
            # Copy player_teams/ from Example Campaign (Defense/Speedy/Basic/Trios
            # + draft pool) so the user has every player team ready to edit.
            _seed_player_teams_from_example(campaign_dir)
        open_campaign_editor(campaign_dir)

    # ===== Community (Dropbox-backed) =====
    def browse_community(self):
        """List campaigns from the community Dropbox folder and download any."""
        import threading, tempfile
        dlg = tk.Toplevel(self)
        dlg.title("Community Campaigns")
        _fit_geometry(dlg, 720, 460)
        dlg.transient(self)

        tk.Label(dlg, text="Community Campaigns", font=("", 12, "bold"),
                 anchor="w").pack(fill="x", padx=12, pady=(10, 2))
        status = tk.Label(dlg, text="Loading campaign list…", fg="#666", anchor="w")
        status.pack(fill="x", padx=12, pady=(0, 6))

        # Pack the button column FIRST so it reserves its width — otherwise
        # the tree's expand=True steals everything and buttons clip off the
        # right edge on smaller windows.
        row = ttk.Frame(dlg, width=170)
        row.pack(side="right", fill="y", padx=12, pady=4)
        row.pack_propagate(False)

        cols = ("name", "size", "modified")
        tree_frame = ttk.Frame(dlg)
        tree_frame.pack(side="left", fill="both", expand=True, padx=(12, 0), pady=4)
        tree = ttk.Treeview(tree_frame, columns=cols, show="headings", selectmode="browse")
        tree.heading("name", text="Campaign")
        tree.heading("size", text="Size")
        tree.heading("modified", text="Uploaded")
        tree.column("name", width=300, anchor="w")
        tree.column("size", width=80, anchor="e")
        tree.column("modified", width=150, anchor="w")
        sb = ttk.Scrollbar(tree_frame, orient="vertical", command=tree.yview)
        tree.configure(yscrollcommand=sb.set)
        sb.pack(side="right", fill="y")
        tree.pack(side="left", fill="both", expand=True)

        path_by_iid = {}

        def _fmt_bytes(n):
            if n >= 1024*1024: return f"{n/(1024*1024):.2f} MB"
            if n >= 1024: return f"{n/1024:.0f} KB"
            return f"{n} B"

        def _refresh():
            status.config(text="Loading…")
            tree.delete(*tree.get_children())
            path_by_iid.clear()
            def _worker():
                try:
                    items = _community_list()
                except Exception as e:
                    self.after(0, lambda: status.config(
                        text=f"Failed to load: {e}", fg="#c00"))
                    return
                def _ui():
                    for it in items:
                        iid = tree.insert("", "end", values=(
                            it["name"], _fmt_bytes(it.get("size", 0)),
                            it.get("modified", "")[:19].replace("T", " ")))
                        path_by_iid[iid] = it["path"]
                    status.config(
                        text=(f"{len(items)} campaign(s) available" if items else
                              "No campaigns uploaded yet — be the first!"),
                        fg="#666")
                self.after(0, _ui)
            threading.Thread(target=_worker, daemon=True).start()

        def _download_selected():
            sel = tree.selection()
            if not sel: return
            iid = sel[0]
            path = path_by_iid.get(iid)
            if not path: return
            name = tree.item(iid, "values")[0]
            status.config(text=f"Downloading {name}…")
            tmp = os.path.join(tempfile.gettempdir(), "t2t_" + os.path.basename(path))
            def _worker():
                try:
                    _community_download(path, tmp)
                    top = _extract_campaign_zip(tmp, CAMPAIGNS_DIR)
                    try: os.remove(tmp)
                    except Exception: pass
                    self.after(0, lambda: self._on_community_installed(top, dlg))
                except FileExistsError as e:
                    target = str(e)
                    self.after(0, lambda: self._prompt_overwrite_install(tmp, target, dlg))
                except Exception as e:
                    self.after(0, lambda: messagebox.showerror("Download failed",
                                                                str(e), parent=dlg))
            threading.Thread(target=_worker, daemon=True).start()

        ttk.Button(row, text="↻ Refresh", command=_refresh, width=14).pack(pady=4)
        ttk.Button(row, text="⬇ Download + Install",
                   command=_download_selected, width=20).pack(pady=4)
        ttk.Button(row, text="Close", command=dlg.destroy, width=14).pack(pady=(20, 4))

        _refresh()

    def _on_community_installed(self, folder_name, parent_dlg=None):
        messagebox.showinfo("Installed",
            f"Campaign '{folder_name}' installed into:\n{CAMPAIGNS_DIR}\n\n"
            f"Use Active campaign → Set Active to play it.",
            parent=parent_dlg or self)
        try: self._refresh_tree()
        except Exception: pass
        try: self._refresh_active_picker()
        except Exception: pass

    def _prompt_overwrite_install(self, zip_path, existing_dir, parent_dlg):
        base = os.path.basename(existing_dir)
        ans = messagebox.askyesnocancel(
            "Campaign already exists",
            f"A campaign named '{base}' already exists.\n\n"
            f"Yes = rename the download (adds suffix)\n"
            f"No  = overwrite the existing campaign\n"
            f"Cancel = abort",
            parent=parent_dlg)
        if ans is None: return
        import zipfile, shutil
        if ans is False:
            # Overwrite
            try: shutil.rmtree(existing_dir)
            except Exception as e:
                messagebox.showerror("Overwrite failed", str(e), parent=parent_dlg)
                return
            try:
                top = _extract_campaign_zip(zip_path, CAMPAIGNS_DIR)
                self._on_community_installed(top, parent_dlg)
            except Exception as e:
                messagebox.showerror("Install failed", str(e), parent=parent_dlg)
        else:
            # Rename the top folder inside zip as we extract
            i = 2
            while os.path.exists(existing_dir + f" ({i})"): i += 1
            new_name = base + f" ({i})"
            new_dir = os.path.join(CAMPAIGNS_DIR, new_name)
            try:
                with zipfile.ZipFile(zip_path, "r") as zf:
                    for m in zf.infolist():
                        parts = m.filename.split("/", 1)
                        rel = parts[1] if len(parts) > 1 else ""
                        if m.is_dir():
                            os.makedirs(os.path.join(new_dir, rel), exist_ok=True)
                        elif rel:
                            full = os.path.join(new_dir, rel)
                            os.makedirs(os.path.dirname(full), exist_ok=True)
                            with zf.open(m) as src, open(full, "wb") as dst:
                                dst.write(src.read())
                self._on_community_installed(new_name, parent_dlg)
            except Exception as e:
                messagebox.showerror("Install failed", str(e), parent=parent_dlg)

    def share_community(self):
        """Zip the active campaign (or prompt to pick one) and upload."""
        import threading, tempfile, json as _json
        camp = read_active_campaign()
        if not camp or camp == "default":
            camps = [c for c in list_campaigns() if c != LIBRARY_SOURCE]
            if not camps:
                messagebox.showwarning("No campaign",
                    "No campaigns to share yet. Create one first.")
                return
            camp = _ask_pick("Share campaign",
                              "Pick a campaign to upload to the community folder:",
                              camps, parent=self)
            if not camp: return

        camp_dir = os.path.join(CAMPAIGNS_DIR, camp)
        if not os.path.isdir(camp_dir):
            messagebox.showerror("Not found",
                f"Campaign folder '{camp_dir}' doesn't exist.")
            return

        # Collect submitter info
        from tkinter.simpledialog import askstring
        author = askstring("Your name",
                            "Your name or handle (shown on the uploaded file):",
                            parent=self) or "Anonymous"
        description = askstring("Description",
                                  "One-line description (optional):",
                                  parent=self) or ""

        meta = _json.dumps({"author": author, "description": description,
                             "campaign_name": camp})

        dlg = tk.Toplevel(self)
        dlg.title("Uploading to community")
        _fit_geometry(dlg, 420, 180)
        dlg.transient(self)
        tk.Label(dlg, text=f"Sharing {camp}", font=("", 11, "bold"),
                 anchor="w").pack(fill="x", padx=12, pady=(14, 2))
        status = tk.Label(dlg, text="Zipping…", anchor="w")
        status.pack(fill="x", padx=12, pady=(4, 2))
        pb = ttk.Progressbar(dlg, orient="horizontal", mode="indeterminate",
                              maximum=100)
        pb.pack(fill="x", padx=12, pady=4)
        pb.start(15)
        ttk.Button(dlg, text="Close", command=dlg.destroy, width=12).pack(pady=10)

        def _worker():
            try:
                tmp_zip = os.path.join(tempfile.gettempdir(),
                                        f"t2t_{camp.replace(' ', '_')}.zip")
                _zip_campaign(camp_dir, tmp_zip)
                self.after(0, lambda: status.config(
                    text=f"Uploading {os.path.getsize(tmp_zip)//1024:,} KB…"))
                display = f"{camp} - {author}"
                path = _community_upload(tmp_zip, display_name=display,
                                          meta_json=meta)
                try: os.remove(tmp_zip)
                except Exception: pass
                def _done():
                    try: pb.stop(); dlg.destroy()
                    except Exception: pass
                    messagebox.showinfo("Uploaded",
                        f"Uploaded successfully!\n\nPath: {path}\n\n"
                        f"Anyone browsing the community folder will see it.",
                        parent=self)
                self.after(0, _done)
            except Exception as e:
                def _err():
                    try: pb.stop(); dlg.destroy()
                    except Exception: pass
                    messagebox.showerror("Upload failed", str(e), parent=self)
                self.after(0, _err)

        threading.Thread(target=_worker, daemon=True).start()

    def import_campaign_from_file(self):
        """Install a campaign zip picked from disk (useful for direct shares)."""
        from tkinter.filedialog import askopenfilename
        path = askopenfilename(
            title="Pick a campaign zip",
            filetypes=[("Campaign zip", "*.zip *.t2tcampaign"), ("All files", "*.*")])
        if not path: return
        try:
            top = _extract_campaign_zip(path, CAMPAIGNS_DIR)
            self._on_community_installed(top, self)
        except FileExistsError as e:
            self._prompt_overwrite_install(path, str(e), self)
        except Exception as e:
            messagebox.showerror("Install failed", str(e))

    def import_game_team(self):
        """Pick a game team by name and create a library reference for it."""
        names = get_game_team_names()
        if not names:
            messagebox.showinfo("Run the game first",
                "Team list hasn't been generated yet.\n\n"
                "Launch Tape to Tape once with the mod installed — it automatically\n"
                "dumps all teams + players to your library.\n\n"
                "Then come back here and this button will show every team.")
            return
        name = _ask_pick("Import Game Team",
                          "Pick a team to add to your library.\n"
                          "The game will resolve all players + stats on next launch:",
                          names, parent=self)
        if not name: return
        try:
            path = import_game_team_to_library(name)
        except Exception as e:
            messagebox.showerror("Import failed", f"{type(e).__name__}: {e}")
            return
        messagebox.showinfo("Imported",
            f"'{name}' added to your library.\n\n{path}\n\n"
            f"Launch the game once to populate all player stats + skins.\n"
            f"Then reopen the editor to customize.")
        self._refresh_tree()

    def edit_campaign(self):
        items = [c for c in list_campaigns() if c != LIBRARY_SOURCE]
        if not items:
            messagebox.showinfo("No campaigns", "No campaigns found. Create one first.")
            return
        name = _ask_pick("Edit Campaign", "Pick a campaign to edit:", items, parent=self)
        if name:
            open_campaign_editor(os.path.join(CAMPAIGNS_DIR, name))

    def edit_team(self):
        def on_pick(camp, team):
            if camp == LIBRARY_SOURCE:
                path = resolve_library_team_dir(team) or os.path.join(TEAM_LIBRARY_DIR, team)
                path = auto_copy_to_library(path, is_team=True)
                if path != resolve_library_team_dir(team):
                    self._refresh_tree()
            else:
                path = os.path.join(CAMPAIGNS_DIR, camp, "teams", team)
            open_team_editor(path)
        open_team_browser(None, on_pick=on_pick)

    def edit_player(self):
        def on_pick(camp, team, filename):
            if camp == LIBRARY_SOURCE:
                path = resolve_library_player_path(filename) or os.path.join(PLAYER_LIBRARY_DIR, filename)
                path = auto_copy_to_library(path)
                self._refresh_tree()
            else:
                path = os.path.join(CAMPAIGNS_DIR, camp, "teams", team, "players", filename)
            open_player_editor(path)
        open_player_browser(on_pick)


def main():
    app = MainMenu()
    app.mainloop()


if __name__ == "__main__":
    main()
