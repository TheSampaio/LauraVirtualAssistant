#include "PCH.h"
#include "Input.h"

#include "Application.h"

bool Input::s_bIsHidden = true;
bool Input::s_bFlags[256] = { false };
bool Input::s_bKeys[256] = { false };

short              Input::s_MouseWheel = 0;
std::array<int, 2> Input::s_MousePosition = { 0 };

bool Input::GetKeyTapped(int KeyCode)
{
	if (GetKeyPressed(KeyCode))
	{
		s_bFlags[KeyCode] = true;
	}

	else if (s_bFlags[KeyCode])
	{
		if (GetKeyReleased(KeyCode))
		{
			s_bFlags[KeyCode] = false;
			return true;
		}
	}

	return false;
}

LRESULT Input::Procedure(HWND hWindow, UINT uMessage, WPARAM wParam, LPARAM lParam)
{
	switch (uMessage)
	{
	/* ==================== KEYBOARD ==================== */
		// If keyboard's key was pressed
	case WM_KEYDOWN:
	case WM_SYSKEYDOWN:
		s_bKeys[wParam] = true;

		if (s_bKeys[VK_F4])
		{
			// Ask if user want to close the window
			if (MessageBox(hWindow, L"Do you really want to close the window?", L"Window", MB_YESNO | MB_DEFBUTTON2 | MB_ICONWARNING) == IDYES)
			{
				PostMessage(hWindow, WM_DESTROY, NULL, NULL);
				return 0;
			}

			else
			{
				s_bKeys[VK_F4] = false;
				return 0;
			}
		}

		return 0;

		// If keyboard's key was released
	case WM_KEYUP:
	case WM_SYSKEYUP:
		s_bKeys[wParam] = false;
		return 0;

	/* ==================== MOUSE ==================== */
		// If mouse move
	case WM_MOUSEMOVE:
		s_MousePosition[0] = GET_X_LPARAM(lParam);
		s_MousePosition[1] = GET_Y_LPARAM(lParam);
		return 0;

		// If mouse's left button pressed
	case WM_LBUTTONDOWN:
	case WM_LBUTTONDBLCLK:
		s_bKeys[VK_LBUTTON] = true;
		return 0;

		// If mouse's left button released
	case WM_LBUTTONUP:
		s_bKeys[VK_LBUTTON] = false;
		return 0;

		// If mouse's right button pressed
	case WM_RBUTTONDOWN:
	case WM_RBUTTONDBLCLK:
		s_bKeys[VK_RBUTTON] = true;
		return 0;

		// If mouse's right button released
	case WM_RBUTTONUP:
		s_bKeys[VK_RBUTTON] = false;
		return 0;

	case WM_MOUSEHWHEEL:
		s_MouseWheel = GET_WHEEL_DELTA_WPARAM(wParam);
		return 0;

	/* ==================== WINDOW ==================== */
		// If a hot key was pressed
	case WM_HOTKEY:
		ShowWindow(hWindow, s_bIsHidden);
		s_bIsHidden = !s_bIsHidden;
		return 0;

		// If window was focused
	case WM_SETFOCUS:
		return 0;

		// If window was NOT focused
	case WM_KILLFOCUS:
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