# Moonlark Native agent contract

- Inspect git status first and preserve existing edits.
- Follow the target library's upstream naming, safe .NET API conventions and
  docs/versioning.md. Generated raw interop remains internal in 1.x.
- Public APIs throw typed exceptions or expose Try methods; XML docs,
  nullable annotations, checked offsets and SafeHandle ownership are required.
- No managed exception crosses a native callback. No hot-path allocations,
  fake asynchronous native wrappers, or system-path fallback after verified load.
- Keep products independent of downstream applications. Extract shared runtime
  helpers only when a second library needs them.
- Never commit locally built binaries. CI binary PRs need pinned sources,
  manifests, attestations, exact licenses and independently reviewed evidence.
- Tests use synthetic data only. No package/asset publication, push, tag,
  tool installation or runner provisioning without explicit owner approval.
- Read CONTRIBUTING.md for actual checks; report failures and unrun platforms.
  Stage explicit accepted paths. Commits: type(scope): description, one line.
