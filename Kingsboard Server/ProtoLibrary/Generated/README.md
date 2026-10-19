# Kingsboard Protos (generated)

Everything in this folder except `package.json`, `Kingsboard.Protos.asmdef`, this README and Unity `.meta` files
is **generated** from `../Protos/*.proto` when the ProtoLibrary project builds. Don't edit the `.cs` files. Change the
`.proto` and rebuild.

- Server: reference `ProtoLibrary.csproj`.
- Unity client: this folder is a local package (`com.kingsboard.protos`) referenced from `Packages/manifest.json`.
  It needs the Google.Protobuf and Grpc.Core.Api assemblies, which NuGetForUnity provides.
