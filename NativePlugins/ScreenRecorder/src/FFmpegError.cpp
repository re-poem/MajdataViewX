#include "FFmpegError.h"

extern "C" {
#include <libavutil/error.h>
}

namespace {
thread_local std::string g_lastError;
}

std::string SrAvError(int result, const char* operation)
{
    char buffer[AV_ERROR_MAX_STRING_SIZE] = {};
    av_strerror(result, buffer, sizeof(buffer));
    return std::string("FFmpeg could not ") + operation + ": " + buffer + " (" + std::to_string(result) + ")";
}

void SrSetLastError(std::string message)
{
    g_lastError = std::move(message);
}

const char* SrGetLastError()
{
    return g_lastError.empty() ? "Native screen recorder failed." : g_lastError.c_str();
}
