# Command hub base version: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows desktop app with a page sidebar, a snap-to-grid tile board, four starter tiles, Stint's themes, and an optional start-with-Windows setting.

**Architecture:** A WPF app shaped like Stint. Plain C# models and pure layout logic (`BoardGrid`) sit under a thin UI layer. Each tile type is a self-contained `ITileType` registered in one list. A tile's content is stored as a `JsonObject` that only its own type reads, so the board and the save file never know about specific tiles. Data is saved as one versioned JSON file using Stint's safe-save pattern.

**Tech stack:** C#, WPF, .NET 10 (`net10.0-windows`, SDK 10.0.403 installed), System.Text.Json, and xUnit 2.9.2 (same versions as Stint's test project).

**Spec:** `docs/superpowers/specs/2026-11-10-command-hub-design.md`. Stint lives at `../Timer app` and is referenced as "Stint".

## Global constraints

- Target `net10.0-windows`, with `UseWPF` true, `Nullable` enabled and `ImplicitUsings` enabled. Use `SatelliteResourceLanguages` `en`, as Stint does.
- Name the assembly `CommandHub`, the root namespace `CommandHub`, and the product "Command hub".
- Save to `%AppData%\CommandHub\data.json`.
- Themes: Midnight (the default), Daylight, Paper, Forest, Ocean and Dusk, with exactly Stint's colours.
- Start with Windows is off by default. It uses the HKCU Run key, value name `CommandHub`, and the flag `--minimized`.
- Tile sizes in cells (columns × rows): Small 1×1, Wide 2×1, Tall 1×2 and Large 2×2.
- Tiles never overlap. A move or resize that would overlap **snaps back** (decision made here).
- Saving is automatic. Note text saves 600 ms after typing stops, everything else saves immediately, and anything pending is saved when the app closes.
- Removing a tile always offers **Undo** for 6 seconds (decision made here; no confirm dialog).
- No Stint integration, tray mode, installer, APIs or page creation UI. The model supports more pages, but the base version only ships Home and Settings (decision made here).
- UI text uses sentence case, matching Stint's wording style.

## Review focus

1. **A save file written by a newer version** (`version` > 1, or an unknown tile `typeId`): don't crash, show unknown tiles as a placeholder, and never drop or overwrite them. Pinned in Task 3 (DataStore) and Task 5 (registry).
2. **Stored tile positions that are invalid** (overlapping, outside the 6 columns, or negative, for example from a hand-edited file): repair them on load by moving the offenders to free spots. Pinned in Task 2 (`Normalize`).
3. **A quick link whose target is a typo, a deleted folder or empty**: clicking it shows a short message on the tile and doesn't crash. Pinned in Task 5 (`LinkTarget`).
4. **Closing the app within 600 ms of typing in a note**: the text is still saved. Pinned in Task 7 (`Debouncer.Flush`).
5. **A corrupt or locked data file**: back it up, run on defaults, and never overwrite a file that couldn't be read. Pinned in Task 3 (ported Stint tests).

---

## File structure

```
Command hub/
  .gitignore                      bin/ obj/ *.user .vs/ .superpowers/
  CommandHub.csproj               app project; excludes CommandHub.Tests/** and docs/** like Stint
  App.xaml / App.xaml.cs          startup, data load, ApplyTheme, save on exit
  MainWindow.xaml / .cs           custom title bar, sidebar, page host
  Themes/Theme.xaml               palette keys, brushes, fonts, shared styles (trimmed copy of Stint's)
  Models/TileSize.cs              enum plus cell spans
  Models/PlacedTile.cs            one tile on a board
  Models/Page.cs                  name, icon, tiles
  Models/HubData.cs               root save object, version, settings, CreateDefault
  Layout/BoardGrid.cs             pure placement logic (fit, free spot, move, resize, normalize, snap)
  Services/DataStore.cs           safe JSON load and save (port of Stint's)
  Services/ThemeCatalog.cs        port of Stint's, with accent keys renamed
  Services/StartupManager.cs      port of Stint's
  Services/WindowEffects.cs       straight copy of Stint's
  Services/Debouncer.cs           delayed save with Flush
  Tiles/ITileType.cs              tile type contract plus TileContext
  Tiles/TileRegistry.cs           list of all tile types; lookup with an unknown-type fallback
  Tiles/UnknownTileView.xaml(.cs) placeholder for unrecognized types
  Tiles/Clock/ClockTile.cs, ClockTileView.xaml(.cs)
  Tiles/Note/NoteTile.cs, NoteContent.cs, NoteTileView.xaml(.cs)
  Tiles/Todo/TodoTile.cs, TodoContent.cs, TodoTileView.xaml(.cs)
  Tiles/Links/LinksTile.cs, LinksContent.cs, LinkTarget.cs, LinksTileView.xaml(.cs)
  Views/BoardView.xaml(.cs)       renders a page's tiles, edit mode, drag, resize, add, remove with Undo
  Views/TileFrame.xaml(.cs)       card chrome around each tile view
  Views/AddTilePicker.xaml(.cs)   tile type picker sheet
  Views/SettingsPage.xaml(.cs)    theme choice and start with Windows
  CommandHub.Tests/               xUnit project referencing ..\CommandHub.csproj
```

