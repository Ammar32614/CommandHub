# Command hub: base version design

Date: 2026-11-10
Status: approved by the owner on 2026-11-10

## Intent

A personal "command hub" desktop app for Windows. In the long run it will show and monitor all of the owner's interests in one place, with analysis of each one (charts, trends and summaries). This document covers only the **base**: a simple, solid foundation that new interests are added to over time.

Why start small: the owner wants to build up gradually. The base has to prove the structure first (pages, tiles, layout, saving and theming), so each later interest is "add a page and some tiles" and not a rewrite.

It is a sibling to the owner's existing app **Stint** (a study timer, at `../Timer app`). It should look and feel like Stint and be built the same way.

## Goals (base version)

1. A Windows desktop app with a **sidebar of pages** on the left and a **free-form tile board** on each page.
2. **Home** is the only content page at first, plus **Settings**. The sidebar is built so new pages (one per interest, such as Finance or Fitness) can be added later without reworking it.
3. Tiles come in **a few fixed sizes** (small, wide, tall, large) and sit on a snap-to grid.
4. An **Edit layout** mode lets the owner drag tiles, resize them (between the fixed sizes), add tiles from a picker and remove tiles. Outside edit mode the board is locked, so tiles can't be moved by accident.
5. The layout (which tiles, their positions and sizes) and each tile's content are **saved and restored** on the next launch.
6. Four starter tile types:
   - **Clock and date**: big time, plus weekday and date.
   - **Quick note**: a sticky note the owner types into, saved automatically.
   - **To-do list**: a short checklist where the owner can add items, tick them off and delete them.
   - **Quick links**: buttons that open a website, a folder or an app (for example Stint). Each link has a name and a target, and the owner can add, edit and remove links.
7. **Themes like Stint's.** It uses the same theme set (Midnight, Daylight, Paper, Forest, Ocean, Dusk), switchable in Settings, with Midnight as the default.
8. **Start with Windows** is an option in Settings, **off by default**, and works the same way as Stint's.

## Non-goals (for this version)

- No connection to Stint. A Stint tile (today's study time and streak, read from Stint's log) is a likely early addition, but not now.
- No interest pages, live data feeds, external APIs or analysis yet. The architecture must leave room for them, but none get built.
- No installer or GitHub releases yet. It runs from source while it's being built, and a Stint-style installer and releases come later.
- No system-tray mode or hide-on-close.
- No cloud sync, accounts or mobile access.

## Anti-goals (would count as failure even if it works)

- **Adding a new tile type or page means touching lots of existing code.** The whole point of the base is that growth is cheap. A new tile type should be a self-contained addition plus a registration line.
- **It looks or behaves unlike Stint**, for example with a different visual language or a different settings style.
- **Losing the owner's notes, to-dos or layout** because of a bad or locked save file.
- **A cluttered or fiddly board.** Free placement should snap neatly. No pixel-level positioning, overlapping tiles or tiny resize handles.

## Constraints and decisions made with the owner

| Decision | Choice |
|---|---|
| Platform | Windows desktop app, same stack as Stint: WPF on .NET 10 (`net10.0-windows`), C# |
| Layout | Sidebar of pages (from mockup B) plus a free-form tile board with mixed sizes (from mockup C) |
| Visual style | Stint family: dark, rounded, warm accent, same theme catalogue |
| First content | Home with clock and date, quick note, to-do list and quick links |
| Stint integration | Not in this version |
| Distribution | Run from source for now |
| Start with Windows | Optional setting, off by default |

## What the approved mockups showed

The owner picked from rough mockups in the brainstorming chat. They aren't saved as files, so here is what they showed:

