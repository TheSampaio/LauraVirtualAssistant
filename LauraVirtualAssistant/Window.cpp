#include "PCH.h"
#include "Window.h"

bool Window::s_bIsHidden = true;
bool Window::s_Keys[256] = { false };
std::array<int, 2> Window::s_Mouse = { 0 };

Window::Window()
	: m_Id(NULL), m_Instance(GetModuleHandle(NULL))
{
	// Style
	m_Style = WS_OVERLAPPED | WS_MINIMIZEBOX | WS_EX_TOPMOST;

	// Title
	m_Title = std::string("Window");

	// Screen's size
	m_Screen[0] = GetSystemMetrics(SM_CXSCREEN);
	m_Screen[1] = GetSystemMetrics(SM_CYSCREEN);

	// Window's size
	m_Size[0] = 800;
	m_Size[1] = 600;

	// Window's center
	m_Center[0] = m_Size[0] / 2;
	m_Center[1] = m_Size[1] / 2;

	// Window's position
	m_Position[0] = 0;
	m_Position[1] = 0;

	// Window's icon and cursor
	m_Icon = NULL;
	m_Cursor = NULL;

	// Window's display mode and background's color
	m_DisplayMode = EDisplayMode::WINDOWED;
	m_BackgroundColor = RGB(0, 0, 0);
}

bool Window::Create()
{
	// Create and define window's class
	WNDCLASSEX Window{ NULL };
	Window.cbSize = sizeof(Window);
	Window.lpfnWndProc = Window::Procedure;
	Window.style = CS_HREDRAW | CS_VREDRAW;
	Window.cbClsExtra = NULL;
	Window.cbWndExtra = NULL;
	Window.lpszMenuName = NULL;
	Window.hCursor = m_Cursor;
	Window.hIcon = m_Icon;
	Window.hIconSm = m_Icon;
	Window.hInstance = m_Instance;
	Window.hbrBackground = static_cast<HBRUSH>(CreateSolidBrush(m_BackgroundColor));
	Window.lpszClassName = L"BasicWindow";

	// Register window's class
	(!RegisterClassEx(&Window)) ? false : true;

	// Create window
	m_Id = CreateWindowEx
	(
		NULL,												  // Window's EXTRA style
		L"BasicWindow",										  // Window's class's name
		std::wstring(m_Title.begin(), m_Title.end()).c_str(), // Window's title
		m_Style,											  // Window's DEFAULT style
		m_Position[0], m_Position[1],						  // Window's position
		m_Size[0], m_Size[1],								  // Window's size
		NULL,												  // Window's parent
		NULL,												  // Window's menu
		m_Instance,											  // Window's instance (Id)
		NULL												  // Window's long pointer param
	);

	// Setup client area
	if (m_DisplayMode == EDisplayMode::WINDOWED)
	{
		// Create new rect
		RECT Rect{ 0, 0, static_cast<LONG>(m_Size[0]), static_cast<LONG>(m_Size[1]) };

		// Change current window's rect to the new one
		AdjustWindowRectEx
		(
			&Rect,
			GetWindowStyle(m_Id),
			GetMenu(m_Id) != NULL,
			GetWindowStyle(m_Id)
		);

		// Update window's position
		m_Position[0] = (m_Screen[0] / 2) - ((Rect.right - Rect.left) / 2);
		m_Position[1] = (m_Screen[1] / 2) - ((Rect.bottom - Rect.top) / 2);

		// Apply changes
		MoveWindow(m_Id, m_Position[0], m_Position[1], m_Size[0], m_Size[1], true);
	}

	return (m_Id) ? true : false;
}

void Window::SetSize(unsigned int Width, unsigned int Height)
{
	// Window's size
	m_Size[0] = Width;
	m_Size[1] = Height;

	// Window's center
	m_Center[0] = m_Size[0] / 2;
	m_Center[1] = m_Size[1] / 2;

	// Adust window to screen's center
	m_Position[0] = (GetSystemMetrics(SM_CXSCREEN) / 2) - (m_Size[0] / 2);
	m_Position[1] = (GetSystemMetrics(SM_CYSCREEN) / 2) - (m_Size[1] / 2);
}

void Window::SetDisplayMode(unsigned int DisplayMode)
{
	m_DisplayMode = DisplayMode;

	// Windowed mode
	if (m_DisplayMode == EDisplayMode::WINDOWED)
	{
		m_Style = WS_OVERLAPPED | WS_SYSMENU | WS_MINIMIZEBOX | WS_EX_TOPMOST | WS_VISIBLE;
	}

	// Bordless or fullscreen modes
	else
	{
		// Resizes window
		m_Size[0] = m_Screen[0];
		m_Size[1] = m_Screen[1];


		// Setup window's new position
		m_Position[0] = 0;
		m_Position[1] = 0;

		m_Style = WS_POPUP | WS_EX_TOPMOST | WS_VISIBLE;
	}
}

LRESULT Window::Procedure(HWND hWindow, UINT uMessage, WPARAM wParam, LPARAM lParam)
{
	switch (uMessage)
	{
		/* ========== KEYBOARD ==================== */
		// If keyboard's key was pressed
	case WM_KEYDOWN:
	case WM_SYSKEYDOWN:
		s_Keys[wParam] = true;

		if (s_Keys[VK_F4])
		{
			// Ask if user want to close the window
			if (MessageBox(hWindow, L"Do you really want to close the window?", L"Window", MB_YESNO | MB_DEFBUTTON2 | MB_ICONWARNING) == IDYES)
			{
				PostMessage(hWindow, WM_DESTROY, NULL, NULL);
				return 0;
			}

			else
			{
				s_Keys[VK_F4] = false;
				return 0;
			}
		}

		return 0;

		// If keyboard's key was released
	case WM_KEYUP:
	case WM_SYSKEYUP:
		s_Keys[wParam] = false;
		return 0;

		/* ========== MOUSE ==================== */
		// If mouse move
	case WM_MOUSEMOVE:
		s_Mouse[0] = GET_X_LPARAM(lParam);
		s_Mouse[1] = GET_Y_LPARAM(lParam);
		return 0;

		// If mouse's left button pressed
	case WM_LBUTTONDOWN:
	case WM_LBUTTONDBLCLK:
		s_Keys[VK_LBUTTON] = true;
		return 0;

		// If mouse's left button released
	case WM_LBUTTONUP:
		s_Keys[VK_LBUTTON] = false;
		return 0;

		// If mouse's right button pressed
	case WM_RBUTTONDOWN:
	case WM_RBUTTONDBLCLK:
		s_Keys[VK_RBUTTON] = true;
		return 0;

		// If mouse's right button released
	case WM_RBUTTONUP:
		s_Keys[VK_RBUTTON] = false;
		return 0;

		/* ========== WINDOW ==================== */

		// If a hot key was pressed
	case WM_HOTKEY:
		ShowWindow(hWindow, s_bIsHidden);
		s_bIsHidden = !s_bIsHidden;
		return 0;

		// If window was closed
	case WM_QUIT:
	case WM_CLOSE:
		PostMessage(hWindow, WM_DESTROY, NULL, NULL);
		return 0;

		// If window was destroyed
	case WM_DESTROY:
		PostQuitMessage(NULL);
		return 0;

		// Else return default behavior
	default:
		return DefWindowProc(hWindow, uMessage, wParam, lParam);
	}
}