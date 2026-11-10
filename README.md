# Command hub

A personal dashboard for Windows. It has a sidebar of pages, and each page holds a board of tiles you arrange yourself. This is the base version: one Home page with four starter tiles. Pages for your interests (and the tiles that monitor and analyse them) get added on top of it.

It's a sibling to [Stint](../Timer%20app). It has the same look and the same themes, and it's built the same way.

## What's in it

- **Home board** with eleven tiles, each with its own accent colour:
  - **Greeting** says good morning, afternoon or evening, with a new line each day.
  - **Weather** shows now and the next three days for a city you type. It uses [Open-Meteo](https://open-meteo.com), which is free and needs no account. It sends the city you type once, then that place's coordinates on each refresh. Like any website, Open-Meteo also sees your IP address. It refreshes every 30 minutes and keeps the last report for when you're offline.
  - **Time progress** has rings for how much of the day, week, month and year has gone.
  - **Clock and date**
  - **Study today** reads [Stint](../Timer%20app)'s log to show today's study time against your goal, your streak and the last 7 days. It only reads the log and never changes it.
  - **System pulse** shows live CPU with a small chart, plus memory, disk, battery and uptime.
  - **To-do list** lets you add items with Enter, tick them off, and delete them.
  - **Habits** shows the last 7 days as dots for each habit. Click a dot to tick that day.
  - **Quick note** saves as you type.
  - **Quick links** open a website, a folder or an app in one click.
  - **Countdowns** count the days to the things you're looking forward to.

  Every tile can be added more than once from **Add tile**.
- **Edit layout** (top right) lets you:
  - drag tiles to a new spot
  - change a tile's size (small, wide, tall, large)
  - add tiles
  - remove tiles, with **Undo** for 6 seconds

  Tiles snap to a grid and never overlap. If you drop a tile somewhere taken, it slides back.
- **Settings** has the six Stint themes (Midnight, Daylight, Paper, Forest, Ocean, Dusk) and **Start with Windows** (off by default). Start with Windows opens the hub minimized when you sign in.

Everything saves automatically.

## Study tab

**Study**, below Home in the sidebar, turns Stint's log into analytics:

- **Time studied** for the last 7 days, 30 days or all time, compared with the period before.
- **Daily average**, **goal days**, **streak** and **longest sitting**.
- **Every day:** a bar per day, split by course colour, with your goal as a line. Long ranges switch to weekly bars.
- **By course:** each course's share of your time. Click a course to filter the next list.
- **What you worked on:** each course + task pair (what you typed in Stint's "What are you working on?"), so "Assignment 2h" and "Lecture 8 1h" are separate rows.
- **When you study:** a weekday × hour heatmap and your peak two hours.
- **Insights** in plain words, and your **recent sessions**.

It only reads the log, never changes it, and refreshes when Stint saves.

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
