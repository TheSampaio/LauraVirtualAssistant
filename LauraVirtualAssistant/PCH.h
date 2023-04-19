#pragma once

// Standard
#include <array>
#include <chrono>
#include <format>
#include <iostream>
#include <memory>
#include <vector>
#include <string>

// External
#include <sapi.h>
#include <ShlObj_core.h>

// Windows
#ifndef WIN32_MEAN_AND_LEAN
#define WIN32_MEAN_AND_LEAN
	#include <windows.h>
	#include <windowsx.h>
#endif
