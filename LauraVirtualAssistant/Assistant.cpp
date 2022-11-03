#include "PCH.h"
#include "Assistant.h"

Voice* Assistant::s_Voice = nullptr;
Window* Assistant::s_Window = nullptr;

// Allocates memory dynamically
Assistant::Assistant()
{
	s_Voice = new Voice;
}

// Deletes allocated memory
Assistant::~Assistant()
{
	delete s_Voice;
}

// Starts the assistant
void Assistant::Start()
{
	s_Voice->Speak(L"Initializing systems");
	Sleep(500);

	// Registers assistant's hot keys
	s_Voice->Speak(L"Registering hot keys");
	RegisterHotKey(s_Window->GetId(), 1, MOD_ALT, 'L');

	Sleep(500);
	s_Voice->Speak(L"Initializing completed");
}

// Updates the assistant
void Assistant::Update()
{
	if (s_Window->GetKeyPressed(VK_SPACE))
	{
		s_Voice->Speak(L"Yes baby");
	}
}

// Finalizes the assistant
void Assistant::End()
{
	s_Voice->Speak(L"Uninitializing systems");
}

// Get current user profile
std::wstring Assistant::GetUserProfile()
{
	TCHAR UserFolderPath[MAX_PATH];
	std::vector<TCHAR> NewUserFolderPath;

	if SUCCEEDED(SHGetFolderPath(NULL, CSIDL_PROFILE, NULL, 0, UserFolderPath))
	{
		for (unsigned int i = 0; (i < sizeof(UserFolderPath) / sizeof(UserFolderPath[0]) - 1) && (UserFolderPath[i] != '\0'); i++)
		{
			NewUserFolderPath.push_back(UserFolderPath[i]);
		}
	}

	return std::wstring(UserFolderPath);
}
