#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <vulkan/vulkan_core.h>
#include <vulkan/vulkan_win32.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstring>
#include <stdexcept>
#include <thread>
#include <vector>

/// <summary>Rejects failed Vulkan operations before their handles can be reused.</summary>
static void Check(VkResult result) { if (result != VK_SUCCESS) { throw std::runtime_error("Vulkan operation failed."); } }

/// <summary>Owns one child surface and a GPU presentation lane, independent of the MAUI dispatcher.</summary>
struct Reactor {
    HWND window{};
    std::thread thread;
    std::atomic<bool> stop{false}, visible{false};
    std::atomic<int> status{0}, mode{0};
    int x{-1}, y{-1}, width{}, height{}, shown{-1};
    std::atomic<float> level{0};
    std::atomic<unsigned long long> frames{0};
    VkInstance instance{};
    VkSurfaceKHR surface{};
    VkPhysicalDevice physical{};
    VkDevice device{};
    VkQueue queue{};
    uint32_t family{};
    VkSwapchainKHR swapchain{};
    VkRenderPass pass{};
    VkCommandPool pool{};
    VkCommandBuffer command{};
    VkSemaphore acquired{}, rendered{};
    VkFence completed{};
    VkExtent2D extent{};
    std::vector<VkImageView> views;
    std::vector<VkFramebuffer> buffers;
    std::chrono::steady_clock::time_point started{std::chrono::steady_clock::now()};

