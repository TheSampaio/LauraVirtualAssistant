# Optional Vulkan reactor

This project renders an owned Win32 child surface using a genuine hardware Vulkan
device, swapchain, attachment operations and presentation queue. It does not claim
to replace the Windows compositor used by native MAUI/WinUI controls. Rendering,
GPU waits and swapchain recreation stay on a dedicated lane. Hiding or navigating
away suspends presentation. The standard renderer remains available if Vulkan
initialization fails. No Vulkan SDK or separate graphics program is needed by users;
the Vulkan loader comes from their GPU driver.

Build: `pwsh -NoProfile -File Build/Build-VulkanRenderer.ps1` (also run by the App build).
MSVC x64 tools are needed only on the development machine. The renderer uses the
static C++ runtime and the system GPU driver's Vulkan loader.

Vendored Vulkan headers: KhronosGroup/Vulkan-Headers tag v1.4.335;
tag object d55a082b9b4ae364e7faee0d2f44643c69f89bc4;
source ZIP SHA-256 41db700705915538a1dd66ecd20f4a0853e41db46539231ab1aad60528ea8e00.
Only required public headers are included. See Vulkan-Headers-LICENSE.md and their
SPDX copyright headers. The Laura renderer source follows the repository license.
