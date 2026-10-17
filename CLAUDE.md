# Kingsboard

Multiplayer turn-based board game. An authoritative .NET gRPC server owns all game state; a Unity client displays it.

```
Kingsboard Server/
  KingsboardServer/   ASP.NET Core gRPC server (net10.0, Serilog)
  ProtoLibrary/       Shared gRPC contract (netstandard2.1)
    Protos/           .proto files — the only place protos are added or edited
    Generated/        C# generated from Protos/ on build. TRACKED in git; also a Unity local package
  TestConsole/        Console client for exercising the server
Kingsboard Client/Kingsboard Client/   Unity 6000.6.2f1 project (URP)
```

## Build & run

```bash
dotnet build "Kingsboard Server/KingsboardServer/KingsboardServer.csproj"
dotnet run --project "Kingsboard Server/KingsboardServer" --launch-profile https   # https://localhost:7116
dotnet run --project "Kingsboard Server/TestConsole" -- --turns --board            # --server=<url> to override
```

- The server has no MVC controllers. Visual Studio injects its Endpoints Explorer hosting startup (`Microsoft.WebTools.ApiEndpointDiscovery`), which uses MVC and logs "No action descriptors found". Every launch profile in `launchSettings.json` excludes it with `ASPNETCORE_HOSTINGSTARTUPEXCLUDEASSEMBLIES`; keep that when adding profiles.
- If the server is already running, its `bin/` is locked and builds fail with MSB3021. Build to another folder with `-o <dir>` instead of killing the user's server.
- The Unity client can't be compiled from the CLI. Check that its scripts compile with a throwaway `netstandard2.1` csproj that compiles `Assets/Systems/**/*.cs` and `ProtoLibrary/Generated/*.cs`, referencing `<Unity Editor>/Data/Managed/UnityEngine/*.dll` and `Assets/Plugins/Grpc/*.dll`.
- There are no automated tests yet. Verify server changes by running a copy on a spare port (`--urls http://localhost:<port>`) and pointing TestConsole at it.

## Protos

- To change the contract, edit `Protos/*.proto` and build ProtoLibrary. That regenerates `Generated/`, which the server, TestConsole and Unity (package `com.kingsboard.protos`) all share. Never hand-edit `Generated/*.cs`; commit the regenerated files.
- `Pawns.proto` is unfinished and excluded from the build in `ProtoLibrary.csproj`.
- Each proto uses its own `csharp_namespace` (`GrpcChat`, `GrpcTurnTimer`, `GrpcBoard`).

## Server architecture (`KingsboardServer/`)

The layers are gRPC handler, then Service, then Repository. Everything is a singleton registered in `Program.cs`.

- **`Handlers/`**: gRPC endpoints (`XxxGrpcHandler : XxxService.XxxServiceBase`) that manage the gRPC communication with game clients. They are thin: they translate between proto and domain models, map results to `RpcException` status codes, and contain no game logic.
  - Streaming handlers queue events into a per-client `Channel` and keep a single writer loop, because gRPC forbids concurrent writes to one stream.
  - Stream calls end on a token linked from `context.CancellationToken` and `IHostApplicationLifetime.ApplicationStopping`.
- **`Services/`** (and `Services/Interfaces/`): game logic.
  - Services publish changes as C# events whose handlers must be quick and non-blocking.
  - Services hand out snapshots or clones, never live state.
  - `GameService` runs the game loop: it creates the board, then runs the turn loop.
- **`Repositories/`** (and `Repositories/Interfaces/`): in-memory storage behind interfaces. Repositories lock internally and return copies.
- **`Models/`**: domain types, with subfolders `Chat/`, `Board/`, `Configuration/` and `Enumerations/`. See the model rule below.
- **`Extensions/`**: static `XxxExtensions` classes holding the logic for models, such as `turn.Clone()`, `board.GetTile(x, y)` and `tile.IsPassable()`.
- **Settings:** each system has a settings class in `Models/Configuration/` with a `SectionName` constant. It is bound in `Program.cs` with `.Validate(...).ValidateOnStart()`, mirrored in `appsettings.json`, and read through `IOptionsMonitor<T>.CurrentValue`.
- **Other dependencies:** inject `Serilog.ILogger` for logging and `TimeProvider` for time; never call `DateTime.Now`.

## Client architecture (`Assets/Systems/<System>/`)

- `Networking/GrpcConnection` holds the one shared `GrpcChannel` (HTTP/2 via `UnityHttpMessageHandler`, address from `--server=`).
- Each system has a pair of classes:
  - A **`XxxGrpcHandler`** (plain class, `IDisposable`) manages the gRPC communication with the server and is the only code that touches gRPC types. It runs a reconnect loop and raises C# events on the main thread using client-side model classes.
  - A **`XxxController`** (`MonoBehaviour`) owns state and visuals. It creates itself via `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` when the scene doesn't contain one.
- **Board:** tile (x, y) sits at local position (x, 0, y). The board is rebuilt in full on each snapshot and patched on `tiles_changed`. Updates are ignored if they belong to a different board id or have a version older than the current one.
- Server rules, such as validation and limits, are enforced server-side only. Client copies of limits, such as `ChatController.MaxMessageLength`, are only for UX.

## Model rule: models hold data only

This applies to model files on both the server (`Models/**`) and the client (data classes such as `GameBoard`, `MapTile` and `ChatLine`).

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

Put logic in the type's `XxxExtensions` class (server: `KingsboardServer/Extensions/`; client: next to the model, e.g. `Assets/Systems/Board/BoardExtensions.cs`), or in the service that owns it. This applies even to small conveniences.

When you find logic in an existing model, move it to an extensions class rather than adding to it.

## Conventions

- Brace style is Allman. Fields are `_camelCase`. Write `/// <summary>` docs on public members and interfaces in plain language.

## Documentation rule

Every public type and member gets a descriptive `/// <summary>`. That covers:
- Classes, records, interfaces and enums, plus each enum value
- Properties, constants and events
- Constructors and methods, including `override`s of the gRPC base classes

Positional records also need a `<param>` for each property.

Always put `<summary>` and `</summary>` on their own lines, never on the same line as the text, even for one-line summaries:

```csharp
/// <summary>
/// Number of columns
/// </summary>
public int Width { get; set; }
```

A summary should say what the member does or means, including any limits and when it throws. Don't restate the name, and use real summaries rather than `<inheritdoc/>`.

**How this is checked:**
- **Server:** `KingsboardServer.csproj` treats a missing summary (CS1591) or a broken `cref`/`param` (CS1572–CS1574) as a build error.
- **Unity:** the editor doesn't run this check. Use the throwaway compile-check csproj with `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and look for CS1591 under `Assets/`. Comments explain *why*.
- Every class that manages gRPC communication is named `XxxGrpcHandler`: server endpoints in `KingsboardServer/Handlers/`, and client handlers in `Assets/Systems/<System>/`. There is no `Services/Grpc/`.
- Work happens on the `prototype` branch; `master` is the main branch.
