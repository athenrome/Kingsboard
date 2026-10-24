# Kingsboard SharedModels

Model types that both the server and the Unity client use, so each is written once.

- Server: reference `SharedLibrary.csproj` (`Kingsboard Client/SharedLibrary`).
- Unity client: this folder is a local package (`com.kingsboard.sharedmodels`) referenced from `Packages/manifest.json`.

Rules for everything in here:

- **C# 9 only.** Unity compiles these files itself with C# 9, so no record structs, file-scoped namespaces or
  other newer syntax. `SharedLibrary.csproj` pins `LangVersion` 9 so the server build catches it first.
- **No Unity or server dependencies.** The asmdef has `noEngineReferences`; use plain .NET types (e.g. a colour is a
  `"#RRGGBB"` string, not `UnityEngine.Color`).
- **Data only.** The model rule applies: logic lives in the owning service on each side (server `XxxService`,
  client static `XxxService`).
