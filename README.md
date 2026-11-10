# Command hub

A personal dashboard for Windows. It has a sidebar of pages, and each page holds a board of tiles you arrange yourself. This is the base version: one Home page with four starter tiles. Pages for your interests (and the tiles that monitor and analyse them) get added on top of it.

It's a sibling to [Stint](../Timer%20app). It has the same look and the same themes, and it's built the same way.

## What's in it

- **Home board** with four starter tiles:
  - **Clock and date**
  - **Quick note** saves as you type.
  - **To-do list** lets you add items with Enter, tick them off, and delete them.
  - **Quick links** open a website, a folder or an app in one click. A broken link shows a message on the tile instead of failing silently.
- **Edit layout** (top right) lets you:
  - drag tiles to a new spot
  - change a tile's size (small, wide, tall, large)
  - add tiles
  - remove tiles, with **Undo** for 6 seconds

  Tiles snap to a grid and never overlap. If you drop a tile somewhere taken, it slides back.
- **Settings** has the six Stint themes (Midnight, Daylight, Paper, Forest, Ocean, Dusk) and **Start with Windows** (off by default). Start with Windows opens the hub minimized when you sign in.

Everything saves automatically.

## Run it

You need the .NET 10 SDK on Windows 10 or 11.

```bash
dotnet run
```

Run the tests:

```bash
dotnet test CommandHub.Tests
```

## Where your data lives

`%AppData%\CommandHub\data.json`. It's one JSON file, and the app is careful with it:

- If the file is corrupt, it's backed up as `data.corrupt-<time>.json` before the app starts fresh.
- If the file is locked by another program, or was written by a newer version, the app won't overwrite it.

To try the app against a throwaway file, set `COMMANDHUB_DATA` to another path.

## Adding a new tile type

Each tile type is self-contained:

1. Make a folder `Tiles/<Name>/` with a class that implements `ITileType`. It needs an id, a display name, an icon glyph, its allowed sizes and default size, and `CreateView`.
2. Store the tile's data in `ctx.Content` (a `JsonObject` only your tile reads). Call `ctx.SaveNow()` after a change, or `ctx.SaveSoon()` while the user is typing.
3. Add one line to `TileRegistry.All`.

The board, saving, edit mode and the Add tile picker pick it up automatically.

## Project layout

| Folder | What's there |
|---|---|
| `Models/` | Saved data: pages, placed tiles, settings |
| `Layout/BoardGrid.cs` | Grid rules: fit, free spot, move, resize, repair, snap |
| `Services/` | Safe saving, themes, start with Windows, window effects, debounced save |
| `Tiles/` | One folder per tile type, plus the registry |
| `Views/` | The board, tile card, tile picker and settings page |
| `docs/superpowers/` | The design spec and build plan |
