# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-05-08

### Added
- Kubernetes YAML from Notes: Secure Notes with valid K8s YAML are automatically applied, even without the `namespaces` custom field. Uses `metadata.namespace` from the manifest when no explicit namespace is set.
- `context-name` custom field for multi-cluster filtering.
- `stringData` mode for notes content.
- Dashboard E2E tests with Playwright running in CI.

### Changed
- README restructured into separate README.md and EXAMPLES.md files.
- Default key names simplified: `username` instead of `secret-key-username`, `password` instead of `secret-key-password`.

### Removed
- Serilog.Sinks.File dependency.

### Fixed
- SSH key data now correctly parsed from cipher text.

## [1.4.0] - 2026-04-12

### Added
- Support for `kubernetes.io/dockerconfigjson` secret type via the `secret-type` custom field.

### Fixed
- Deleted Vaultwarden items are now excluded from sync. Previously, deleted items caused orphan cleanup issues.
- Fallback secret key is no longer created when data is already present in custom fields.

## [1.3.0] - 2026-03-15

### Added
- `secret-type` custom field for specifying the Kubernetes Secret type. Supports `Opaque`, `kubernetes.io/tls`, and `kubernetes.io/basic-auth`.

## [1.2.8] - 2026-02-05

### Added
- Persistent device ID to prevent repeated "New device logged in" email notifications from Vaultwarden.

## [1.2.0] - 2026-02-02

### Changed
- Removed Bitwarden CLI (`bw`) dependency. Switched to direct API calls via the VwConnector package, eliminating the need for a separate CLI binary.

### Added
- End-to-end tests covering the full sync workflow.

### Performance
- Significant performance improvements from removing the subprocess-based Bitwarden CLI interface.

[unreleased]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.2.0...v1.3.0
[1.2.8]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.2.0...v1.2.8
[1.2.0]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/releases/tag/v1.2.0