---

### Task 1: Scaffold, git, and models

**Files:**
- Create: `.gitignore`, `CommandHub.csproj`, `App.xaml`, `App.xaml.cs` (an empty window for now), `MainWindow.xaml(.cs)` (empty)
- Create: `Models/TileSize.cs`, `Models/PlacedTile.cs`, `Models/Page.cs`, `Models/HubData.cs`
- Create: `CommandHub.Tests/CommandHub.Tests.csproj` (copy of Stint's test csproj, pointing at `..\CommandHub.csproj`), `CommandHub.Tests/GlobalUsings.cs`, `CommandHub.Tests/ModelTests.cs`

**Interfaces:**
- Produces:
  - `enum TileSize { Small, Wide, Tall, Large }`
  - `static class TileSizes { static (int Cols, int Rows) Span(TileSize s); }`
  - `class PlacedTile { Guid Id; string TypeId; int Column; int Row; TileSize Size; JsonObject Content = new(); }` (mutable properties, `System.Text.Json.Nodes.JsonObject`)
  - `class Page { Guid Id; string Name; string Icon; List<PlacedTile> Tiles = []; }`, where `Icon` is a Segoe Fluent Icons glyph string
  - `class AppSettings { string Theme = "Midnight"; }`
  - `class HubData { const int CurrentVersion = 1; int Version = CurrentVersion; List<Page> Pages = []; AppSettings Settings = new(); static HubData CreateDefault(); }`
  - Tile type ids (string constants used everywhere): `"clock"`, `"note"`, `"todo"`, `"links"`

- [ ] **Step 1:** Run `git init` in the project root and add `.gitignore`. Create both csproj files and empty `App` and `MainWindow` files. Commit the spec and plan docs too.
- [ ] **Step 2: Write failing tests in `ModelTests.cs`**

```csharp
[Theory]
[InlineData(TileSize.Small, 1, 1)] [InlineData(TileSize.Wide, 2, 1)]
[InlineData(TileSize.Tall, 1, 2)] [InlineData(TileSize.Large, 2, 2)]
public void Span_MatchesSpec(TileSize s, int cols, int rows) =>
    Assert.Equal((cols, rows), TileSizes.Span(s));

[Fact]
public void CreateDefault_HomeHasOneOfEachStarterTile()
{
    var d = HubData.CreateDefault();
    var home = Assert.Single(d.Pages);
    Assert.Equal("Home", home.Name);
    Assert.Equal(new[] { "clock", "note", "todo", "links" }, home.Tiles.Select(t => t.TypeId));
    Assert.Equal(1, d.Version);
    Assert.Equal("Midnight", d.Settings.Theme);
}
```

- [ ] **Step 3:** Run `dotnet test CommandHub.Tests`. Expected: FAIL (the types don't exist).
- [ ] **Step 4:** Implement the models. The default Home layout uses (column, row, size): clock (0,0,Large), note (2,0,Wide), todo (4,0,Tall), links (2,1,Wide). Give each tile a new Guid.
- [ ] **Step 5:** Run `dotnet test CommandHub.Tests`. Expected: PASS. Also run `dotnet build`, which must succeed with no warnings about nullability in the new files.
- [ ] **Step 6:** Commit with the message `chore: scaffold Command hub, models and test project`.

### Task 2: BoardGrid placement logic

**Files:**
- Create: `Layout/BoardGrid.cs`
- Test: `CommandHub.Tests/BoardGridTests.cs`

**Interfaces:**
- Consumes: `PlacedTile`, `TileSize`, `TileSizes.Span` (Task 1)
- Produces (`static class BoardGrid`):
  - `const int Columns = 6; const double CellWidth = 160; const double CellHeight = 140; const double Gap = 12;`
  - `bool Fits(IReadOnlyList<PlacedTile> tiles, int col, int row, TileSize size, Guid? ignoreId = null)`
  - `(int Col, int Row) FindFreeSpot(IReadOnlyList<PlacedTile> tiles, TileSize size)`: scans row by row from (0,0) and returns the first spot that fits. Rows are unbounded.
  - `bool TryMove(IReadOnlyList<PlacedTile> tiles, Guid id, int col, int row)`: changes nothing and returns false if the tile doesn't fit there.
  - `bool TryResize(IReadOnlyList<PlacedTile> tiles, Guid id, TileSize size)`: same rule, keeping the top-left corner fixed.
  - `void Normalize(List<PlacedTile> tiles)`: keeps tiles in list order, and any tile that doesn't fit given the tiles before it is moved to `FindFreeSpot`.
  - `(int Col, int Row) Snap(double x, double y)`: converts a pixel position (top-left of a dragged tile, relative to the board) to the nearest cell using the cell pitch `CellWidth+Gap` / `CellHeight+Gap`. Clamps to `col >= 0`, `row >= 0`, `col <= Columns-1`.
  - `Rect CellRect(int col, int row, TileSize size)`: the pixel rectangle for rendering, where width is `cols*CellWidth + (cols-1)*Gap`.

- [ ] **Step 1: Write failing tests**

```csharp
static PlacedTile T(int c, int r, TileSize s) => new() { Id = Guid.NewGuid(), TypeId = "note", Column = c, Row = r, Size = s };

[Fact] public void Fits_RejectsOverlap() =>
    Assert.False(BoardGrid.Fits([T(0,0,TileSize.Large)], 1, 1, TileSize.Small));
[Fact] public void Fits_RejectsPastLastColumn() =>
    Assert.False(BoardGrid.Fits([], 5, 0, TileSize.Wide));
[Fact] public void Fits_RejectsNegative() =>
    Assert.False(BoardGrid.Fits([], -1, 0, TileSize.Small));
[Fact] public void Fits_IgnoresSelf()
{ var a = T(0,0,TileSize.Large); Assert.True(BoardGrid.Fits([a], 0, 0, TileSize.Large, a.Id)); }
[Fact] public void FindFreeSpot_FirstGapRowMajor() =>
    Assert.Equal((2, 0), BoardGrid.FindFreeSpot([T(0,0,TileSize.Large)], TileSize.Wide));
[Fact] public void FindFreeSpot_FullRowGoesBelow() =>
    Assert.Equal((0, 1), BoardGrid.FindFreeSpot([T(0,0,TileSize.Wide), T(2,0,TileSize.Wide), T(4,0,TileSize.Wide)], TileSize.Small));
[Fact] public void TryMove_OntoOtherTile_SnapsBack()
{ var a = T(0,0,TileSize.Small); var b = T(1,0,TileSize.Small);
  Assert.False(BoardGrid.TryMove([a, b], a.Id, 1, 0)); Assert.Equal((0,0), (a.Column, a.Row)); }
[Fact] public void TryMove_ToFreeCell_Moves()
{ var a = T(0,0,TileSize.Small); Assert.True(BoardGrid.TryMove([a], a.Id, 3, 2)); Assert.Equal((3,2), (a.Column, a.Row)); }
[Fact] public void TryResize_BlockedKeepsSize()
{ var a = T(0,0,TileSize.Small); var b = T(1,0,TileSize.Small);
  Assert.False(BoardGrid.TryResize([a, b], a.Id, TileSize.Wide)); Assert.Equal(TileSize.Small, a.Size); }
[Fact] public void Normalize_RepairsOverlapAndOutOfBounds()
{ var a = T(0,0,TileSize.Large); var b = T(1,1,TileSize.Small); var c = T(9,-3,TileSize.Wide);
  var list = new List<PlacedTile> { a, b, c }; BoardGrid.Normalize(list);
  Assert.Equal((0,0), (a.Column, a.Row));
  foreach (var t in list) Assert.True(BoardGrid.Fits(list, t.Column, t.Row, t.Size, t.Id)); }
[Fact] public void Snap_RoundsToNearestCell() =>
    Assert.Equal((1, 1), BoardGrid.Snap(160 + 12 - 40, 140 + 12 + 30));
[Fact] public void Snap_ClampsToBoard() =>
    Assert.Equal((5, 0), BoardGrid.Snap(5000, -200));
```

- [ ] **Step 2:** Run `dotnet test CommandHub.Tests --filter BoardGridTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `BoardGrid` with an occupied-cell check (cells covered by `[Column, Column+cols) × [Row, Row+rows)`).
- [ ] **Step 4:** Run `dotnet test CommandHub.Tests --filter BoardGridTests`. Expected: PASS. Then run the full suite.
- [ ] **Step 5:** Commit with the message `feat: board grid placement logic`.

### Task 3: DataStore (safe, versioned save)

**Files:**
- Create: `Services/DataStore.cs` (port of Stint's `Services/DataStore.cs`)
- Test: `CommandHub.Tests/DataStoreTests.cs`

**Interfaces:**
- Consumes: `HubData` (Task 1), `BoardGrid.Normalize` (Task 2)
- Produces: `class DataStore(string path) { static string DefaultPath; string FilePath; bool ReadFailed; HubData Load(); bool Save(HubData data); }`

Behaviour beyond Stint's version:
- After a successful load, if `Pages` is empty, replace it with `HubData.CreateDefault().Pages`. Call `BoardGrid.Normalize` on every page's tiles.
- If the loaded `Version` is greater than `HubData.CurrentVersion`, set `ReadFailed = true` (so the newer file is never overwritten) and return the loaded data as is. Unknown fields and unknown tile `typeId`s round-trip untouched because tile content is a `JsonObject`.
- Use the same serializer options as Stint: camelCase, indented, and enums as strings.

- [ ] **Step 1: Write failing tests.** Port Stint's tests for a missing file, round-trip, corrupt file backup, and refusing to save after a failed read, adapted to `HubData`. Add these tests:

```csharp
[Fact] public void SaveThenLoad_RoundTripsTileContentAndUnknownTypes()
{ var s = new DataStore(PathIn); var d = HubData.CreateDefault();
  d.Pages[0].Tiles.Add(new PlacedTile { Id = Guid.NewGuid(), TypeId = "weather", Column = 0, Row = 3,
      Size = TileSize.Small, Content = new JsonObject { ["city"] = "Lahore" } });
  d.Pages[0].Tiles[1].Content["text"] = "hello";
  s.Save(d); var t = s.Load().Pages[0].Tiles;
  Assert.Equal("hello", (string?)t[1].Content["text"]);
  Assert.Equal("Lahore", (string?)t.Single(x => x.TypeId == "weather").Content["city"]); }

[Fact] public void Load_NewerVersion_RefusesToOverwrite()
{ Directory.CreateDirectory(_dir); File.WriteAllText(PathIn, """{"version":99,"pages":[]}""");
  var s = new DataStore(PathIn); s.Load();
  Assert.False(s.Save(HubData.CreateDefault()));
  Assert.Contains("99", File.ReadAllText(PathIn)); }

[Fact] public void Load_OverlappingTiles_AreRepaired()
{ /* write a file with two Large tiles both at (0,0) */
  var tiles = new DataStore(PathIn).Load().Pages[0].Tiles;
  Assert.NotEqual((tiles[0].Column, tiles[0].Row), (tiles[1].Column, tiles[1].Row)); }
```

- [ ] **Step 2:** Run `dotnet test CommandHub.Tests --filter DataStoreTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `DataStore`. `DefaultPath` is `%AppData%\CommandHub\data.json`, with an atomic `.tmp` write followed by a move, as in Stint.
- [ ] **Step 4:** Run `dotnet test CommandHub.Tests --filter DataStoreTests`. Expected: PASS.
- [ ] **Step 5:** Commit with the message `feat: safe versioned data store`.

### Task 4: Themes, startup, window effects

**Files:**
- Create: `Services/ThemeCatalog.cs`, `Services/StartupManager.cs`, `Services/WindowEffects.cs`, `Themes/Theme.xaml`
- Test: `CommandHub.Tests/ThemeCatalogTests.cs`, `CommandHub.Tests/StartupManagerTests.cs` (port of Stint's)

**Interfaces:**
- Produces:
  - `ThemeCatalog.Keys` = Stint's keys with timer-specific names renamed: `FocusColor→AccentColor`, `BreakColor→Accent2Color`, `TimerColor→Accent3Color`, `StopwatchColor→Accent4Color`. Everything else and every hex value stays unchanged.
  - `ThemeCatalog.All`, `ThemeCatalog.Get(string? name)` (falls back to Midnight), `ThemeDef`, as in Stint.
  - `StartupManager` as in Stint, with `ValueName = "CommandHub"`.
  - `Theme.xaml`: Stint's palette, brushes, fonts, `FocusRing`, `IconButton` and the other button and text styles it needs. Rename `FocusBrush` to `AccentBrush`, and replace `ModeAccent` with `{DynamicResource AccentBrush}`. Drop timer-only styles.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Catalog_HasStintThemesInOrder() =>
    Assert.Equal(new[] { "Midnight", "Daylight", "Paper", "Forest", "Ocean", "Dusk" }, ThemeCatalog.All.Select(t => t.Name));
[Fact] public void Midnight_AccentMatchesStintFocus() =>
    Assert.Equal("#F5B14C", ThemeCatalog.Get("Midnight")["AccentColor"]);
[Fact] public void Get_UnknownName_FallsBackToMidnight() =>
    Assert.Equal("Midnight", ThemeCatalog.Get("nope").Name);
[Fact] public void EveryTheme_DefinesEveryKey() =>
    Assert.All(ThemeCatalog.All, t => Assert.All(ThemeCatalog.Keys, k => Assert.False(string.IsNullOrEmpty(t[k]))));
```

Port Stint's `StartupManagerTests` with a test registry key path and the `CommandHub` value name.

- [ ] **Step 2:** Run `dotnet test CommandHub.Tests`. Expected: the new tests FAIL.
- [ ] **Step 3:** Port the three services and `Theme.xaml`.
- [ ] **Step 4:** Run `dotnet test CommandHub.Tests`. Expected: all PASS.
- [ ] **Step 5:** Commit with the message `feat: themes, start with Windows, window effects (ported from Stint)`.

### Task 5: Tile framework and tile content logic

**Files:**
- Create: `Tiles/ITileType.cs`, `Tiles/TileRegistry.cs`, `Tiles/UnknownTileView.xaml(.cs)`
- Create: `Tiles/Note/NoteContent.cs`, `Tiles/Todo/TodoContent.cs`, `Tiles/Links/LinksContent.cs`, `Tiles/Links/LinkTarget.cs`
- Create: `Tiles/Clock/ClockTile.cs`, `Tiles/Note/NoteTile.cs`, `Tiles/Todo/TodoTile.cs`, `Tiles/Links/LinksTile.cs`. Their `CreateView` returns a temporary `TextBlock` until Task 7.
- Test: `CommandHub.Tests/TileContentTests.cs`, `CommandHub.Tests/TileRegistryTests.cs`

**Interfaces:**
- Consumes: `PlacedTile`, `TileSize` (Task 1)
- Produces:
  - `interface ITileType { string TypeId; string DisplayName; string Icon; IReadOnlyList<TileSize> AllowedSizes; TileSize DefaultSize; FrameworkElement CreateView(TileContext ctx); }`
  - `sealed class TileContext(PlacedTile tile, Action saveNow, Action saveSoon)`, where `saveSoon` is the debounced save and `saveNow` is the immediate save
  - `static class TileRegistry { IReadOnlyList<ITileType> All; ITileType? Find(string typeId); }`. The board shows `UnknownTileView` ("This tile needs a newer version of Command hub") when `Find` returns null.
  - Allowed and default sizes: clock {Small, Wide, Large} with default Large; note, todo and links all four sizes, with defaults Wide, Tall and Wide.
  - `NoteContent { static string Read(JsonObject c); static void Write(JsonObject c, string text); }` (key `"text"`)
  - `record TodoItem(string Text, bool Done)`; `TodoContent { static List<TodoItem> Read(JsonObject c); static void Write(JsonObject c, IEnumerable<TodoItem> items); static bool TryAdd(List<TodoItem> items, string text); }` (key `"items"`; `TryAdd` trims and rejects blank text)
  - `record QuickLink(string Name, string Target)`; `LinksContent { Read / Write }` (key `"links"`)
  - `enum LinkKind { Web, Folder, File, Invalid }`; `static class LinkTarget { LinkKind Classify(string target); bool TryOpen(string target, out string error); }`. An http/https URL is Web, an existing directory is Folder, and an existing file (including .exe and .lnk) is File. Everything else, including empty, is Invalid. `TryOpen` uses `Process.Start` with `UseShellExecute = true` and catches `Win32Exception`.
  - Every `Read` returns an empty value (never throws) when the key is missing or has the wrong JSON type.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Note_ReadMissing_IsEmpty() => Assert.Equal("", NoteContent.Read(new JsonObject()));
[Fact] public void Note_ReadWrongType_IsEmpty() => Assert.Equal("", NoteContent.Read(new JsonObject { ["text"] = 5 }));
[Fact] public void Todo_RoundTrip()
{ var c = new JsonObject(); TodoContent.Write(c, [new("Milk", false), new("Gym", true)]);
  Assert.Equal([new TodoItem("Milk", false), new TodoItem("Gym", true)], TodoContent.Read(c)); }
[Fact] public void Todo_TryAdd_TrimsAndRejectsBlank()
{ var l = new List<TodoItem>(); Assert.False(TodoContent.TryAdd(l, "   ")); Assert.True(TodoContent.TryAdd(l, "  Call bank "));
  Assert.Equal("Call bank", l.Single().Text); }
[Fact] public void Todo_ReadGarbage_IsEmpty() => Assert.Empty(TodoContent.Read(new JsonObject { ["items"] = "x" }));
[Fact] public void Links_RoundTrip() { /* write two QuickLinks, read back equal */ }
[Theory]
[InlineData("https://example.com", LinkKind.Web)] [InlineData("", LinkKind.Invalid)]
[InlineData("C:\\definitely\\missing\\folder", LinkKind.Invalid)] [InlineData("ftp://x", LinkKind.Invalid)]
public void LinkTarget_Classify(string t, LinkKind k) => Assert.Equal(k, LinkTarget.Classify(t));
[Fact] public void LinkTarget_ExistingFolder_IsFolder() => Assert.Equal(LinkKind.Folder, LinkTarget.Classify(Path.GetTempPath()));
[Fact] public void LinkTarget_TryOpenInvalid_ReturnsErrorWithoutThrowing()
{ Assert.False(LinkTarget.TryOpen("C:\\missing\\x.exe", out var err)); Assert.False(string.IsNullOrWhiteSpace(err)); }
[Fact] public void Registry_HasFourStarterTypes() =>
    Assert.Equal(new[] { "clock", "note", "todo", "links" }, TileRegistry.All.Select(t => t.TypeId));
[Fact] public void Registry_UnknownType_IsNull() => Assert.Null(TileRegistry.Find("weather"));
[Fact] public void Registry_DefaultSizeIsAllowed() =>
    Assert.All(TileRegistry.All, t => Assert.Contains(t.DefaultSize, t.AllowedSizes));
```

- [ ] **Step 2:** Run `dotnet test CommandHub.Tests`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement the tile framework and content classes as specified.
- [ ] **Step 4:** Run `dotnet test CommandHub.Tests`. Expected: all PASS.
- [ ] **Step 5:** Commit with the message `feat: tile type framework and starter tile content logic`.

### Task 6: App shell (window, sidebar, settings)

**Files:**
- Modify: `App.xaml`, `App.xaml.cs`, `MainWindow.xaml(.cs)`
- Create: `Views/SettingsPage.xaml(.cs)`

**Interfaces:**
- Consumes: `DataStore`, `ThemeCatalog`, `StartupManager`, `WindowEffects`, `HubData`
- Produces:
  - `App.Data` (HubData), `App.Store` (DataStore)
  - `App.SaveNow()`: shows the same non-blocking warning as Stint when `Save` returns false
  - `App.ApplyTheme(string? name)` and `event Action? ThemeChanged`, as in Stint
  - `MainWindow.ShowPage(Page page)`; `MainWindow.ShowSettings()`

Behaviour:
- The window is resizable, with a default size of 1180×760 and a minimum of 720×480. It uses Stint's custom title bar (an accent dot, "Command hub", and minimize, maximize and close buttons) and Stint's rounded corners.
- The sidebar is 200 px wide. It shows one row per `Page` (icon and name), with the selected row on the `Raised` background. Settings is pinned at the bottom.
- `--minimized` starts the window minimized (used by start with Windows).
- The Settings page has a theme list (name and blurb, as in Stint's settings) that applies and saves at once, and a "Start with Windows" toggle that is off by default and reads its state from `StartupManager.IsEnabled`.
- The page area shows a placeholder until Task 7.

- [ ] **Step 1:** Implement the shell.
- [ ] **Step 2:** Run `dotnet run`. Verify the window opens with Midnight and the Home row selected. Switching to Daylight recolours everything at once. Close and reopen: the theme is kept. Turning on Start with Windows creates the `HKCU\...\Run\CommandHub` value, and turning it off removes it.
- [ ] **Step 3:** Run `dotnet test CommandHub.Tests`. Expected: all PASS.
- [ ] **Step 4:** Commit with the message `feat: app shell with sidebar, settings and theming`.

### Task 7: Board rendering, tile views, autosave

**Files:**
- Create: `Views/BoardView.xaml(.cs)`, `Views/TileFrame.xaml(.cs)`, `Services/Debouncer.cs`
- Create: `Tiles/Clock/ClockTileView.xaml(.cs)`, `Tiles/Note/NoteTileView.xaml(.cs)`, `Tiles/Todo/TodoTileView.xaml(.cs)`, `Tiles/Links/LinksTileView.xaml(.cs)`
- Modify: the four tile classes' `CreateView`, `MainWindow` (host `BoardView`), and `App.xaml.cs` (flush on exit)
- Test: `CommandHub.Tests/DebouncerTests.cs`

**Interfaces:**
- Consumes: `BoardGrid.CellRect`, `TileRegistry`, `TileContext`, the content classes, and `App.SaveNow`
- Produces:
  - `sealed class Debouncer(TimeSpan delay, Action action) { void Request(); void Flush(); bool IsPending; }`, built on a `DispatcherTimer`. `Flush` runs the action once if one is pending, and otherwise does nothing.
  - `App.SaveSoon()` (a 600 ms Debouncer around `SaveNow`). `App.OnExit` and `MainWindow.Closing` call `Flush`.
  - `BoardView(Page page)` places a `TileFrame` per tile on a `Canvas` sized to fit, using `BoardGrid.CellRect`, inside a `ScrollViewer` that scrolls both ways (the layout never reflows).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Flush_RunsPendingActionOnce()
{ int n = 0; var d = new Debouncer(TimeSpan.FromSeconds(10), () => n++);
  d.Request(); d.Request(); d.Flush(); d.Flush(); Assert.Equal(1, n); Assert.False(d.IsPending); }
[Fact] public void Flush_WithNothingPending_DoesNothing()
{ int n = 0; new Debouncer(TimeSpan.FromSeconds(10), () => n++).Flush(); Assert.Equal(0, n); }
```

(A `DispatcherTimer` can be created on the xUnit thread. `Flush` stops it, so no dispatcher loop is needed.)

- [ ] **Step 2:** Run `dotnet test CommandHub.Tests --filter DebouncerTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `Debouncer`, then run the same tests. Expected: PASS.
- [ ] **Step 4:** Implement `TileFrame`, which is a rounded `Surface` card (corner radius 14, padding 14) with a small muted title (the tile type's `DisplayName`, hidden on the clock).
- [ ] **Step 5:** Implement the four views:
  - **Clock:** time in `DisplayFont` Light, sized to the tile, with the weekday and date in `Muted`. A 1-second `DispatcherTimer` updates it, and stops when the view unloads.
  - **Note:** a borderless multi-line `TextBox` that wraps text. Each change writes `NoteContent` and calls `saveSoon`.
  - **To-do:** a list of check box rows with a delete button on hover, plus an "Add a to-do" box (Enter adds via `TryAdd`). Any change writes the content and calls `saveNow`.
  - **Links:** a wrap panel of buttons (Fluent glyph by `LinkKind`, plus the name). Clicking calls `TryOpen`, and on failure shows the error in small `Danger` text on the tile for 4 s. An edit button (pencil) toggles a small inline list of name and target rows with add and remove, saved via `saveNow`.
- [ ] **Step 6:** Run `dotnet run`. Verify first launch shows the four tiles in the default layout. Type in the note and close the window within half a second: the text is there after reopening. Add a to-do, tick it, delete it, and reopen: the state is kept. Add a link to `https://example.com` and to a folder; both open. A link to `C:\nope` shows an error and doesn't crash. Shrink the window: the board scrolls and tiles don't move.
- [ ] **Step 7:** Run `dotnet test CommandHub.Tests`. Expected: all PASS.
- [ ] **Step 8:** Commit with the message `feat: tile board with clock, note, to-do and links tiles`.

### Task 8: Edit layout mode

**Files:**
- Modify: `Views/BoardView.xaml(.cs)`, `Views/TileFrame.xaml(.cs)`
- Create: `Views/AddTilePicker.xaml(.cs)`

**Interfaces:**
- Consumes: `BoardGrid.Fits / FindFreeSpot / TryMove / TryResize / Snap`, `TileRegistry.All`, `App.SaveNow`

Behaviour:
- An "Edit layout" toggle sits at the top-right of the board area (it reads "Done" while on). When it's off, tiles can't be dragged and there are no edit controls.
- When it's on, each `TileFrame` shows a remove button (×) and a resize button in its top-right corner, and the tile content stops taking input.
- **Drag:** press on a tile and move. The tile follows the mouse. A cell-sized outline appears at `BoardGrid.Snap(...)`, using `AccentBrush` when `Fits` and `Danger` when not. On release, `TryMove` runs; if it fails, the tile animates back. Save on success.
- **Resize:** the resize button moves to the next size in the type's `AllowedSizes` order that `TryResize` accepts. If none fit, it stays put and briefly flashes the outline in `Danger`. Save on success.
- **Add:** a dashed "+ Add tile" cell at `FindFreeSpot(tiles, Small)` opens `AddTilePicker`, a sheet listing `TileRegistry.All` (icon and name). Picking one adds it at `FindFreeSpot(tiles, DefaultSize)` with empty content, then saves.
- **Remove:** removes the tile at once, saves, and shows a toast at the bottom of the board ("Tile removed" with an Undo button) for 6 s. Undo puts it back at the old spot if it still fits, and otherwise at `FindFreeSpot`, then saves.
- The Canvas grows by one empty row in edit mode so tiles can be dragged below the last row.

- [ ] **Step 1:** Implement the edit mode as specified.
- [ ] **Step 2:** Run `dotnet run` and verify:
  - Edit mode off: dragging does nothing.
  - Dropping a tile onto another snaps it back.
  - Dropping it on empty space moves it, and the move survives a restart.
  - Resize cycles through the allowed sizes and skips sizes that would overlap. The clock never becomes Tall.
  - Adding two notes gives each its own text.
  - Removing a tile and pressing Undo brings it back with its content.
  - Removing a tile and waiting 6 s means it's gone after a restart.
- [ ] **Step 3:** Run `dotnet test CommandHub.Tests`. Expected: all PASS.
- [ ] **Step 4:** Commit with the message `feat: edit layout mode with drag, resize, add and undoable remove`.

### Task 9: Final check and README

**Files:**
- Create: `README.md` (what it is, how to run it with `dotnet run`, where data lives, and how to add a new tile type: implement `ITileType` in `Tiles/<Name>/` and add it to `TileRegistry.All`)

- [ ] **Step 1:** Run `dotnet build -c Release` with no warnings, then run `dotnet test CommandHub.Tests`, which must all PASS.
- [ ] **Step 2:** Check that the Review focus items 1 to 5 above each have a passing test, and repeat the Task 7 and Task 8 manual checks once on the final build.
- [ ] **Step 3:** Commit with the message `docs: README for Command hub`.