    /// <summary>Creates a genuine Win32 Vulkan surface and selects a hardware graphics/presentation queue.</summary>
    void Initialize() {
        VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO}; app.pApplicationName = "Laura reactor"; app.apiVersion = VK_API_VERSION_1_1;
        const char* extensions[]{"VK_KHR_surface", "VK_KHR_win32_surface"};
        VkInstanceCreateInfo info{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO}; info.pApplicationInfo = &app; info.enabledExtensionCount = 2; info.ppEnabledExtensionNames = extensions;
        Check(vkCreateInstance(&info, nullptr, &instance));
        VkWin32SurfaceCreateInfoKHR win{VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR}; win.hinstance = GetModuleHandleW(nullptr); win.hwnd = window;
        Check(vkCreateWin32SurfaceKHR(instance, &win, nullptr, &surface));
        uint32_t count = 0; Check(vkEnumeratePhysicalDevices(instance, &count, nullptr));
        std::vector<VkPhysicalDevice> devices(count); Check(vkEnumeratePhysicalDevices(instance, &count, devices.data()));
        int best = 0;
        for (auto candidate : devices) {
            VkPhysicalDeviceProperties properties{}; vkGetPhysicalDeviceProperties(candidate, &properties);
            int score = properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU ? 3 : properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU ? 2 : 0;
            if (score <= best) { continue; }
            uint32_t queues = 0; vkGetPhysicalDeviceQueueFamilyProperties(candidate, &queues, nullptr);
            std::vector<VkQueueFamilyProperties> families(queues); vkGetPhysicalDeviceQueueFamilyProperties(candidate, &queues, families.data());
            for (uint32_t i = 0; i < queues; ++i) {
                VkBool32 present{}; Check(vkGetPhysicalDeviceSurfaceSupportKHR(candidate, i, surface, &present));
                if (present && (families[i].queueFlags & VK_QUEUE_GRAPHICS_BIT)) { physical = candidate; family = i; best = score; break; }
            }
        }
        if (!physical) { throw std::runtime_error("No compatible hardware graphics queue."); }
        float priority = 1;
        VkDeviceQueueCreateInfo qi{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO}; qi.queueFamilyIndex = family; qi.queueCount = 1; qi.pQueuePriorities = &priority;
        const char* deviceExtensions[]{"VK_KHR_swapchain"};
        VkDeviceCreateInfo di{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO}; di.queueCreateInfoCount = 1; di.pQueueCreateInfos = &qi; di.enabledExtensionCount = 1; di.ppEnabledExtensionNames = deviceExtensions;
        Check(vkCreateDevice(physical, &di, nullptr, &device)); vkGetDeviceQueue(device, family, 0, &queue);
        VkCommandPoolCreateInfo cp{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO}; cp.queueFamilyIndex = family; cp.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        Check(vkCreateCommandPool(device, &cp, nullptr, &pool));
        VkCommandBufferAllocateInfo ca{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO}; ca.commandPool = pool; ca.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; ca.commandBufferCount = 1;
        Check(vkAllocateCommandBuffers(device, &ca, &command));
        VkSemaphoreCreateInfo si{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO}; Check(vkCreateSemaphore(device, &si, nullptr, &acquired)); Check(vkCreateSemaphore(device, &si, nullptr, &rendered));
        VkFenceCreateInfo fi{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO}; fi.flags = VK_FENCE_CREATE_SIGNALED_BIT; Check(vkCreateFence(device, &fi, nullptr, &completed));
    }
    /// <summary>Releases size-dependent resources only after their GPU work has completed.</summary>
    void ClearSwapchain() {
        for (auto buffer : buffers) { vkDestroyFramebuffer(device, buffer, nullptr); } buffers.clear();
        for (auto view : views) { vkDestroyImageView(device, view, nullptr); } views.clear();
        if (pass) { vkDestroyRenderPass(device, pass, nullptr); pass = {}; }
        if (swapchain) { vkDestroySwapchainKHR(device, swapchain, nullptr); swapchain = {}; }
    }
    /// <summary>Creates a FIFO swapchain at the native surface size; resizing stays on the rendering lane.</summary>
    void Resize() {
        Check(vkDeviceWaitIdle(device)); ClearSwapchain();
        VkSurfaceCapabilitiesKHR caps{}; Check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physical, surface, &caps));
        uint32_t count{}; Check(vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, nullptr));
        if (!count) { throw std::runtime_error("No Vulkan surface format."); }
        std::vector<VkSurfaceFormatKHR> formats(count); Check(vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, formats.data()));
        auto format = formats[0]; for (auto item : formats) { if (item.format == VK_FORMAT_B8G8R8A8_UNORM) { format = item; break; } }
        extent = caps.currentExtent;
        if (!extent.width || !extent.height || extent.width == UINT32_MAX) { throw std::runtime_error("Invalid Vulkan surface extent."); }
        VkSwapchainCreateInfoKHR sc{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR}; sc.surface = surface; sc.minImageCount = caps.minImageCount + 1;
        if (caps.maxImageCount && sc.minImageCount > caps.maxImageCount) { sc.minImageCount = caps.maxImageCount; }
        sc.imageFormat = format.format; sc.imageColorSpace = format.colorSpace; sc.imageExtent = extent; sc.imageArrayLayers = 1; sc.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
        sc.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE; sc.preTransform = caps.currentTransform; sc.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR; sc.presentMode = VK_PRESENT_MODE_FIFO_KHR; sc.clipped = VK_TRUE;
        Check(vkCreateSwapchainKHR(device, &sc, nullptr, &swapchain));
        VkAttachmentDescription attachment{}; attachment.format = format.format; attachment.samples = VK_SAMPLE_COUNT_1_BIT; attachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR; attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        attachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE; attachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE; attachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED; attachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        VkAttachmentReference color{0, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL};
        VkSubpassDescription subpass{}; subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS; subpass.colorAttachmentCount = 1; subpass.pColorAttachments = &color;
        VkSubpassDependency dependency{}; dependency.srcSubpass = VK_SUBPASS_EXTERNAL; dependency.dstSubpass = 0; dependency.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT; dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT; dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        VkRenderPassCreateInfo rp{VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO}; rp.attachmentCount = 1; rp.pAttachments = &attachment; rp.subpassCount = 1; rp.pSubpasses = &subpass; rp.dependencyCount = 1; rp.pDependencies = &dependency;
        Check(vkCreateRenderPass(device, &rp, nullptr, &pass));
        count = 0; Check(vkGetSwapchainImagesKHR(device, swapchain, &count, nullptr)); std::vector<VkImage> images(count); Check(vkGetSwapchainImagesKHR(device, swapchain, &count, images.data()));
        for (auto image : images) {
            VkImageViewCreateInfo vi{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO}; vi.image = image; vi.viewType = VK_IMAGE_VIEW_TYPE_2D; vi.format = format.format; vi.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
            VkImageView view{}; Check(vkCreateImageView(device, &vi, nullptr, &view)); views.push_back(view);
            VkFramebufferCreateInfo fb{VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO}; fb.renderPass = pass; fb.attachmentCount = 1; fb.pAttachments = &views.back(); fb.width = extent.width; fb.height = extent.height; fb.layers = 1;
            VkFramebuffer buffer{}; Check(vkCreateFramebuffer(device, &fb, nullptr, &buffer)); buffers.push_back(buffer);
        }
    }
    /// <summary>Clears clipped rectangles in one GPU command, preserving the native orb palette.</summary>
    void Paint(const std::vector<VkClearRect>& pixels, float red, float green, float blue, float alpha = 1.f) {
        if (pixels.empty()) { return; }
        VkClearAttachment color{}; color.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT; color.colorAttachment = 0;
        color.clearValue.color = VkClearColorValue{{(.063f * (1 - alpha) + red * alpha), (.051f * (1 - alpha) + green * alpha),
            (.071f * (1 - alpha) + blue * alpha), 1}};
        vkCmdClearAttachments(command, 1, &color, static_cast<uint32_t>(pixels.size()), pixels.data());
    }
    /// <summary>Adds a bounded pixel rectangle to a GPU draw batch.</summary>
    void Pixel(std::vector<VkClearRect>& pixels, int x, int y, int width = 1, int height = 1) {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > static_cast<int>(extent.width) || y + height > static_cast<int>(extent.height)) { return; }
        VkClearRect rect{}; rect.rect.offset = {x, y}; rect.rect.extent = {static_cast<uint32_t>(width), static_cast<uint32_t>(height)}; rect.layerCount = 1; pixels.push_back(rect);
    }
    /// <summary>Builds one segmented ring using the same radii, sweeps and rotation as the MAUI drawable.</summary>
    void Arc(std::vector<VkClearRect>& pixels, float cx, float cy, float radius, float angle, int segments, int sweep, int width) {
        for (int segment = 0; segment < segments; ++segment) {
            for (int step = 0; step <= sweep; ++step) {
                float theta = (segment * 360.f / segments + step + angle) * .01745329252f;
                Pixel(pixels, static_cast<int>(cx + std::cos(theta) * radius - width / 2),
                    static_cast<int>(cy + std::sin(theta) * radius - width / 2), width, width);
            }
        }
    }
    /// <summary>Draws a small fixed capital glyph without creating an image or a CPU raster frame.</summary>
    void Letter(std::vector<VkClearRect>& pixels, char letter, int x, int y, int scale) {
        const char* glyph = letter == 'A' ? "011101000110001111111000110001" : letter == 'E' ? "11111100001000011110100001000011111"
            : letter == 'I' ? "11111001000010000100001000010011111" : letter == 'L' ? "10000100001000010000100001000011111"
            : letter == 'N' ? "10001110011010110011100011000110001" : letter == 'O' ? "01110100011000110001100011000101110"
            : letter == 'P' ? "11110100011000111110100001000010000" : letter == 'R' ? "11110100011000111110101001001010001"
            : letter == 'S' ? "01111100001000001110000010000111110" : letter == 'U' ? "10001100011000110001100011000101110" : nullptr;
        if (!glyph) { return; }
        for (int row = 0; row < 7; ++row) { for (int column = 0; column < 5; ++column) {
            if (glyph[row * 5 + column] == '1') { Pixel(pixels, x + column * scale, y + row * scale, scale, scale); }
        } }
    }
    /// <summary>Draws the same two central labels shown in the normal orb.</summary>
    void Label(std::vector<VkClearRect>& pixels, const char* value, int cx, int y, int scale) {
        int count = static_cast<int>(strlen(value)); int x = cx - count * 6 * scale / 2;
        for (int index = 0; index < count; ++index) { Letter(pixels, value[index], x + index * 6 * scale, y, scale); }
    }
    /// <summary>Records the normal orb's red rings, grid, ticks, center and labels with Vulkan attachment clears.</summary>
    void Rings(float angle) {
        const float cx = extent.width * .5f, cy = extent.height * .5f;
        const float radius = std::min(extent.width, extent.height) * .39f;
        const bool speaking = mode.load() == 2;
        const float red = speaking ? 1.f : 246.f / 255.f, green = speaking ? 37.f / 255.f : 51.f / 255.f;
        const float blue = speaking ? 76.f / 255.f : 80.f / 255.f;
        std::vector<VkClearRect> pixels; pixels.reserve(2048);
        for (int x = 0; x < static_cast<int>(extent.width); x += 40) { Pixel(pixels, x, 0, 1, extent.height); }
        for (int y = 0; y < static_cast<int>(extent.height); y += 40) { Pixel(pixels, 0, y, extent.width, 1); }
        Paint(pixels, 37.f / 255.f, 20.f / 255.f, 32.f / 255.f); pixels.clear();
        Arc(pixels, cx, cy, radius, 0, 1, 359, 1); Arc(pixels, cx, cy, radius * .94f, 0, 1, 359, 1);
        Paint(pixels, red, green, blue, .28f); pixels.clear();
        Arc(pixels, cx, cy, radius * .89f, angle, 3, 84, 3);
        Arc(pixels, cx, cy, radius * .73f, -angle * 1.3f + 40.f, 3, 90, 11);
        Arc(pixels, cx, cy, radius * .58f, angle * .7f, 2, 145, 2);
        Paint(pixels, red, green, blue); pixels.clear();
        for (int index = 0; index < 90; ++index) {
            float theta = (index * 4.f - angle * .35f) * .01745329252f;
            for (float r = radius * (index % 5 == 0 ? .775f : .797f); r <= radius * .81f; r += 1.f) {
                Pixel(pixels, static_cast<int>(cx + std::cos(theta) * r), static_cast<int>(cy + std::sin(theta) * r), index % 5 == 0 ? 2 : 1, index % 5 == 0 ? 2 : 1);
            }
        }
        Paint(pixels, red, green, blue, .65f); pixels.clear();
        float center = radius * .43f;
        for (int dy = -static_cast<int>(center); dy <= static_cast<int>(center); ++dy) {
            int half = static_cast<int>(std::sqrt(std::max(0.f, center * center - dy * dy)));
            Pixel(pixels, static_cast<int>(cx) - half, static_cast<int>(cy) + dy, half * 2 + 1, 1);
        }
        Paint(pixels, 39.f / 255.f, 16.f / 255.f, 27.f / 255.f); pixels.clear();
        Arc(pixels, cx, cy, center, 0, 1, 359, 2);
        for (int index = 0; index < 25; ++index) {
            float theta = (index * 3.f + angle) * .01745329252f;
            for (float r = radius * .35f; r <= radius * .39f; r += 1.f) {
                Pixel(pixels, static_cast<int>(cx + std::cos(theta) * r), static_cast<int>(cy + std::sin(theta) * r));
            }
        }
        Paint(pixels, red, green, blue, .8f); pixels.clear();
        Label(pixels, "L A U R A", static_cast<int>(cx), static_cast<int>(cy) - 11, 2);
        Paint(pixels, .94f, .9f, .92f); pixels.clear();
        Label(pixels, "PERSONAL AI", static_cast<int>(cx), static_cast<int>(cy) + 15, 1);
        Paint(pixels, red, green, blue);
    }
    /// <summary>Presents one complete GPU frame using explicit semaphore/fence ownership.</summary>
    void Draw() {
        RECT bounds{}; GetClientRect(window, &bounds);
        if (!swapchain || static_cast<uint32_t>(bounds.right) != extent.width || static_cast<uint32_t>(bounds.bottom) != extent.height) { Resize(); }
        auto waited = vkWaitForFences(device, 1, &completed, VK_TRUE, 1000000000);
        if (waited == VK_TIMEOUT) { return; } Check(waited);
        uint32_t index{}; auto result = vkAcquireNextImageKHR(device, swapchain, 100000000, acquired, VK_NULL_HANDLE, &index);
        if (result == VK_ERROR_OUT_OF_DATE_KHR) { Resize(); return; } if (result == VK_TIMEOUT) { return; } if (result != VK_SUBOPTIMAL_KHR) { Check(result); }
        Check(vkResetCommandBuffer(command, 0)); VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO}; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT; Check(vkBeginCommandBuffer(command, &begin));
        VkClearValue background{}; background.color = {{16.f / 255.f,13.f / 255.f,18.f / 255.f,1}};
        VkRenderPassBeginInfo rp{VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO}; rp.renderPass = pass; rp.framebuffer = buffers[index]; rp.renderArea.extent = extent; rp.clearValueCount = 1; rp.pClearValues = &background;
        vkCmdBeginRenderPass(command, &rp, VK_SUBPASS_CONTENTS_INLINE);
        float seconds = std::chrono::duration<float>(std::chrono::steady_clock::now() - started).count(); Rings(seconds * (mode.load() ? 30.f : 14.f));
        vkCmdEndRenderPass(command); Check(vkEndCommandBuffer(command)); Check(vkResetFences(device, 1, &completed));
        VkPipelineStageFlags stage = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO}; submit.waitSemaphoreCount = 1; submit.pWaitSemaphores = &acquired; submit.pWaitDstStageMask = &stage; submit.commandBufferCount = 1; submit.pCommandBuffers = &command; submit.signalSemaphoreCount = 1; submit.pSignalSemaphores = &rendered;
        Check(vkQueueSubmit(queue, 1, &submit, completed));
        VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR}; present.waitSemaphoreCount = 1; present.pWaitSemaphores = &rendered; present.swapchainCount = 1; present.pSwapchains = &swapchain; present.pImageIndices = &index;
        result = vkQueuePresentKHR(queue, &present); if (result != VK_ERROR_OUT_OF_DATE_KHR && result != VK_SUBOPTIMAL_KHR) { Check(result); }
        Check(vkQueueWaitIdle(queue)); ++frames;
    }
    /// <summary>Runs rendering and releases Vulkan resources on the same dedicated thread, including failure paths.</summary>
    void Run() {
        try {
            Initialize(); status = 1;
            while (!stop) {
                if (visible && IsWindowVisible(window) && !IsIconic(GetAncestor(window, GA_ROOT))) { Draw(); }
                std::this_thread::sleep_for(std::chrono::milliseconds(visible ? 33 : 100));
            }
        } catch (...) { status = -1; }
        if (device) {
            vkDeviceWaitIdle(device); ClearSwapchain();
            if (completed) { vkDestroyFence(device, completed, nullptr); }
            if (rendered) { vkDestroySemaphore(device, rendered, nullptr); } if (acquired) { vkDestroySemaphore(device, acquired, nullptr); }
            if (pool) { vkDestroyCommandPool(device, pool, nullptr); } vkDestroyDevice(device, nullptr);
        }
        if (surface) { vkDestroySurfaceKHR(instance, surface, nullptr); } if (instance) { vkDestroyInstance(instance, nullptr); }
    }
};

