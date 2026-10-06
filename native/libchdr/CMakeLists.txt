cmake_minimum_required(VERSION 3.24)
project(moonlark_chdr LANGUAGES C)

if(NOT MOONLARK_RID MATCHES "^(linux-x64|linux-arm64|osx-arm64|win-x64)$")
  message(FATAL_ERROR "Select an explicit supported MOONLARK_RID")
endif()
if(NOT IS_DIRECTORY "${MOONLARK_UPSTREAM}" OR
   NOT EXISTS "${MOONLARK_BUILD_INFO_HEADER}" OR NOT MOONLARK_SOURCE_ROOT)
  message(FATAL_ERROR "Use eng/build_libchdr.py with verified sources and build-info")
endif()

# Never configure upstream's shared library: its wildcard export handling and
# install rules are not our ABI. Reuse its reviewed sources, defines and codecs.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(INSTALL_STATIC_LIBS OFF CACHE BOOL "" FORCE)
set(WITH_SYSTEM_ZLIB OFF CACHE BOOL "" FORCE)
set(WITH_SYSTEM_ZSTD OFF CACHE BOOL "" FORCE)
set(CHDR_WANT_RAW_DATA_SECTOR ON CACHE BOOL "" FORCE)
set(CHDR_WANT_SUBCODE ON CACHE BOOL "" FORCE)
set(CHDR_VERIFY_BLOCK_CRC ON CACHE BOOL "" FORCE)
set(CHDR_LOWRAM_TARGET OFF CACHE BOOL "" FORCE)
set(CHDR_CD_SCRATCH_BUFFER ON CACHE STRING "" FORCE)
set(CHDR_FLAC_BACKEND drflac CACHE STRING "" FORCE)
set(CHDR_WANT_TESTS OFF CACHE BOOL "" FORCE)
set(BUILD_LTO OFF CACHE BOOL "" FORCE)
set(WITH_LZMA_ASM OFF CACHE BOOL "" FORCE)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
if(MSVC)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
  add_compile_options("/pathmap:${MOONLARK_SOURCE_ROOT}=/_/moonlark,${MOONLARK_UPSTREAM}=/_/moonlark/native/libchdr/upstream" /Brepro)
else()
  add_compile_options("-ffile-prefix-map=${MOONLARK_SOURCE_ROOT}=/_/moonlark"
    "-fdebug-prefix-map=${MOONLARK_SOURCE_ROOT}=/_/moonlark"
    "-fmacro-prefix-map=${MOONLARK_SOURCE_ROOT}=/_/moonlark"
    "-ffile-prefix-map=${MOONLARK_UPSTREAM}=/_/moonlark/native/libchdr/upstream"
    "-fdebug-prefix-map=${MOONLARK_UPSTREAM}=/_/moonlark/native/libchdr/upstream"
    "-fmacro-prefix-map=${MOONLARK_UPSTREAM}=/_/moonlark/native/libchdr/upstream")
endif()

add_subdirectory("${MOONLARK_UPSTREAM}" upstream-build EXCLUDE_FROM_ALL)
get_target_property(upstream_sources chdr-static SOURCES)
list(TRANSFORM upstream_sources PREPEND "${MOONLARK_UPSTREAM}/")
get_target_property(upstream_defines chdr-static COMPILE_DEFINITIONS)
add_library(moonlark_chdr SHARED ${upstream_sources} moonlark_chdr_build_info.c)
target_include_directories(moonlark_chdr PRIVATE "${MOONLARK_UPSTREAM}/include")
target_compile_definitions(moonlark_chdr PRIVATE ${upstream_defines} MOONLARK_CHDR_BUILDING
  "MOONLARK_CHDR_BUILD_INFO_HEADER=\"${MOONLARK_BUILD_INFO_HEADER}\"")
target_link_libraries(moonlark_chdr PRIVATE chdr-lzma miniz zstd)
foreach(codec chdr-lzma miniz zstd)
  set_target_properties(${codec} PROPERTIES POSITION_INDEPENDENT_CODE ON
    C_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN ON)
endforeach()
# zstd's public declarations override -fvisibility=hidden by default.
if(NOT MSVC)
  target_compile_definitions(zstd PRIVATE "ZSTDLIB_VISIBILITY=__attribute__((visibility(\"hidden\")))")
endif()
set_target_properties(moonlark_chdr PROPERTIES OUTPUT_NAME moonlark_chdr
  LIBRARY_OUTPUT_DIRECTORY "${MOONLARK_NATIVE_OUTPUT}"
  RUNTIME_OUTPUT_DIRECTORY "${MOONLARK_NATIVE_OUTPUT}")
if(MSVC)
  target_compile_definitions(moonlark_chdr PRIVATE CHD_DLL CHD_DLL_EXPORTS _CRT_SECURE_NO_WARNINGS)
  target_sources(moonlark_chdr PRIVATE exports.def)
  target_link_options(moonlark_chdr PRIVATE /Brepro /INCREMENTAL:NO)
elseif(APPLE)
  # Apple ld rejects unresolved symbols by default. Its former explicit
  # '-undefined error' spelling is deprecated in the current linker.
  target_link_options(moonlark_chdr PRIVATE
    "LINKER:-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/exports.osx"
    "LINKER:-fatal_warnings" "LINKER:-dead_strip")
  set_target_properties(moonlark_chdr PROPERTIES MACOSX_RPATH OFF
    INSTALL_NAME_DIR "@loader_path" BUILD_WITH_INSTALL_NAME_DIR ON)
else()
  target_link_options(moonlark_chdr PRIVATE
    "LINKER:--version-script=${CMAKE_CURRENT_SOURCE_DIR}/exports.map"
    "LINKER:--no-undefined" "LINKER:--fatal-warnings")
  target_link_libraries(moonlark_chdr PRIVATE m)
  set_target_properties(moonlark_chdr PROPERTIES SKIP_BUILD_RPATH ON)
endif()
