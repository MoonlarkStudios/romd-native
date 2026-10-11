# CHD worker tool candidate notices

This inventory describes the two exact unqualified Linux `chdman` candidates.
It does not change any grant or give a legal opinion or release qualification.
This image's tool notice subtree contains 30 finite legal and source-reference
texts. It contains no MAME source archive, selected source units, linker maps,
build makefiles, compiler executables or SDK.

The full original MAME source archive, complete selected source units and
original build evidence remain in a separate retained private source bundle.
Paths prefixed `linked-source/` or `evidence/` below refer to that separate
bundle, not to files shipped beside this inventory in the app image. Their
retention and upstream links do not establish completed corresponding-source
delivery or a public source offer.

MAME upstream: `f34f02505e32c1993c6a782b6814232cbfc74e36`, tag `mame0289`.
Source archive SHA256:
`0929cc749afabcef892900e10dd90bd8b05f94a7dde8f367ac6a5d2082589f84`.
Upstream source reference:
https://github.com/mamedev/mame/tree/f34f02505e32c1993c6a782b6814232cbfc74e36
Original source archive reference:
https://github.com/mamedev/mame/archive/refs/tags/mame0289.tar.gz

`mame-mame0289/COPYING` states MAME as a whole uses GNU GPL version 2.
`mame-mame0289/docs/legal/GPL-2.0` preserves the original full text here.
`chdman.cpp`'s BSD-3-Clause header does not establish that the entire linked
executable is BSD licensed. This worker-only tool is separate from the
Moonlark/libchdr library. The app image retains that library's own actual
package notices under `/usr/share/doc/romd/libchdr/`, separately from these
worker tool notices. No claim about ROMD's own license follows from this file.

## Actual linked source components

The separate source bundle's original linker maps and generated make
prerequisites bind the following archive members to source units. Its
`linked-source/` subtree retains the complete selected units and embedded
original notices; `evidence/notice-source-files.json` records their body sizes
and hashes. Those copies match both actual original source-input censuses.
This image preserves the finite full legal files listed below, not those
separately retained source bodies or evidence.

| Actual link component | Material in separate source bundle | Full legal files shipped here and scope |
| --- | --- | --- |
| Direct chdman object and libutils (18 objects) | `linked-source/src/tools/chdman.cpp`, `linked-source/src/lib/util/` | Original MAME COPYING and docs/legal texts; per-file source headers remain in the separate source bundle |
| libocore_sdl (7 objects) | `linked-source/src/osd/` | Original MAME COPYING and docs/legal texts; archive name does not establish a linked SDL shared library |
| lib7z (4 objects) | `linked-source/3rdparty/lzma/C/` | `mame-mame0289/3rdparty/lzma/DOC/7zC.txt`; actual LZMA C objects and original public-domain attribution |
| libflac (15 ARM objects, 21 x64 objects) | `linked-source/3rdparty/flac/src/libFLAC/` | `mame-mame0289/3rdparty/flac/COPYING.Xiph`, `.GPL`, `.LGPL`, `.FDL`; exact original alternative/component texts, no claim FLAC command-line programs were linked |
| libutf8proc (1 object) | `linked-source/3rdparty/utf8proc/utf8proc.c` | `mame-mame0289/3rdparty/utf8proc/LICENSE.md`; original MIT and Unicode data notices |
| libzlib (8 objects) | `linked-source/3rdparty/zlib/` | `mame-mame0289/3rdparty/zlib/LICENSE`; original zlib license |
| libzstd (21 objects, including xxhash) | `linked-source/3rdparty/zstd/lib/` | `mame-mame0289/3rdparty/zstd/LICENSE` and `COPYING`; original BSD/GPL alternatives, no rewritten choice statement |
| libexpat | Original map and source inputs | `mame-mame0289/3rdparty/expat/COPYING`; LOAD-selected archive with no extracted member in either map, a conservative notice rather than a claim its objects were included |

