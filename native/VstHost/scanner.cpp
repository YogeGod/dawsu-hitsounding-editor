#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

extern "C" __declspec(dllimport) int dv_scan(const char*, char*, int);

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    const auto apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(apartment)) return 3;
    const auto length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, argv[1], -1, nullptr, 0, nullptr, nullptr);
    if (length <= 1) { CoUninitialize(); return 2; }
    std::string path(static_cast<size_t>(length), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, argv[1], -1, path.data(), length, nullptr, nullptr);
    std::vector<char> xml(65536);
    const int size = dv_scan(path.c_str(), xml.data(), static_cast<int>(xml.size()));
    if (size <= 0) { CoUninitialize(); return 4; }
    std::ofstream output(std::filesystem::path(argv[2]), std::ios::binary | std::ios::trunc);
    output.write(xml.data(), size);
    output.close();
    CoUninitialize();
    return output ? 0 : 5;
}
