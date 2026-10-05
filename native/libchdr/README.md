# libchdr native build boundary

The reviewed source pin and header digests are in ../../eng/pins/libchdr.json.
The submodule, CMake wrapper, shim and export lists belong to the native-wrapper
gate. No source checkout or binary is included by the foundation. CI must
produce the four supported RID binaries and their full manifests/attestations
before packaging is enabled. Local build products remain uncommitted.
