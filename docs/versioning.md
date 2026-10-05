# Versioning and public names

The product root is Moonlark. Package/root namespace: Moonlark.Libchdr.
Internal generated C namespace: Moonlark.Libchdr.Interop. Asset-only package:
Moonlark.Libchdr.Native. Native file base/DllImport name: moonlark_chdr.
The build-info export is moonlark_chdr_build_info. Raw C names/layouts stay
unchanged but never enter the 1.x public API.

eng/versions/libchdr.props is the family authority. Managed versions use our
SemVer2, beginning at 1.0.0-preview.1. Native mode is permanently Family because
the reviewed upstream pin is untagged; native and managed versions are identical
and the managed dependency must be exact, `[x.y.z]`. Runtime build-info also
checks the exact pairing. Do not number this pin as an upstream 0.3.0 release.
Changing native mode later requires a new package ID.

Major changes break public API or documented behavior; minor changes add API,
platform or a compatible raised native floor; patch changes preserve API and
behavior. API analyzers and validation against the previous released package
enforce compatibility. Every pin change needs its reviewed upstream diff in
CHANGELOG.md, including bundled codec changes.

AssemblyVersion stays major.0.0.0; FileVersion contains the numeric release;
InformationalVersion includes SemVer and our Git commit through SDK SourceLink.
Upstream version/commit appear in assembly metadata, package descriptions,
release notes, manifests and eventual BuildInfo. Initial previews are not
publication approval. Confirm Moonlark, MIT and nuget.org prefix reservation
before first public publication.

Family tag: libchdr-v{version}, with no separate native tag. Tool tag:
chdman-0.289-r1. Assets: chdman-0.289-r1-{rid}.tar.gz plus .sha256 and exact
corresponding source. Revision increments on a rebuild of the same MAME pin.
