# PutDoc — Blazor Server Starter

A minimal starter for **PutDoc**, a Blazor Server app that edits a `.putDoc` container (HTML snippets only, filtered via AngleSoft).

## Features
- Left **Index** (Leaf/Page tree), right stack: **HTML Editor** over **WorkPane**.
- File-backed storage at `<PutDocRootPath>/.putDoc` (JSON structure).
- WorkPane buttons: **＋ / Delete / Clone / Copy / ↑ / ↓**.
- AngleSoft filter placeholder removes `<script>` and inline `on*=` handlers.

## Quick Start
1. Install .NET 8 SDK.
2. Unzip this repo.
3. Set the desired working directory in `appsettings.json` (`PutDocRootPath`), or pass via environment/config.
4. From `src/PutDoc` run:
   ```bash
   dotnet restore
   dotnet run
   ```
5. Browse to http://localhost:5000 (or shown URL).

## Notes
- The starter seeds an `.putDoc` with a Root `Leaf` ("Index") and a single `Page` ("Welcome") with one snippet.
- Replace `AngleSoftFilter` with your real filter logic.
- Extend `IndexPanel` with create/move/clone/delete for Leafs/Pages as needed.
