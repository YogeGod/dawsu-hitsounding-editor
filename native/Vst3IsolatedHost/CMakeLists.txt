# dawsu! isolated VST3 instrument host: one process per Keysound track instrument (see host.cpp).
# Built on the Steinberg VST 3 SDK, MIT-licensed since 3.8. Configure with -DVST3_SDK_ROOT=<vst3sdk checkout>.
cmake_minimum_required(VERSION 3.22)

if(NOT VST3_SDK_ROOT)
    message(FATAL_ERROR "Set VST3_SDK_ROOT to a VST 3 SDK 3.8+ checkout (base, pluginterfaces, public.sdk, cmake).")
endif()

list(APPEND CMAKE_MODULE_PATH "${VST3_SDK_ROOT}/cmake/modules")
include(SMTG_VST3_SDK)
project(dawsu_vst3_host LANGUAGES CXX)

smtg_setup_platform_toolset()
set(SDK_ROOT "${VST3_SDK_ROOT}")
set(public_sdk_SOURCE_DIR "${SDK_ROOT}/public.sdk")
set(pluginterfaces_SOURCE_DIR "${SDK_ROOT}/pluginterfaces")
include_directories(${SDK_ROOT})

smtg_create_lib_base_target()
smtg_create_pluginterfaces_target()
smtg_create_public_sdk_common_target()
smtg_create_public_sdk_hosting_target()

# sdk_hosting leaves these to the host program, as the SDK's editorhost sample does.
add_executable(dawsu_vst3_host
    host.cpp
    ${SDK_ROOT}/public.sdk/source/vst/hosting/module_win32.cpp
    ${SDK_ROOT}/public.sdk/source/vst/hosting/plugprovider.cpp
    ${SDK_ROOT}/public.sdk/source/common/memorystream.cpp
)
target_compile_features(dawsu_vst3_host PRIVATE cxx_std_17)
target_compile_definitions(dawsu_vst3_host PRIVATE UNICODE _UNICODE)
target_link_libraries(dawsu_vst3_host PRIVATE sdk_hosting ole32 crypt32)
if(MSVC)
    target_link_options(dawsu_vst3_host PRIVATE /ENTRY:wmainCRTStartup)
endif()
