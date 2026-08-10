cmake_minimum_required(VERSION 3.25)

project(TaskFlyoutTaskbarWeather LANGUAGES C CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Task Flyout taskbar weather supports Windows x64 only.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_library(taskbar_weather_minhook STATIC
    third_party/minhook/src/buffer.c
    third_party/minhook/src/hook.c
    third_party/minhook/src/trampoline.c
    third_party/minhook/src/hde/hde64.c
)
target_include_directories(taskbar_weather_minhook
    PUBLIC third_party/minhook/include
    PRIVATE
        third_party/minhook/src
        third_party/minhook/src/hde
)
target_compile_definitions(taskbar_weather_minhook PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)
if(MSVC)
    target_compile_options(taskbar_weather_minhook PRIVATE
        /W3
        /guard:cf
    )
endif()

add_library(taskbar_weather_profile STATIC
    src/module_profile.cpp
)
target_include_directories(taskbar_weather_profile PUBLIC include)
target_compile_definitions(taskbar_weather_profile PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_profile PUBLIC user32)

add_library(taskbar_weather_view_model STATIC
    src/weather_view_model.cpp
)
target_include_directories(taskbar_weather_view_model PUBLIC include)
target_compile_definitions(taskbar_weather_view_model PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)

add_library(taskbar_weather_tree_profile STATIC
    src/taskbar_tree_profile.cpp
)
target_include_directories(taskbar_weather_tree_profile PUBLIC include)
target_compile_definitions(taskbar_weather_tree_profile PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_tree_profile PUBLIC runtimeobject)

add_library(taskbar_weather_slot_geometry STATIC
    src/taskbar_slot_geometry.cpp
)
target_include_directories(taskbar_weather_slot_geometry PUBLIC include)
target_compile_definitions(taskbar_weather_slot_geometry PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)

add_library(taskbar_weather_mount STATIC
    src/taskbar_weather_mount.cpp
    src/weather_xaml_view.cpp
)
target_include_directories(taskbar_weather_mount PUBLIC include)
target_compile_definitions(taskbar_weather_mount PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_mount PUBLIC
    taskbar_weather_tree_profile
    taskbar_weather_slot_geometry
    taskbar_weather_view_model
    runtimeobject
    windowsapp
)

add_library(taskbar_weather_host_runtime STATIC
    src/loaded_taskbar_profile.cpp
    src/taskbar_detour.cpp
)
target_include_directories(taskbar_weather_host_runtime PUBLIC include)
target_compile_definitions(taskbar_weather_host_runtime PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_host_runtime PUBLIC
    taskbar_weather_profile
    taskbar_weather_minhook
    psapi
    synchronization
    user32
)

add_library(taskbar_weather_host SHARED
    src/host.cpp
    src/host.def
)
set_target_properties(taskbar_weather_host PROPERTIES
    OUTPUT_NAME "TaskFlyout.TaskbarHost"
)
target_include_directories(taskbar_weather_host PRIVATE include)
target_compile_definitions(taskbar_weather_host PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_host PRIVATE
    taskbar_weather_host_runtime
    taskbar_weather_mount
    taskbar_weather_profile
    user32
)

add_executable(taskbar_weather_broker
    src/broker_main.cpp
)
set_target_properties(taskbar_weather_broker PROPERTIES
    OUTPUT_NAME "TaskFlyout.TaskbarBroker"
)
target_include_directories(taskbar_weather_broker PRIVATE include)
target_compile_definitions(taskbar_weather_broker PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_broker PRIVATE taskbar_weather_profile user32)

add_executable(taskbar_weather_profile_tests
    tests/module_profile_tests.cpp
)
target_include_directories(taskbar_weather_profile_tests PRIVATE include)
target_compile_definitions(taskbar_weather_profile_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_profile_tests PRIVATE taskbar_weather_profile)

add_executable(taskbar_weather_view_model_tests
    tests/weather_view_model_tests.cpp
)
target_include_directories(taskbar_weather_view_model_tests PRIVATE include)
target_compile_definitions(taskbar_weather_view_model_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_view_model_tests PRIVATE taskbar_weather_view_model)

add_executable(taskbar_weather_detour_tests
    tests/taskbar_detour_tests.cpp
)
target_include_directories(taskbar_weather_detour_tests PRIVATE include)
target_compile_definitions(taskbar_weather_detour_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_detour_tests PRIVATE taskbar_weather_host_runtime)

add_executable(taskbar_weather_tree_profile_tests
    tests/taskbar_tree_profile_tests.cpp
)
target_include_directories(taskbar_weather_tree_profile_tests PRIVATE include)
target_compile_definitions(taskbar_weather_tree_profile_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_tree_profile_tests PRIVATE taskbar_weather_tree_profile)

add_executable(taskbar_weather_mount_tests
    tests/taskbar_weather_mount_tests.cpp
)
target_include_directories(taskbar_weather_mount_tests PRIVATE include)
target_compile_definitions(taskbar_weather_mount_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_mount_tests PRIVATE taskbar_weather_mount)

add_executable(taskbar_weather_slot_geometry_tests
    tests/taskbar_slot_geometry_tests.cpp
)
target_include_directories(taskbar_weather_slot_geometry_tests PRIVATE include)
target_compile_definitions(taskbar_weather_slot_geometry_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_slot_geometry_tests PRIVATE taskbar_weather_slot_geometry)

add_executable(taskbar_weather_minhook_smoke_tests
    tests/minhook_smoke_tests.cpp
)
target_include_directories(taskbar_weather_minhook_smoke_tests PRIVATE include)
target_compile_definitions(taskbar_weather_minhook_smoke_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_minhook_smoke_tests PRIVATE taskbar_weather_minhook)

foreach(target
        taskbar_weather_profile
        taskbar_weather_view_model
        taskbar_weather_tree_profile
        taskbar_weather_slot_geometry
        taskbar_weather_mount
        taskbar_weather_host_runtime
        taskbar_weather_host
        taskbar_weather_broker
        taskbar_weather_profile_tests
        taskbar_weather_view_model_tests
        taskbar_weather_detour_tests
        taskbar_weather_tree_profile_tests
        taskbar_weather_mount_tests
        taskbar_weather_slot_geometry_tests
        taskbar_weather_minhook_smoke_tests)
    if(MSVC)
        target_compile_options(${target} PRIVATE
            /W4
            /WX
            /permissive-
            /EHsc
            /guard:cf
            /utf-8
        )
        target_link_options(${target} PRIVATE
            $<$<NOT:$<STREQUAL:$<TARGET_PROPERTY:TYPE>,STATIC_LIBRARY>>:/DYNAMICBASE>
            $<$<NOT:$<STREQUAL:$<TARGET_PROPERTY:TYPE>,STATIC_LIBRARY>>:/NXCOMPAT>
            $<$<NOT:$<STREQUAL:$<TARGET_PROPERTY:TYPE>,STATIC_LIBRARY>>:/HIGHENTROPYVA>
            $<$<NOT:$<STREQUAL:$<TARGET_PROPERTY:TYPE>,STATIC_LIBRARY>>:/CETCOMPAT>
        )
    endif()
endforeach()

enable_testing()
add_test(
    NAME taskbar_weather_profile_tests
    COMMAND taskbar_weather_profile_tests $<TARGET_FILE:taskbar_weather_host>
)
add_test(
    NAME taskbar_weather_view_model_tests
    COMMAND taskbar_weather_view_model_tests
)
add_test(
    NAME taskbar_weather_detour_tests
    COMMAND taskbar_weather_detour_tests
)
add_test(
    NAME taskbar_weather_tree_profile_tests
    COMMAND taskbar_weather_tree_profile_tests
)
add_test(
    NAME taskbar_weather_mount_tests
    COMMAND taskbar_weather_mount_tests
)
add_test(
    NAME taskbar_weather_slot_geometry_tests
    COMMAND taskbar_weather_slot_geometry_tests
)
add_test(
    NAME taskbar_weather_minhook_smoke_tests
    COMMAND taskbar_weather_minhook_smoke_tests
)
