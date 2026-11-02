# AGENTS.md

## Build and run

- The API runs as `Jobmate.Api.exe` from `src/Jobmate.Api/bin/Debug/net9.0`. A running
  instance holds file handles on its own output DLLs, which makes the build fail with
  `MSB3021` / `MSB3027`.
- `Jobmate.Api.csproj` has a `StopRunningApi` target that kills that process before
  `CoreBuild`, so a plain `dotnet build` is safe while the API is up. Do not
  hand-kill it first unless a build is hanging.
- After any build, the API is no longer running. If you started it for a test, restart it.
- Long-running work that needs the API alive should use `dotnet watch run --project src/Jobmate.Api`
  instead of a plain `Start-Process`, so restarts stay under the watcher.
