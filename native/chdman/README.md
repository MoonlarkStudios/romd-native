# chdman build boundary

../../eng/pins/mame.json records the exact MAME0.289 source archive, size,
digest, tag and commit. The tool gate builds only chdman, strips it and checks
its dependencies on three native RIDs. Every release ships corresponding source
and an exact linked-license inventory. The source preflight workflow builds and
publishes no tool binary.
