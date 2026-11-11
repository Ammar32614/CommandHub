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

## Planner tab

**Planner**, below Study in the sidebar, holds classes, deadlines, exams and events.

- **Week and month views.** Each day shows what's planned. Under it is how long you actually studied that day according to Stint, coloured by course, so plan and reality sit side by side.
- **Classes can repeat weekly,** with an optional end date.
- **Coming up** lists the next three weeks with anything overdue at the top in red. Tick a deadline off right there.
- **Add or edit** in a popup with a mini calendar. Dates and times can be typed naturally ("fri", "20 nov", "2pm"). Pick a course from your Stint courses so items get that course's colour.
- **Up next** is a Home tile showing the next few items. Click its corner button to open the Planner.

## Money tab

**Money** tracks spending against a monthly budget.

- **Log a payment in seconds.** Type an amount ("450", "1,200", "1.5k"), pick a category, add a note if you like, and press Enter. The date defaults to today; "yesterday", "mon" or "5 nov" also work, and always mean the most recent one.
- **Budget.** Set a monthly budget, plus optional limits per category, under **Budgets**. The top of the page shows what's left and how much that allows per day for the rest of the month. Over-budget categories turn red.
- **Where it went** shows each category's share. **Every day** charts each day's spending. **Insights** names your biggest category, your daily average, the change against last month and your biggest single spend.
- **This month** lists every payment and income. Hover a row to delete it; the toast offers Undo.
- The currency symbol defaults to your region's and can be changed under Budgets.

## Journal tab

**Journal** keeps one entry per day.

- **Write and pick a mood** (Awful to Great). It saves as you type. Each day offers a different prompt if you're stuck.
- **That day** sits beside the entry: what you studied according to Stint, what was in the Planner, and what you spent.
- **#tags** in your writing become chips you can click to find every day with that tag. Search finds any word.
- **Mood calendar** colours each day of the month by mood; click a day to open it. **On this day** shows what you wrote a week, a month and a year ago.
- Your streak counts days in a row with something written.

## Install and updates

Download `CommandHub-win-Setup.exe` from the latest GitHub release and run it. It installs for your account only, so no admin rights are needed.

Updates happen inside the app; you never reinstall:

- The app checks GitHub about 15 seconds after it starts, then every 4 hours. You can also press **Settings → Check for updates**.
- A new version downloads in the background. When it's ready, **Update ready → Restart to update** appears in the sidebar.
- Restarting saves everything, copies `data.json` to `%AppData%\CommandHub\backups\` (the newest five copies are kept), swaps in the new version, and reopens.

**Your data is safe across updates.** The app is installed in `%LocalAppData%\CommandHub`, and that folder is all an update replaces. Your tiles, notes, settings and layout live separately in `%AppData%\CommandHub`. Uninstalling doesn't delete them either.

### Releasing a new version

1. Bump `<Version>` in `CommandHub.csproj`.
2. Commit, then tag and push:

   ```bash
   git tag v1.0.1
   git push origin v1.0.1
   ```

GitHub Actions (`.github/workflows/release.yml`) tests, builds, packs with Velopack (including a small delta update from the previous version) and publishes the release. Installed copies pick it up on their next check.

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
| `Home/` | The After Hours header and shared page helpers |
| `Study/`, `Planner/`, `Money/`, `Journal/` | One folder per sidebar tab: the page plus its pure logic (tested) |
| `docs/superpowers/` | The design spec and build plan |
