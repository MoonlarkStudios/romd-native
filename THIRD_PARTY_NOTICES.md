# Third-party inventory

Repository wrapper code, build scripts and generator configuration use MIT.
Native artifacts retain their actual upstream component licenses. Native
binaries are staged under the checkout's ignored `artifacts/`; internal
`local-unqualified` native package candidates include their declared RID
binaries. These candidates remain unqualified for release. This inventory does
not establish a complete component/license or corresponding-source inventory
and does not replace the exact license texts and required corresponding source
for each qualified package/archive.

At libchdr commit `607694ca0812edfc9cc2030c64634fc2393668de`:

| Component | Version | Selected upstream license |
| --- | --- | --- |
| libchdr | v0.3.0-116-g607694c | BSD-3-Clause |
| miniz | 3.1.2 | MIT |
| LZMA SDK | 26.02 | Public domain |
| zstd | 1.5.7 | BSD-3-Clause choice of dual BSD/GPL license |
| dr_flac | 0.13.4 | MIT-0 choice of MIT-0/public domain |

[Pinned libchdr source](https://github.com/rtissera/libchdr/tree/607694ca0812edfc9cc2030c64634fc2393668de)
contains the component notices. Their complete texts must travel with binaries.

MAME0.289's [COPYING](https://github.com/mamedev/mame/blob/mame0289/COPYING)
declares the combined distribution GPL-2.0; chdman.cpp itself is BSD-3-Clause.
The exact linked tool still needs its component/license inventory. Apply
conservative GPL-2.0 distribution obligations and ship exact corresponding
source; do not infer an or-later grant or a whole-artifact BSD license.
No game data is distributed.
