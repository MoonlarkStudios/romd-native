# Runtime notice acquisition addendum

## Findings

1. **Source closure remains incomplete.** The exact GCC and glibc source RPM
   filenames are recorded in the acquired binary RPM headers, but their full
   corresponding source artifacts were not acquired or delivered. Object-level
   source attribution for the static GCC/glibc members is not established by
   a package-wide license tag. No conclusion about legal exception applicability,
   complete source delivery, or G3 acceptance follows from this addendum.
2. **Final image identities remain separate.** These are exact builder RPM
   components. A final Ubuntu/.NET image's actual dynamic runtime package
   versions and notices need their own image inventory. This is not a final
   image notice census and does not qualify a Docker build or clean CI route.
3. **Package and notice staging still need root execution.** The base bundle's
   empty private pending-pack library inventory deliberately cannot pass fetch.
   Root must create the actual SDK packages, update concrete pins/locks, and
   add explicit finite notice copying with role controls. This preparation
   changes none of those live files.

## Executed passive findings

A2 authorized bounded, project-local read-only acquisition after the original
397-file bundle seal. The original `freeze.json` remains unchanged; its entries
all still match. New research files are sealed separately and in the final
combined census. No SDK, Git, container, RPM installation, target binary,
game body read, or publication occurred.

The original default-sandbox acquisition returned DNS errors for four requests;
those exact error receipts remain under this directory. The same bounded
requests then succeeded with scoped network access, followed by exact RPM
downloads. Every download has requested/final URL, byte bound, actual size,
SHA256 and error/status receipt. Original command completion pointers are in
`command-results.json`; no OS PID is invented.

| Exact acquired package | Bytes | SHA256 | Binding |
| --- | ---: | --- | --- |
| gcc-toolset-15-libstdc++-devel-15.2.1-7.1.el8_10.x86_64.rpm | 4,464,844 | 53dc54787200a2f74129a6f40880a5affa4b0e09f85a1adc6474c58b8a7ba7f0 | Official AppStream primary metadata checksum and extracted actual static archive |
| gcc-toolset-15-libstdc++-devel-15.2.1-7.1.el8_10.aarch64.rpm | 4,412,588 | 5e538c80f820ff55be3ebc3fafaee69b771ea0a54b410282dd2624321d233e2f | Official HTTPS/index size, exact header identity and extracted actual static archive |
| gcc-toolset-15-gcc-15.2.1-7.1.el8_10.x86_64.rpm | 58,032,816 | a639feaaa4c1dee999d7da5ea783db69f3b021f19a5a0c4791a7675f148516d5 | Official AppStream primary metadata checksum and exact header identity |
| glibc-2.28-251.el8_10.40.x86_64.rpm | 2,308,552 | d7a83dee4d25476eaf4a38be8894e916c79620e20cb7bb955a75ec71069f7e54 | Official HTTPS/index size and exact header identity; no repository checksum claim |
| glibc-2.28-251.el8_10.40.aarch64.rpm | 1,885,840 | 75f0adba187c4aec609e68c1e4877b484ef45b5468a2da7d191c8085cd2af0e7 | Official HTTPS and exact header identity; no repository checksum claim |

The x64 primary metadata compressed body matched the exact SHA256/size in the
retained official `repomd.xml`. Exact package XML records and their source RPM
references are preserved. RPM signatures were **not verified**. No AlmaLinux
notarization, upstream attestation, or release-provenance claim is made.

Both extracted static `libstdc++.a` bodies match the actual candidate manifests:

- x64: 7,068,396 bytes,
  `598e08520faad3c584081af60a1a8385c332b498997964c73ed32de89deefa12`.
- ARM64: 7,187,564 bytes,
  `e23fce263be8d80da4f1ba91295154eb08ce7e7f99b9d775721cdce8253a2db9`.

Both devel RPMs identify
`gcc-toolset-15-gcc-15.2.1-7.1.el8_10.src.rpm`; they contain no standalone
notice files. The matching x64 GCC package identifies the same source RPM
and supplies five exact full text files:
`COPYING`, `COPYING.LIB`, `COPYING.RUNTIME`, `COPYING3`, `COPYING3.LIB`.
`COPYING.RUNTIME` is 3,324 bytes,
`9d6b43ce4d8de0c878bf16b54d8e7a10d9bd42b75178153e3af6a815bdc90f74`.
The full original text explicitly conditions its application on a file bearing
the relevant notice; the addendum does not infer satisfaction for every object.
The source RPM identity is common across both architecture packages, but the
ARM GCC package's license payload was not separately acquired.

Both exact glibc packages identify `glibc-2.28-251.el8_10.40.src.rpm` and supply
byte-identical `COPYING` (18,092 bytes), `COPYING.LIB` (26,530 bytes), and
`LICENSES` (18,941 bytes). This preserves the full original assorted attribution
notices, rather than replacing them with a license name. It does not body-bind
builder `libc_nonshared.a` or startup objects, which were not exported/hashes in
the original manifests. Build pin records specify the same glibc-devel/header
NVR for both RIDs; the actual x64 installed RPM census confirms those identities.

## Bounded candidate notice payload

`staged/<rid>/tool/notices/runtime/` contains the five unchanged GCC files,
the three unchanged glibc files and this explicit source-reference addendum.
The original full RPM paths and bodies remain under `notice-payloads/`;
the inspection receipts contain a complete passive member census.
These files are concrete private candidate material for a root-owned finite
notice staging route. They do not retroactively change the base frozen bundle,
do not distribute GCC build executables, and do not establish final distribution
qualification.

The linked object inventories distinguish actual static runtime contributions
from build-only Clang, GNU Make/binutils, GENie/Lua and .NET. Nothing in those
tools' licenses was inferred merely because they executed during a build.
No component was reclassified as build-only when its members appear in a map.

The publisher describes its source package repository in the
[AlmaLinux build-system documentation](https://wiki.almalinux.org/development/AlmaLinux-Build-System)
and its source cache workflow in the
[package-building guide](https://wiki.almalinux.org/documentation/package-building-guide.html).
Those are research pointers, **not** exact acquired corresponding-source pins.

## Validation integrity, test gaps, open questions

Passive header and CPIO decoding checked exact name/version/release/architecture,
bounded member counts and expanded sizes, canonical relative paths, full
notice bytes, and exact static C++ archive digests. Selected notices were
copied only from regular members. No filesystem links, devices, executable
payloads or package scripts were installed/executed. All original RPM bodies
and inspection inventories are retained. Parser/check failures were not
suppressed; no native/tool qualification check was run or weakened.

Meaningful runtime/image/package controls and full compiler/runtime source
closure remain root-owned work. No broader tests passed in this task.
The blocking open questions are the exact corresponding source payload and
object notice mappings, actual newly packed feed identities, and the final
image's finite notice/runtime inventory. G2/G3/G4 remain unaccepted.
