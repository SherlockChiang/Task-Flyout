cmake_minimum_required(VERSION 3.25)

project(TaskFlyoutTaskbarWeather LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Task Flyout taskbar weather supports Windows x64 only.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_library(taskbar_weather_profile STATIC
    src/module_profile.cpp
)
target_include_directories(taskbar_weather_profile PUBLIC include)
target_compile_definitions(taskbar_weather_profile PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_profile PUBLIC user32)

add_library(taskbar_weather_host SHARED
    src/host.cpp
    src/host.def
)
set_target_properties(taskbar_weather_host PROPERTIES
    OUTPUT_NAME "TaskFlyout.TaskbarHost"
)
target_include_directories(taskbar_weather_host PRIVATE include)
target_compile_definitions(taskbar_weather_host PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(taskbar_weather_host PRIVATE taskbar_weather_profile user32)

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

foreach(target
        taskbar_weather_profile
        taskbar_weather_host
        taskbar_weather_broker
        taskbar_weather_profile_tests)
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

