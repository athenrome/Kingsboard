# Kingsboard

Multiplayer turn-based board game. An authoritative .NET gRPC server owns all game state; a Unity client displays it.

```
Kingsboard Server/
  KingsboardServer/   ASP.NET Core gRPC server (net10.0, Serilog)
  ProtoLibrary/       Shared gRPC contract (netstandard2.1)
    Protos/           .proto files — the only place protos are added or edited
    Generated/        C# generated from Protos/ on build. TRACKED in git; also a Unity local package
Kingsboard Client/
  Kingsboard Client/  Unity 6000.6.2f1 project (URP)
  SharedLibrary/      Models shared by server and client (netstandard2.1, C# 9)
    SharedModels/     The model source; also the Unity local package com.kingsboard.sharedmodels
```

## Changing this file

- **Ask before adding a rule.** Never add a new rule or convention to this file without the user's approval. Propose the exact wording and where it goes, and wait for a clear yes. This applies even to rules that seem obvious or that follow from work just done.

## Build & run

```bash
dotnet build "Kingsboard Server/KingsboardServer/KingsboardServer.csproj"
dotnet run --project "Kingsboard Server/KingsboardServer" --launch-profile https   # https://localhost:7116
```

- The server has no MVC controllers. Visual Studio injects its Endpoints Explorer hosting startup (`Microsoft.WebTools.ApiEndpointDiscovery`), which uses MVC and logs "No action descriptors found". Every launch profile in `launchSettings.json` excludes it with `ASPNETCORE_HOSTINGSTARTUPEXCLUDEASSEMBLIES`; keep that when adding profiles.
- If the server is already running, its `bin/` is locked and builds fail with MSB3021. Build to another folder with `-o <dir>` instead of killing the user's server.
- The Unity client can't be compiled from the CLI. Check that its scripts compile with a throwaway `netstandard2.1` csproj with `LangVersion` 9 that compiles `Assets/Systems/**/*.cs`, `ProtoLibrary/Generated/*.cs` and `SharedLibrary/SharedModels/**/*.cs`, referencing `<Unity Editor>/Data/Managed/UnityEngine/*.dll` and `Assets/Plugins/Grpc/*.dll`.
  - **⚠ REVIEW:** possibly obsolete. The prefab section below already uses the `unity` CLI (`unity command recompile`) against a live editor, so "can't be compiled from the CLI" only holds when the editor is closed. Say which to prefer when.
- There are no automated tests yet. Verify server changes in two ways:
  - **Logic:** a throwaway console project in the scratchpad that references `KingsboardServer.csproj` and calls the services directly. Don't add it to the repo.
  - **End to end:** run the Unity client in Play mode against the server on `https://localhost:7116`. If the user's server isn't running, run a copy with `--urls https://localhost:7116`, then stop it afterwards.

## Protos

- To change the contract, edit `Protos/*.proto` and build ProtoLibrary. That regenerates `Generated/`, which the server and Unity (package `com.kingsboard.protos`) share. Never hand-edit `Generated/*.cs`; commit the regenerated files.
- Protos have no protobuf `package`, so one proto refers to another's types by bare name after `import` (e.g. `GrpcPawn.proto` imports `GrpcBoard.proto` and uses `BoardPosition`).
- Each proto uses its own `csharp_namespace` (`GrpcChat`, `GrpcTurnTimer`, `GrpcBoard`, `GrpcPawn`, `GrpcCard`, `GrpcPlayer`).
- A proto with no `service` (today only `GrpcCard.proto`) needs a `<Protobuf Update="Protos\Xxx.proto" GrpcServices="None" />` line in `ProtoLibrary.csproj`, or the build fails looking for a `XxxGrpc.cs` that is never generated.

## Server architecture (`KingsboardServer/`)

The layers are gRPC handler, then Service, then Repository. Everything is a singleton registered in `Program.cs`.

- **`Handlers/`**: gRPC endpoints (`XxxGrpcHandler : XxxService.XxxServiceBase`) that manage the gRPC communication with game clients. They are thin: they translate between proto and domain models, map results to `RpcException` status codes, and contain no game logic.
  - Streaming handlers queue events into a per-client `Channel` and keep a single writer loop, because gRPC forbids concurrent writes to one stream.
  - Stream calls end on a token linked from `context.CancellationToken` and `IHostApplicationLifetime.ApplicationStopping`.
