# PutDoc — Blazor Server App

**PutDoc**, a Blazor Server app that edits a `.putDoc` container (HTML snippets only, filtered via AngleSoft).

## Features
- Left **Index** (Leaf/Page tree), right stack: **HTML Editor** over **WorkPane**.
- File-backed storage at `<PutDocRootPath>/.putDoc` (JSON structure).
- WorkPane buttons: **＋ / Delete / Clone / Copy / ↑ / ↓**.
- AngleSoft filter placeholder removes `<script>` and inline `on*=` handlers.

## Quick Start
1. Install .NET 8 SDK.
2. Clone this repo.
3. Set the desired working directory in `appsettings.local.json` (`PutDocRootPath`), or pass via environment/config.
4. See QUICKSTART.md for more info

## Notes
- The starter seeds an `.putDoc` with a Root `Collection` ("Index") and a single `Page` ("Welcome") with one snippet.
- Replace `AngleSoftFilter` with your real filter logic.
- Extend `IndexPanel` with create/move/clone/delete for Leafs/Pages as needed.
- Your browser cache may need to be cleared if putdoc.js changes
