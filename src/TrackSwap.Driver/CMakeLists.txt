cmake_minimum_required(VERSION 3.24)

project(TrackSwapDriver VERSION 0.0.2 LANGUAGES CXX)

include(CTest)

if(POLICY CMP0169)
    cmake_policy(SET CMP0169 OLD)
endif()

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "TrackSwap.Driver currently supports Windows x64 only.")
endif()

include(FetchContent)

# OpenVR IVRSystem_026 snapshot, pinned to an immutable commit for reproducible headers.
set(TRACKSWAP_OPENVR_REVISION "0924064316de3effbcd1acf1e309182a2deb1c05" CACHE STRING "Pinned OpenVR commit")
FetchContent_Declare(
    openvr
    GIT_REPOSITORY https://github.com/ValveSoftware/openvr.git
    GIT_TAG ${TRACKSWAP_OPENVR_REVISION}
    GIT_SHALLOW FALSE
    GIT_PROGRESS TRUE
    UPDATE_DISCONNECTED TRUE
)
FetchContent_GetProperties(openvr)
if(NOT openvr_POPULATED)
    FetchContent_Populate(openvr)
endif()

add_library(driver_trackswap SHARED
    src/control_protocol.h
    src/control_server.cpp
    src/control_server.h
    src/driver_factory.cpp
    src/pose_math.cpp
    src/pose_math.h
    src/runtime_launcher.cpp
    src/runtime_launcher.h
    src/tracker_registry.cpp
    src/tracker_registry.h
    src/virtual_tracker.cpp
    src/virtual_tracker.h
)

target_compile_features(driver_trackswap PRIVATE cxx_std_17)
target_include_directories(driver_trackswap SYSTEM PRIVATE "${openvr_SOURCE_DIR}/headers")
target_compile_definitions(driver_trackswap PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(driver_trackswap PRIVATE /W4 /WX /permissive- /EHsc)

set_target_properties(driver_trackswap PROPERTIES
    OUTPUT_NAME "driver_trackswap"
    PREFIX ""
)

install(TARGETS driver_trackswap RUNTIME DESTINATION bin/win64)
install(FILES driver.vrdrivermanifest DESTINATION .)
install(DIRECTORY resources/ DESTINATION resources)

if(BUILD_TESTING)
    add_executable(trackswap_pose_math_tests
        tests/pose_math_tests.cpp
        src/pose_math.cpp
        src/pose_math.h
    )
    target_compile_features(trackswap_pose_math_tests PRIVATE cxx_std_17)
    target_include_directories(trackswap_pose_math_tests PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
    target_include_directories(trackswap_pose_math_tests SYSTEM PRIVATE "${openvr_SOURCE_DIR}/headers")
    target_compile_definitions(trackswap_pose_math_tests PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_compile_options(trackswap_pose_math_tests PRIVATE /W4 /WX /permissive- /EHsc)
    add_test(NAME pose_math COMMAND trackswap_pose_math_tests)
endif()