/// <summary>Creates an owned child HWND on the UI thread and initializes Vulkan asynchronously.</summary>
extern "C" __declspec(dllexport) Reactor* LauraVulkanCreate(HWND parent) {
    try { auto reactor = new Reactor(); reactor->window = CreateWindowExW(0, L"STATIC", L"", WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 1, 1, parent, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!reactor->window) { delete reactor; return nullptr; }
        reactor->thread = std::thread([reactor] { reactor->Run(); }); return reactor;
    } catch (...) { return nullptr; }
}
/// <summary>Updates child placement and atomic visual inputs without waiting for the GPU.</summary>
extern "C" __declspec(dllexport) void LauraVulkanUpdate(Reactor* reactor, int x, int y, int width, int height, int visible, int mode, float level) {
    if (!reactor) { return; } reactor->mode = mode; reactor->level = level; reactor->visible = visible != 0;
    if (reactor->x != x || reactor->y != y || reactor->width != width || reactor->height != height || reactor->shown != visible) {
        reactor->x = x; reactor->y = y; reactor->width = width; reactor->height = height; reactor->shown = visible;
        SetWindowPos(reactor->window, HWND_TOP, x, y, std::max(1,width), std::max(1,height), SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
    }
}
/// <summary>Returns initialization/failure status and an actual presented-frame count.</summary>
extern "C" __declspec(dllexport) int LauraVulkanStatus(Reactor* reactor, unsigned long long* frames) { if (!reactor) { return -1; } *frames = reactor->frames.load(); return reactor->status.load(); }
/// <summary>Stops and joins the rendering lane; callers execute this operation outside the UI dispatcher.</summary>
extern "C" __declspec(dllexport) void LauraVulkanStop(Reactor* reactor) { if (reactor) { reactor->stop = true; if (reactor->thread.joinable()) { reactor->thread.join(); } } }
/// <summary>Destroys the owned child HWND on its creating UI thread after rendering has stopped.</summary>
extern "C" __declspec(dllexport) void LauraVulkanDestroy(Reactor* reactor) { if (reactor) { DestroyWindow(reactor->window); delete reactor; } }