- **`Services/`** (and `Services/Interfaces/`): game logic.
  - Services publish changes as C# events whose handlers must be quick and non-blocking.
  - Services hand out snapshots or clones, never live state.
  - `GameService` runs the game loop: it creates the board and spawns the pawns, then runs the turn loop.
- **`Repositories/`** (and `Repositories/Interfaces/`): in-memory storage behind interfaces. Repositories hold no game rules. Thread-safety and handing out copies is the owning service's job (e.g. `BoardService` locks and clones around `BoardRepository`).
- **`Models/`**: server-only domain types, with subfolders `Board/` (`GameBoard`, `BoardSection`), `Cards/` (`CardDefinition`, `CardActionContext`, `CardActionResult`, `CardSelectResult`), `Chat/`, `Configuration/`, `Enumerations/` and `Players/` (`Player`), and `Turn.cs` at the root. Models the client also uses live in SharedLibrary instead (see below). See the model rule below.
- **`Events/<Category>/`**: event objects, the payloads passed through C# events. They are grouped by category, each with its own namespace: `Events/Board/` (`KingsboardServer.Events.Board`: `TilesChangeEvent`, `BoardViewEvent`), `Events/Pawns/` (`KingsboardServer.Events.Pawns`: `PawnsChangeEvent`) and `Events/Players/` (`KingsboardServer.Events.Players`: `PlayersChangeEvent`). Add a new category folder for a new system. The model rule applies to these too.
- **`Subscriptions/<Category>/`**: subscription objects, which a service returns when a client joins a stream (a session id plus the `ChannelReader` it reads from). They are grouped by category, each with its own namespace: `Subscriptions/Chat/` (`KingsboardServer.Subscriptions.Chat`: `ChatSubscription`) and `Subscriptions/Board/` (`KingsboardServer.Subscriptions.Board`: `BoardViewSubscription`). Name them `XxxSubscription`, keep one type per file, and apply the model rule to them too.
- **No extension classes.** All logic for a model lives in the service that owns it: `BoardService` for boards, `TileService` for tiles, `TurnService` for turns (`GetDuration`, `GetTimeRemaining`, cloning), `PawnService` for pawns. Don't add `XxxExtensions` classes on the server.
- **Settings:** each system has a settings class in `Models/Configuration/` with a `SectionName` constant. It is bound in `Program.cs` with `.Validate(...).ValidateOnStart()`, mirrored in `appsettings.json`, and read through `IOptionsMonitor<T>.CurrentValue`.
- **Other dependencies:** inject `Serilog.ILogger` for logging and `TimeProvider` for time; never call `DateTime.Now`.

## Shared models (`Kingsboard Client/SharedLibrary/`)

- Any model used by both the server and the client lives in `SharedLibrary/SharedModels/` (namespace `Kingsboard.Shared.Models.*`), never as two copies. The server references `SharedLibrary.csproj`; Unity uses `SharedModels/` as the local package `com.kingsboard.sharedmodels`. Today that is `BoardPosition`, `BoardRegion`, `MapTile`, `TileType`, `TurnPhase`, `CardTargetType`, `Card`, `CardSelection`, `Pawn`, `PawnAttributes`, `PawnMovement` and `PawnSpeech`.
- Shared files must compile as C# 9 with no Unity or server dependencies (`SharedLibrary.csproj` pins `LangVersion` 9). Use plain .NET types, e.g. a colour is a `"#RRGGBB"` string.
- The model rule applies. Logic stays in each side's owning service.

## Client architecture (`Assets/Systems/<System>/`)

- `Networking/GrpcConnection` holds the one shared `GrpcChannel` (HTTP/2 via `UnityHttpMessageHandler`, address from `--server=`).
- Each system has a pair of classes:
  - A **`XxxGrpcHandler`** (plain class, `IDisposable`) manages the gRPC communication with the server and is the only code that touches gRPC types. It runs a reconnect loop and raises C# events on the main thread using client-side model classes.
  - A **`XxxController`** (`MonoBehaviour`) owns state and visuals. It lives on its system's prefab (`Board`, `Pawns`, `Chat`), which is placed in the Main scene. Controllers never create themselves.
