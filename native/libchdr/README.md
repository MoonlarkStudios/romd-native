# libchdr native build boundary

The reviewed source pin and header digests are in ../../eng/pins/libchdr.json.
The submodule, CMake wrapper, presets, shim and the export allowlist
(`exports.txt`; CMake generates the per-platform lists) belong to the native-wrapper
gate. No source checkout or binary is included by the foundation. CI must
produce the four supported RID binaries and their full manifests/attestations
before packaging is enabled. Local build products remain uncommitted.
