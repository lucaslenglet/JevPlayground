# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- MIT-0 license, now declared in the package metadata.
- Release notes on nuget.org and on the GitHub release, taken from this file.

## [0.1.0] - 2026-09-20

### Added

- `jevplay serve`: a local web playground to send a context with typed questions and see each
  answer with its distribution.
- Providers `typesafe` (body shape inferred, untested), `simple-jev` (verified) and `mock`
  (deterministic, offline).
- Question types `choice`, `score` and `noul`.

[Unreleased]: https://github.com/lucaslenglet/JevPlayground/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/lucaslenglet/JevPlayground/releases/tag/v0.1.0
