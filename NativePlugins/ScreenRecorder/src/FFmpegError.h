#pragma once

#include <string>

std::string SrAvError(int result, const char* operation);
void SrSetLastError(std::string message);
const char* SrGetLastError();