- **Board (client):** the client holds one `BoardSection` (never the whole board). Tiles keep their whole-board positions, and the block at `BoardPosition` (x, y, z) is centred at local position (x, (z + 0.5) × `LevelHeight`, y): board y maps to world z and the level z to world y. Each `section` event rebuilds the view in full, and `tiles_changed` patches it (removals first, then placements). Updates are ignored if they belong to a different board id or have a version older than the current one. **⚠ REVIEW:** `BoardController` also ignores an update with the *same* version (`version <= Section.Version`); say "not newer than". Load another section with `BoardController.RequestSection(region)` or `PanSection(dx, dy)`.
- Server rules, such as validation and limits, are enforced server-side only. Client copies of limits, such as `ChatController.MaxMessageLength`, are only for UX.

## Board model (3D)

- The board is Width (x) × Depth (y) × Height (z levels). Each (x, y) column is a stack of up to Height tiles, and z = 0 is the bottom.
- Stacks are always solid from z = 0 up. `BoardService` rejects any change that would leave a tile floating, and rejected changes leave nothing changed.
- Server `GameBoard.Tiles` is a flat array indexed by `(z * Depth + y) * Width + x`, with null for empty positions. The proto sends only tiles that exist.
- All board changes go through `BoardService.ApplyChanges`, which applies removals and then placements to a copy, checks that every stack is still solid, then saves, bumps the version and raises the event.
- The public entry points for changes are `PlaceTile`, `RemoveTile`, `SetTiles` and `RemoveTiles`.

### Sections: clients see part of the board

The board, 128 × 128 × 8 by default, is too big to send whole. Each client views one **section** at a time: a `BoardRegion`, which is an origin column plus width × depth, covering every level.

- **Section sizes:** `BoardSettings:DefaultSectionSize` (24) sets the size a client gets when it doesn't ask. `MaxSectionSize` (64) caps every request.
- **Region fitting:** a region hanging over the board's edge is moved back onto it at the same size where possible. Always use the region a section comes back with.
- **Streaming:** `JoinBoardStream(region?)` sends `joined` (a session id and the size limits), then the section, then `tiles_changed` for that section only. `SetBoardSection(session_id, region)` sends a new `section` on the same stream. `GetBoardSection(region)` is a one-off query. No RPC returns the whole board.
- **`BoardViewService`:** owns who is viewing what. It's modelled on `ChatRoomService` (join, set, leave, with a bounded queue per client). A client that falls behind has its backlog replaced by a fresh section.
- **Ordering:** every section snapshot is taken inside `IBoardService.ReadSection(region, callback)`, which runs under the board lock. Board events are raised under the same lock, so each client's queue is always a section followed by exactly the changes after it. Keep it that way: take board-related view state only inside `ReadSection` or the board events, and never hold the view lock while calling into `BoardService` from outside them. That second rule avoids lock-order deadlocks.
- **Testing:** in the Unity client (Play mode), call `BoardController.RequestSection(region)` or `PanSection(dx, dy)` to move.

### BoardPosition

`BoardPosition` (X, Y, Z) represents every position on the board: in the proto and as one shared positional `record` (a class, compared by value) on the server and client. A position may hold a tile or be empty space.

- **Always use it for board positions.** Use `BoardPosition` whenever you check or evaluate a position: methods take a `BoardPosition`, not separate x/y/z ints, and `MapTile` carries one as `Position`. Don't add new APIs that take loose coordinates.
- **Empty space is a first-class position.** `IsEmpty(position)` means "on the board and holds no tile". `GetPlacementPosition(column)` returns the empty position on top of a stack, and a removed tile leaves its position as empty space. A position off the board is neither on the board nor empty, so check with `IsOnBoard`/`Contains`.
- **Column methods ignore Z.** `GetStack`, `GetTopTile` and `GetPlacementPosition` act on the position's column. To change a position's level, use `position with { Z = ... }` (the client also has `BoardService.AtLevel(position, z)`).
- **Ask before you change.** `CanPlaceTile(position)` is true when the position is empty space resting on a tile or the ground. `CanRemoveTile(position)` is true when the position holds a tile with nothing on top of it.
- **Client helpers:** `BoardController.WorldToPosition` turns a world point, such as a raycast hit, into a `BoardPosition`, which may be empty space. `PositionToWorld` goes the other way.

