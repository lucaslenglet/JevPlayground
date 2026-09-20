# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                                    # whole solution, 0 warnings is the expected state
dotnet run --project src/JevPlay -- serve --provider mock
./tests/smoke/run.sh                            # end to end over a real package (see below)
./tests/smoke/run.sh 1.2.3                      # same, at the version the release workflow would pack
```

`TreatWarningsAsErrors` is on repo-wide, so a warning fails the build.

There is no unit test project. The smoke test is the whole safety net, and it is deliberately
placed after packing: see Packaging below.

## What this is

A .NET global tool (`jevplay`) that serves a small local web interface for talking to a Jev model.
A Jev model is "System One": it does not generate prose, it returns a typed decision per question
with its probabilities. Three question types, `choice`, `score` and `noul`, described in the README.

The browser never calls the remote API. It posts to `/api/classify` on the local server, which
relays. Two reasons, both load-bearing: it sidesteps CORS, and the API key never reaches the page.

## Providers

`IJevProvider` is the seam. One provider is one class plus its own DTOs, and it owns everything
about the wire: the request shape, authentication, the response shape. `HttpJevProvider` carries
only transport — send JSON, read JSON, turn failures into `JevProviderException` — and knows no
body shape at all.

**`TypeSafeProvider` is built on an inference, not a source.** The official TypeSafe documentation
is behind a waitlist and has never been read. Its body shape mirrors Simple Jev, whose repository
says it takes after the TypeSafe interface. The duplication between `TypeSafeProvider` and
`SimpleJevProvider` is therefore deliberate — do not "fix" it by extracting a shared mapper. The
point is that correcting TypeSafe stays a diff inside one file, without touching the provider that
is actually verified.

`MockProvider` is a direct port of the Node prototype's mock mode, down to the FNV-1a seed. Its
answers match that prototype bit for bit, and the surrogate-pair skip in `Seed` exists to keep that
true. Changing the draw breaks reproducibility for anyone working on the interface offline.

## The front end

`src/JevPlay/wwwroot` is embedded in the assembly as `EmbeddedResource` with a `web/` logical name,
so the packed tool carries no loose files. `Server/Assets.cs` reads them back.

The form is persisted in `localStorage`. **This is knowingly imperfect**: `localStorage` is scoped
to the origin, the origin carries the port, and the default is a free port picked by the OS, so the
saved form is lost whenever the port changes. Keeping the state in the browser was a deliberate
choice over moving it server-side. Cookies are the one browser store that ignores the port, at the
cost of a 4 KB limit and being readable by anything else on `127.0.0.1`.

## Port

Kestrel binds on port 0 and the actual port is read back from `app.Urls` after `StartAsync`. That
is why the announcement happens after start, not before. `--port` overrides it.

The tool does not open a browser: it prints the URL and waits.

## Packaging

`Microsoft.NET.Sdk` plus `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, not
`Microsoft.NET.Sdk.Web`. That is the supported path for a global tool hosting Kestrel. It means a
machine installing the tool needs the ASP.NET Core runtime, not just the .NET runtime.

`tests/smoke/run.sh` is what proves the package works. It packs to a local feed under
`.artifacts/`, installs the tool from that feed with `--tool-path`, starts it on a free port and
drives the real endpoints. It wipes the feed and uses an isolated `NUGET_PACKAGES` on each run,
because a repacked package at an unchanged version would otherwise be served from cache and the
test would pass without testing anything.

Build output alone would not catch a missing embedded asset, a broken tool manifest or a bad
`FrameworkReference`. That is why the check lives after `dotnet pack`, not before.

## MSBuild layout

- `Directory.Build.props` — shared compiler settings plus `JevPlayVersion`, the single source for
  the package version. The release workflow overrides it from the tag; `tests/smoke/run.sh` passes
  it through when given one.
- `Directory.Packages.props` — central package management, with no packages. The tool has no NuGet
  dependency on purpose, and this keeps the first one anybody adds visible.
- There is no `Directory.Build.targets`: nothing in this repo needs a property evaluated after the
  project file.

`--version` reads `AssemblyInformationalVersionAttribute`, not the assembly version, which drops a
prerelease suffix: a `1.2.3-rc.1` package would otherwise report itself as `1.2.3`.

## Release

Pushing a `v*` tag runs `.github/workflows/release.yml`: the tag is the only source of the published
version (`v1.2.3` -> `1.2.3`), so nothing in the repo is bumped to release. The workflow packs at
that version through `run.sh`, and pushes **that same validated artifact** to nuget.org — never a
separately packed one.

Publishing uses nuget.org trusted publishing (OIDC), not a stored API key. The job needs
`id-token: write`, and the `NuGet/login@v1` step must stay immediately before the push: it trades
one OIDC token for one API key valid for a single hour. The only repository secret is `NUGET_USER`
(the nuget.org profile name). The matching policy lives on nuget.org and names the repository owner,
the repository, and the workflow file name alone — `release.yml`, without its directory.
