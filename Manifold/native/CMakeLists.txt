# Builds libmanifoldc as ONE self-contained shared library (Manifold, Clipper2 and the C binding linked in
# statically) so Unity only has to load a single file per platform. Manifold itself is fetched at a pinned tag.
#
#   cmake -S Manifold/native -B build -DCMAKE_BUILD_TYPE=Release
#   cmake --build build --config Release
#   -> build/libmanifoldc.dylib | build/Release/manifoldc.dll | build/libmanifoldc.so
cmake_minimum_required(VERSION 3.24)
project(manifoldc_unity CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(MANIFOLD_TAG "v3.2.1" CACHE STRING "Manifold git tag")
option(MANIFOLD_UNITY_PARALLEL "Use the TBB parallel backend" OFF)

include(FetchContent)
FetchContent_Declare(manifold_src
  GIT_REPOSITORY https://github.com/elalish/manifold.git
  GIT_TAG ${MANIFOLD_TAG}
  GIT_SHALLOW TRUE)

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(MANIFOLD_CBIND ON CACHE BOOL "" FORCE)
set(MANIFOLD_CROSS_SECTION ON CACHE BOOL "" FORCE)
set(MANIFOLD_USE_BUILTIN_CLIPPER2 ON CACHE BOOL "" FORCE)
set(MANIFOLD_TEST OFF CACHE BOOL "" FORCE)
set(MANIFOLD_PYBIND OFF CACHE BOOL "" FORCE)
set(MANIFOLD_STRICT OFF CACHE BOOL "" FORCE)
set(MANIFOLD_PAR ${MANIFOLD_UNITY_PARALLEL} CACHE BOOL "" FORCE)
set(MANIFOLD_USE_BUILTIN_TBB ${MANIFOLD_UNITY_PARALLEL} CACHE BOOL "" FORCE)
set(TBB_TEST OFF CACHE BOOL "" FORCE)
set(TBB_STRICT OFF CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(manifold_src)

# the shared library: the static C binding and everything it needs, all symbols kept
add_library(manifoldc_unity SHARED empty.cpp)
set_target_properties(manifoldc_unity PROPERTIES OUTPUT_NAME manifoldc PREFIX "lib")
if(WIN32)
  # Windows exports only what is listed: the C binding has no dllexport markers and WINDOWS_EXPORT_ALL_SYMBOLS covers
  # only this target's own objects, so the functions of manifoldc.h go into a .def file (the I/O part, compiled only
  # with MANIFOLD_EXPORT, is left out)
  set_target_properties(manifoldc_unity PROPERTIES PREFIX "")
  file(READ "${manifold_src_SOURCE_DIR}/bindings/c/include/manifold/manifoldc.h" MANIFOLDC_HEADER)
  string(REGEX REPLACE "#ifdef MANIFOLD_EXPORT[^#]*#endif" "" MANIFOLDC_HEADER "${MANIFOLDC_HEADER}")
  string(REGEX MATCHALL "manifold_[A-Za-z0-9_]+\\(" MANIFOLDC_CALLS "${MANIFOLDC_HEADER}")
  list(TRANSFORM MANIFOLDC_CALLS REPLACE "\\($" "")
  list(REMOVE_DUPLICATES MANIFOLDC_CALLS)
  # declared in the header but not implemented at this tag: listing them would fail the link (check after a tag bump)
  list(REMOVE_ITEM MANIFOLDC_CALLS manifold_cross_section_warp_context manifold_curvature_size)
  list(LENGTH MANIFOLDC_CALLS MANIFOLDC_COUNT)
  message(STATUS "manifoldc exports: ${MANIFOLDC_COUNT}")
  list(JOIN MANIFOLDC_CALLS "\n  " MANIFOLDC_DEF)
  file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/manifoldc.def" "EXPORTS\n  ${MANIFOLDC_DEF}\n")
  target_sources(manifoldc_unity PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/manifoldc.def")
endif()
if(APPLE)
  target_link_options(manifoldc_unity PRIVATE -Wl,-all_load)
  set_target_properties(manifoldc_unity PROPERTIES INSTALL_NAME_DIR "@rpath")
elseif(UNIX)
  target_link_options(manifoldc_unity PRIVATE -Wl,--whole-archive $<TARGET_FILE:manifoldc> -Wl,--no-whole-archive)
elseif(WIN32)
  target_link_options(manifoldc_unity PRIVATE /WHOLEARCHIVE:$<TARGET_FILE:manifoldc>)
endif()
target_link_libraries(manifoldc_unity PRIVATE manifoldc manifold)