## Turn phases

Every turn runs Start → Upkeep → Planning → Player → Resolution → End. The Player phase lasts until the turn timer runs out.

- **Phase handlers:** a service that has work in a phase implements `ITurnPhaseHandler` (`Phases` plus `RunPhaseAsync`) and is registered in `Program.cs` as an `ITurnPhaseHandler`. `TurnService` runs every handler for a phase and **awaits them all before the next phase begins**, so put a phase's work inside `RunPhaseAsync` and return once it's actually done.
  - **⚠ REVIEW:** incomplete. `TurnService.RunPhaseAsync` awaits the handlers **one at a time**, in registration order, not concurrently, so a slow handler delays every handler after it. State that.
- **Failures:** a handler that throws is logged, and the turn carries on.
- **Events:** `ITurnService.PhaseStarted` fires as each phase begins. The turn timer stream sends it as `TURN_EVENT_TYPE_PHASE_STARTED`, and `TurnState.phase` carries the current phase.
- **Client:** `TurnTimerController` (on the TurnTimer prefab) shows the turn number and phase at the top of the screen. During the Player phase it adds a countdown and a draining bar, which turn red in the last `warningSeconds`. The countdown runs locally from the server's `time_remaining_ms`, so it's smooth and unaffected by clock differences. The Player phase ends when the turn's time runs out, so turn time remaining is the Player time remaining.

## Pawns

Pawns are characters on the board that act by playing action cards (see Cards). All pawn logic is on the server in `PawnService`; the Unity client (`PawnManager` and a `PawnController` per pawn) only displays it. A pawn has an id, a name, a colour (`#RRGGBB`), a `Position`, `Attributes`, `Health`, a `Deck`, a `Hand`, a `SelectedCard` and an optional owner (`OwnerId`/`OwnerName`).

- **Attributes:** Strength, Dexterity, Intelligence, Wisdom and Constitution (`PawnAttributes`), each a random whole number from 1 to 10, rolled at spawn.
- **Stats:** Physique = Dexterity + Strength × 2. `PawnService` works it out when it rolls the attributes and stores it as `PawnAttributes.Physique` (also `PawnAttributes.physique` in the proto). Max health equals Physique (`GetMaxHealth`); `CreatePawn` stores it as `Pawn.MaxHealth` (sent as `max_health`), and pawns spawn at full health.
- **Creating pawns:** `PawnService.CreatePawn` builds one pawn (name, colour, attributes, starting health). `SpawnPawns` only picks spawn positions and calls it.

