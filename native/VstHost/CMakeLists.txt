cmake_minimum_required(VERSION 3.22)
project(DawsuVstHost VERSION 0.1 LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
if(NOT WIN32 OR NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "This proof requires Windows x64 and an MSVC-compatible compiler")
endif()
include(FetchContent)
FetchContent_Declare(JUCE GIT_REPOSITORY https://github.com/juce-framework/JUCE.git
  GIT_TAG 29396c22c93392d6738e021b83196283d6e4d850 GIT_SHALLOW FALSE)
FetchContent_MakeAvailable(JUCE)
add_library(dawsu_vst3 SHARED host.cpp)
target_compile_definitions(dawsu_vst3 PRIVATE JUCE_PLUGINHOST_VST3=1
  JUCE_WEB_BROWSER=0 JUCE_USE_CURL=0 JUCE_MODAL_LOOPS_PERMITTED=1
  JUCE_DISPLAY_SPLASH_SCREEN=0)
target_link_libraries(dawsu_vst3 PRIVATE juce::juce_audio_processors)
add_executable(dawsu_vst3_scan scanner.cpp)
target_link_libraries(dawsu_vst3_scan PRIVATE dawsu_vst3 ole32)