All original eight `docs/legal` files are shipped here. Some describe source
components outside this bounded object inventory; their presence does not prove
those components are linked. The complete source archive in the separate source
bundle retains original headers and legal material beyond the copied units.

## Build inputs versus distributed tool contents

GENie/Lua, the .NET engineering CLI, CMake, Ninja, Clang, Make, binutils,
pkg-config and Python were build tools. Their execution does not establish
that they were included in the final executable. These build executables are
not shipped in this tool notice subtree. The actual direct/object linker maps,
rather than the full build source inventory, establish the contributions here.

SDL headers were actual build inputs. `SDL2-LICENSE.txt` ships their original
notice. The actual ELF dependency records contain no SDL shared library, and
the maps contain MAME's OS abstraction archive rather than an SDL library
archive. This does not claim no SDL inline/header material could contribute.

The exact recipe inputs and generated makefiles remain under `evidence/` in
the separate source bundle. The actual engineering recipe hashes MAME source,
link map, selected tools and static C++ runtime, records source-date epoch,
commands and dependencies, and keeps the candidates unqualified. Recipe/source
reference for the x64 producer:
https://github.com/MoonlarkStudios/romd-native/tree/789b897171c8d258ce72b93f0c63ef7388da26c9
The repository URL matches the retained actual run API metadata. This is a
source reference, not a release asset URL or completed source delivery.

## Runtime legal texts and remaining source gaps

Both maps extract members from GCC toolset 15 `libstdc++.a` and glibc
`libc_nonshared.a`. ARM also extracts seven members from GCC `libgcc.a`.
Both LOAD startup/end objects (crt1/crti/crtbegin/crtend/crtn). The x64 map also
LOADs `libmvec_nonshared.a`, with no extracted member identified by this audit.

The actual static C++ archive SHA256s are
`e23fce263be8d80da4f1ba91295154eb08ce7e7f99b9d775721cdce8253a2db9`
(ARM) and
`598e08520faad3c584081af60a1a8385c332b498997964c73ed32de89deefa12`
(x64). The manifests bind those bodies; the original maps bind the used objects.

This notice subtree now contains eight unchanged full runtime legal texts
acquired from exact AlmaLinux builder RPMs: GCC `COPYING`, `COPYING.LIB`,
`COPYING.RUNTIME`, `COPYING3`, `COPYING3.LIB` under `runtime/gcc-toolset-15/`,
and glibc `COPYING`, `COPYING.LIB`, `LICENSES` under
`runtime/glibc-builder-2.28/`. The GCC devel RPM archive bodies match the actual
static C++ hashes above. Both devel RPMs and the acquired x64 GCC legal package
identify source RPM `gcc-toolset-15-gcc-15.2.1-7.1.el8_10.src.rpm`; the same
five legal texts are retained for both RIDs, while the ARM GCC legal package
was not separately acquired. The acquired glibc RPMs identify
`glibc-2.28-251.el8_10.40.src.rpm` and supply byte-identical legal texts for
both RIDs. Original RPM bodies and inspection receipts remain in the separate
research/source bundle, not the app image. RPM signatures were not verified.

`runtime/SOURCE_REFERENCES.md` is the unchanged historical acquisition addendum.
Its pending-pack and staging observations describe the earlier preparation
state, not an assertion that those later steps have passed in this image.
It records exact RPM identities, notice hashes and limits. These full legal
and exception texts do not establish object-level notice attribution or
exception applicability. Full corresponding GCC/glibc source RPMs were not
acquired or delivered; builder startup, libgcc and libc_nonshared object body
identities were not exported in the original manifests. Complete compiler/
runtime license and corresponding-source closure remain unqualified.

The tool also declares dynamic libpthread, libdl, libm, libutil, libgcc_s,
libc and architecture loader dependencies. A final image's actual runtime
package identities and original notices require a separate image inventory.
These acquired builder RPM texts do not qualify that dynamic runtime closure.
A source reference alone is not completed source delivery. No system runtime
was installed or executed as part of this passive notice preparation.