- **Position:** the empty space directly above its stack's top tile.
- **Spawning:** `GameService` spawns `PawnSettings:Count` pawns on passable ground near the board's front-left corner, inside `SpawnAreaSize`. That's where the default board section is.
- **Turn flow (cards):** Upkeep: each pawn draws `PawnSettings:HandSize` (5) cards from its `Deck` into its `Hand`. Planning: each pawn tries its hand in random order and chooses the first usable card (`SelectedCard`, plus a target if the card needs one); a Move card's path goes in `PlannedPath`, and no two AI pawns pick the same destination. Player: the pawn's owner may change the choice with `SelectCard`. Resolution: pawns play their cards highest Dexterity first (ties random), each against the board and pawns as they are at that moment; cards stay in hand and choices are cleared. End: the hand is shuffled back into the deck.
- **Movement:** the Move card walks towards its target tile, up to **Dexterity** steps (there is no `MoveRange` any more), to the reachable column nearest the target. Step rules (4-way, passable top, `MaxClimb`, never through another pawn) live in `PawnMovementService`, shared by `PawnService` and `MoveCardAction`.
- **One instance:** `PawnService` is both `IPawnService` and the `ITurnPhaseHandler` for Upkeep, Planning, Player, Resolution and End. It tracks the Player phase itself (it can't depend on `ITurnService`, which depends on the phase handlers).
- **Ordering:** `PawnsChanged` is raised under the pawn lock, and `ReadPawns(callback)` subscribes and takes a snapshot together, the same pattern as `BoardService.ReadSection`. Lock order is **player, then pawn, then board**: `PlayersChanged` arrives under the player lock, and `SpawnPawns` takes the player lock (`ReadPlayers`) before the pawn lock. Never call into players or pawns from the board, or into players from inside the pawn lock.
- **gRPC:** `JoinPawnStream` sends a `snapshot`, then `spawned`, `cards_drawn`, `planned`, `card_selected` (one pawn), `resolved` (movements and speeches), `cards_returned` and `owners_changed`. Every pawn is sent; the client shows only those inside its loaded board section. `GetPawnById(id)` returns all of one pawn's details in one call (`INVALID_ARGUMENT` for a malformed id, `NOT_FOUND` for an unknown one); on the client it's `PawnManager.GetPawnById`. `SelectCard(session_id, pawn_id, card_id, target)` returns UNAUTHENTICATED for an unknown session and INVALID_ARGUMENT for malformed ids. `PawnService.SelectCard` returns a `CardSelectResult` (a `Selected` bool plus an error message), and any refusal becomes FAILED_PRECONDITION with that message as the detail.
- **Client:** `PawnManager` (on the Pawns prefab) handles pawns as a group. It receives them from the server, spawns a `Pawn` prefab per new pawn and despawns pawns that are gone, passes each pawn its own plans and moves, and keeps one pawn selected (click a pawn to select it, click empty space to deselect). Each pawn has its own `PawnController` (on the Pawn prefab) that drives its client actions: name and colour, showing its planned path, walking the path when moves resolve, hiding outside the loaded section, and being selected (`Select`/`Deselect`, which shows its `SelectionRing`). Put per-pawn behaviour in `PawnController` and group behaviour in `PawnManager`. `PawnDetailsController` (the PawnDetails prefab, nested in Pawns) shows the selected pawn's name, health, Physique and attributes at the top right, and hides when nothing is selected. It loads a newly selected pawn through `GetPawnById`, then follows streamed updates.
- **Client (cards):** `PawnHandController` (the PawnHand prefab, nested in Pawns) shows the selected pawn's hand at the bottom right, with the chosen card highlighted. Anyone's hand is visible; only our own pawns' cards are clickable, and only in the Player phase. A card that needs a tile calls `PawnManager.BeginTargeting`, and the next click on the board aims it (right-click or Esc cancels). `PawnController.Say` shows a `SpeechBubble`. Pawns speak in Resolution, right before End, so a bubble stays up until the end of the *following* turn, then fades.

## Cards

Pawns act by playing action cards. All card logic is on the server.

- **Definitions:** `CardService` lists every kind of card in code (`CardDefinition`: id, name, description, image id, `CardTargetType`, `CardActionType`). A `Card` in a deck or hand is a copy with its own `Id`; `DefinitionId` says which kind it is. Today: `move`, `say-number`, `say-colour`, `say-hello`.
- **Decks:** `PawnSettings:StartingDeck` lists the definition ids each pawn starts with (empty means the default: 3 Move and 1 of each test card). Decks are shuffled at spawn and stored on the pawn. A drawn card is in `Hand`, not `Deck`, until End returns it.
- **Actions:** each `CardActionType` is carried out by one `ICardAction` (`Services/CardActions/`, registered in `Program.cs` as `ICardAction`). An action works things out and returns a `CardActionResult` (path, position, blocked, speech); `PawnService` applies it. Actions run under the pawn lock, so they must be quick and must not call back into `PawnService`. To add a card, add a `CardDefinition` and, if it needs a new behaviour, a `CardActionType` plus an `ICardAction`.

## Players

- `PlayerService` tracks connected players (kept in the service, like chat participants). `JoinGame(name)` streams `joined` (public `player_id`, secret `session_id`, name, colour); closing the stream leaves the game.
- Each player gets a colour (`#RRGGBB`) from `PlayerService`'s palette: the first no joined player has, reused in turn once all are taken.
- Each player gets up to `PlayerSettings:PawnsPerPlayer` (2) pawns no one controls, in join order; a leaver's pawns go back to no one and are given to players who are short. A pawn's owner is `Pawn.OwnerId`/`OwnerName`/`OwnerColour` (null/empty means it acts on its own). The client shows a controlled pawn in its owner's colour instead of its own (`PawnService.GetColour`). Owned pawns still choose a card in Planning; the owner can override it.
- Requests that act as a player send the secret `session_id`. Only the public `player_id` ever goes out in pawn data.
- **Client:** `PlayerController` (the Player prefab, placed in the Main scene) joins with its prefab name, or `--name=` from the command line, and exposes `Player`/`IsMe`.

## Prefab rule (Unity): every GameObject comes from a prefab

- **Scenes:** never build GameObjects in a scene by hand. Place prefab instances instead.
- **Code:** code that spawns GameObjects instantiates a prefab it references through a `[SerializeField]`. Never use `new GameObject(...)`, `GameObject.CreatePrimitive`, or `AddComponent` to assemble an object at runtime.
- **Configuration:** a prefab's references and child containers are set on the prefab, not found or created in code.
- **Location:** prefabs live in `Assets/Prefabs/<System>/`, named for what they are. Materials they need live in `Assets/Materials/<System>/`.

| Prefab | What it is |
|---|---|
| `Prefabs/Board/Board.prefab` | `BoardController`, with a `Tiles` child that tile blocks are created under |
| `Prefabs/Board/BoardTile.prefab` | One tile block (a cube) |
| `Prefabs/Pawns/Pawns.prefab` | `PawnManager`, with a `Pawn Views` child that pawns are spawned under and nested `PawnDetails` and `PawnHand` |
| `Prefabs/Pawns/PawnDetails.prefab` | `PawnDetailsController` and its `UIDocument` (`Prefabs/Pawns/PawnDetailsPanelSettings.asset`), the selected-pawn panel |
| `Prefabs/Pawns/PawnHand.prefab` | `PawnHandController` and its `UIDocument` (`Prefabs/Pawns/PawnHandPanelSettings.asset`), the selected pawn's hand. Nested in `Pawns`. |
| `Prefabs/Pawns/Pawn.prefab` | One pawn: a `PawnController`, a `Body` capsule, a `Name` label, a nested `PawnPath`, a hidden `SelectionRing` (`Materials/Pawns/PawnSelection.mat`) and a nested, hidden `SpeechBubble`. Its pivot is at its feet. |
| `Prefabs/Pawns/SpeechBubble.prefab` | `SpeechBubbleController`, a `Background` quad (`Materials/Pawns/SpeechBubble.mat`) and a `Text` TextMesh. Starts hidden; nested in `Pawn`. |
| `Prefabs/Pawns/PawnPath.prefab` | `LineRenderer` for a planned path (`Materials/Pawns/PawnPath.mat`), nested in `Pawn`. It draws in world space, so it stays put while the pawn walks. |
| `Prefabs/Chat/Chat.prefab` | `ChatController` and its `UIDocument` (`Prefabs/Chat/ChatPanelSettings.asset`) |
| `Prefabs/TurnTimer/TurnTimer.prefab` | `TurnTimerController` and its `UIDocument` (`Prefabs/TurnTimer/TurnTimerPanelSettings.asset`) |
| `Prefabs/Players/Player.prefab` | `PlayerController`: this client's player |
| `Prefabs/Camera/Camera.prefab` | The main camera (tagged `MainCamera`) with its `CameraController`: WASD/arrows pan (Shift is faster), the mouse wheel zooms, and it frames each new section the focus point isn't in |

**Making or changing prefabs:**
- **Use the live editor.** When the Unity Editor is open, change prefabs and scenes through it (`unity command ...`, e.g. `eval_file` with fully-qualified type names, since `eval` code can't have `using` lines). Don't hand-edit `.prefab` or `.unity` YAML.
- **Recompile first.** After adding a component script, recompile (`unity command recompile --focus true`, since Unity won't finish a reload in the background) before putting the component on a prefab.
- **Rename or move scripts with `AssetDatabase.MoveAsset`.** Do it through the editor (e.g. via `eval`), never by moving `.cs`/`.meta` files on disk. A script moved on disk can be silently left out of compilation.
- **Screenshots:** `capture_game_view` saves under `Assets/`, so delete the file afterwards. Pass `--source screen` in Play mode to include UI Toolkit overlays (chat, turn timer); the default camera capture leaves them out. Bring the editor to the front (`unity command editor_focus`) before Play mode checks, because a background editor runs too slowly for `eval` to respond.

## Model rule: models hold data only

This applies to model files on the server (`Models/**`, `Events/**` and `Subscriptions/**`), in `SharedLibrary/SharedModels/**`, and on the client (data classes such as `BoardSection`, `TurnInfo` and `ChatLine`).

**Allowed in a model file:**
- Auto-properties, optionally with a default value (`public int Width { get; set; } = 16;`)
- Positional records
- Enums
- `const` values
- XML doc comments

**Not allowed in a model file:**
- Methods
- Computed properties (`=>` bodies or getter logic)
- Constructors with any body
- Static instances or factories
- Validation
- Operators
- Private fields that back behaviour

Server: put logic in the service that owns the model, such as `BoardService` for boards (bounds, validation, cloning, versioning), `TileService` for tiles (`IsPassable`) and `TurnService` for turns (duration, time remaining, cloning). There are no extension classes on the server. If a model has no owning service yet, give it one. Repositories stay plain storage with no rules. Client: put logic in a static `XxxService` class next to the model, e.g. `Assets/Systems/Board/BoardService.cs` (`BoardService.GetTile(section, position)`). There are no extension classes on the client either. This applies even to small conveniences.

When you find logic in an existing model, move it to the owning service rather than adding to it.

## Conventions

- Brace style is Allman. Fields are `_camelCase`. Write `/// <summary>` docs on public members and interfaces in plain language.
- **Event objects end in `Event`.** Any type whose job is to describe something that happened gets the suffix. That includes:
  - payloads passed through C# events, e.g. `TilesChangeEvent`, `PawnsChangeEvent` and `BoardViewEvent`. These live in `KingsboardServer/Events/<Category>/`, not `Models/`;
  - proto messages sent as stream events, e.g. `ChatEvent`, `BoardEvent`, `TilesChangedEvent`, `PawnsResolvedEvent` and `BoardJoinedEvent`. These stay in their service's `.proto` file, alongside the service that streams them.

  Domain objects that an event merely carries keep their own names: `Turn`, `GameBoard`, `ChatMessage`
## Documentation rule

Every public type and member gets a descriptive `/// <summary>`. That covers:
- Classes, records, interfaces and enums, plus each enum value
- Properties, constants and events
- Constructors and methods, including `override`s of the gRPC base classes

Positional records also need a `<param>` for each property.

Always put `<summary>` and `</summary>` on their own lines, never on the same line as the text, even for one-line summaries:

A summary should say what the member does or means, including any limits and when it throws. Don't restate the name, and use real summaries rather than `<inheritdoc/>`.

**How this is checked:**
- **Server:** `KingsboardServer.csproj` reports a missing summary (CS1591) as a build warning, so check build output for CS1591 and fix any you introduce. A broken `cref`/`param` (CS1572–CS1574) is still a build error.
- **Unity:** the editor doesn't run this check. Use the throwaway compile-check csproj with `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and look for CS1591 under `Assets/`. Comments explain *why*.

> **⚠ REVIEW:** misplaced. "Comments explain *why*" and the two bullets below are conventions, not part of "How this is checked". Move them to **Conventions**.

- Every class that manages gRPC communication is named `XxxGrpcHandler`: server endpoints in `KingsboardServer/Handlers/`, and client handlers in `Assets/Systems/<System>/`. There is no `Services/Grpc/`.
- Work happens on the `prototype` branch; `master` is the main branch.
- **Raise a PR into `prototype` when a sprint's work is done.** Do the work on a `feature/<short-kebab-name>` branch, commit it, push it, and open a GitHub PR with `prototype` as the base (never `master`).
  - **Title:** a short, plain-language summary of what the sprint delivered, in sentence case with no prefix or trailing full stop, e.g. "Pawn stats and selected-pawn display". Name the feature, not the process ("Add pawn health bars", not "Sprint 3 work").
  - **Description:** what changed per system (server, protos, client), how it was verified, and anything left undone.
