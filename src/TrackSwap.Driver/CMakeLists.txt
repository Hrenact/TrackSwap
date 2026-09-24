cmake_minimum_required(VERSION 3.24)

project(TrackSwapDriver VERSION 0.0.2 LANGUAGES C CXX)

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

# MinHook is used only for the opt-in physical-source hiding feature. Keep the
# dependency pinned so the injected code is reproducible.
set(TRACKSWAP_MINHOOK_REVISION "565968b28583221751cc2810e09ea621745fc3a3" CACHE STRING "Pinned MinHook commit")
FetchContent_Declare(
    minhook
    GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
    GIT_TAG ${TRACKSWAP_MINHOOK_REVISION}
    GIT_SHALLOW FALSE
    GIT_PROGRESS TRUE
    UPDATE_DISCONNECTED TRUE
)
FetchContent_GetProperties(minhook)
if(NOT minhook_POPULATED)
    FetchContent_Populate(minhook)
endif()
add_library(trackswap_minhook STATIC
    "${minhook_SOURCE_DIR}/src/buffer.c"
    "${minhook_SOURCE_DIR}/src/hook.c"
    "${minhook_SOURCE_DIR}/src/trampoline.c"
    "${minhook_SOURCE_DIR}/src/hde/hde64.c")
target_include_directories(trackswap_minhook PUBLIC "${minhook_SOURCE_DIR}/include")
target_include_directories(trackswap_minhook PRIVATE
    "${minhook_SOURCE_DIR}/src"
    "${minhook_SOURCE_DIR}/src/hde")

set(TRACKSWAP_OPENVR_HAND_SIMULATION_DIR
    "${openvr_SOURCE_DIR}/samples/drivers/drivers/handskeletonsimulation/src")
set(TRACKSWAP_OPENVR_VRMATH_DIR
    "${openvr_SOURCE_DIR}/samples/drivers/utils/vrmath")

add_library(driver_trackswap SHARED
    src/control_protocol.h
    src/control_server.cpp
    src/control_server.h
    src/driver_factory.cpp
    src/finger_animation.cpp
    src/finger_animation.h
    src/pose_math.cpp
    src/pose_math.h
    src/pose_hiding_hook.cpp
    src/pose_hiding_hook.h
    src/runtime_launcher.cpp
    src/runtime_launcher.h
    src/tracker_registry.cpp
    src/tracker_registry.h
    src/virtual_controller.cpp
    src/virtual_controller.h
    src/virtual_tracker.cpp
    src/virtual_tracker.h
    "${TRACKSWAP_OPENVR_HAND_SIMULATION_DIR}/hand_simulation.cpp"
    "${TRACKSWAP_OPENVR_HAND_SIMULATION_DIR}/hand_simulation.h"
)

target_compile_features(driver_trackswap PRIVATE cxx_std_17)
target_include_directories(driver_trackswap SYSTEM PRIVATE "${openvr_SOURCE_DIR}/headers")
target_include_directories(driver_trackswap PRIVATE
    "${TRACKSWAP_OPENVR_HAND_SIMULATION_DIR}"
    "${TRACKSWAP_OPENVR_VRMATH_DIR}")
target_compile_definitions(driver_trackswap PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(driver_trackswap PRIVATE trackswap_minhook)
target_compile_options(driver_trackswap PRIVATE /W4 /WX /permissive- /EHsc)
set_source_files_properties(
    "${TRACKSWAP_OPENVR_HAND_SIMULATION_DIR}/hand_simulation.cpp"
    PROPERTIES COMPILE_OPTIONS "/wd4244;/wd4305;/wd4505")

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

    add_executable(trackswap_finger_animation_tests
        tests/finger_animation_tests.cpp
        src/finger_animation.cpp
        src/finger_animation.h
        src/control_protocol.h
    )
    target_compile_features(trackswap_finger_animation_tests PRIVATE cxx_std_17)
    target_include_directories(trackswap_finger_animation_tests PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
    target_compile_definitions(trackswap_finger_animation_tests PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_compile_options(trackswap_finger_animation_tests PRIVATE /W4 /WX /permissive- /EHsc)
    add_test(NAME finger_animation COMMAND trackswap_finger_animation_tests)
endif()