- **Window**: a Stint-style custom title bar ("Command hub" with a small accent dot before the name). Unlike Stint, the window is **resizable**, with a default size of about a laptop half-screen or larger (builder's call).
- **Sidebar** (left, about 180 to 220px wide): the app name at the top, then one row per page with an icon and a label. The selected page has a raised, rounded background (Stint's RaisedColor), like Stint's selected tab. Settings is pinned to the bottom. Future pages appear as more rows.
- **Board** (the rest of the window): tiles on a grid with even gaps (about 8 to 12px). Each tile is a rounded card on Stint's SurfaceColor, with no heavy borders, a small muted title or label, and its content below. The accent colour is used sparingly, for numbers, chart bars and highlights.
- **Clock tile** (large, 2×2): the time in big light-weight digits, centred, with the weekday and date in muted text below.
- **Add tile**: a dashed-outline cell with "+ Add tile", visible in edit mode.
- **Edit layout**: a toggle at the top-right of the board area. In edit mode, tiles show a remove control and a resize handle, and can be dragged.

Exact spacing, typography and icons are the builder's call, as long as they match Stint's look.

## Reuse from Stint

Stint (`../Timer app`) already solves several of these problems, and its patterns should be copied (not linked as a shared library, at least for now):

- **Themes**: `Services/ThemeCatalog.cs` and `Themes/Theme.xaml` (named colour keys swapped at runtime). The hub uses the same theme names and colours. Stint's timer-specific keys (FocusColor, BreakColor, TimerColor, StopwatchColor) can become general accent keys.
- **Saving**: `Services/DataStore.cs` stores JSON in `%AppData%\Stint\data.json`, never throws on a bad file, backs up a corrupt file before starting fresh, and never overwrites a file it couldn't read. The hub does the same in `%AppData%\CommandHub\`.
- **Start with Windows**: `Services/StartupManager.cs` uses the per-user HKCU Run key with no admin rights, under its own value name.
- **Window chrome**: Stint uses a custom title bar and window effects (`Services/WindowEffects.cs`). The hub matches that look.
- **Tests**: Stint has an xUnit test project for its services (`TimerApp.Tests`). The hub gets a matching test project covering its non-UI logic: the layout model, the save store, grid snapping and placement, and startup.

## Behaviour details

- **Grid**: the board is a fixed number of columns. Tiles snap to cells, and the sizes in grid cells are small 1×1, wide 2×1, tall 1×2 and large 2×2. The column count and cell size are the builder's call, picked to look good at a typical laptop window size.
- **First launch**: Home is pre-filled with one of each starter tile in a sensible arrangement, so the app never opens to a blank screen.
- **Duplicate tiles**: a tile type can be placed more than once (for example two notes), and each tile keeps its own content. *(Claude's call.)*
- **Adding a tile**: in edit mode, an "Add tile" control opens a picker of tile types. The new tile goes in the first free spot that fits.
- **Placement conflicts**: tiles never overlap. If a move or resize would overlap another tile, it doesn't happen (the tile snaps back), or the builder can choose to push the other tile to the next free spot. Either is fine, as long as it's predictable.
- **Window resizing**: the board keeps its layout. If the window gets too narrow for the columns, the board scrolls rather than reflowing tiles.
- **Saving**: changes save automatically (note text after a short pause in typing, everything else immediately). There is no Save button.
- **Removing a tile with content** (a note, to-dos or links) asks for confirmation, or offers an Undo the way Stint's reset does.

## Architecture direction (for growth)

- **Tile types are plugins in spirit.** Each tile type is one self-contained unit (its view, its settings and content, and a registration entry saying its name, icon, allowed sizes and default size). The board knows nothing about specific tile types.
- **Pages are data.** A page is a name, an icon and a list of placed tiles. Home is just the first page. Adding interest pages later is mostly a UI for creating pages, and the model already supports them. *(Builder's call: whether the base version already lets the owner add or rename pages, or that waits for the first interest. The model must support it either way.)*
- **Save format is versioned**, so later versions can migrate older files.

## Left to the builder

- Project and namespace naming (working name "Command hub"; the owner may rename the app later).
- Exact grid dimensions, spacing, and the drag and resize interaction details, within the rules above.
- Icons, the sidebar's collapsed or expanded behaviour, and window size defaults.
- Whether to use MVVM strictly or code-behind like Stint, as long as the tile model and the save logic are testable without the UI.
- Git setup (this folder isn't a git repo yet, while Stint is).
