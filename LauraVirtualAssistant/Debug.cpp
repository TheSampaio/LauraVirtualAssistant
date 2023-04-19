#include "PCH.h"
#include "Debug.h"

#include "Application.h"

void Debug::IMessage(EDebugMode DebugMode, LPCWSTR Text, LPCWSTR Caption)
{
	switch (DebugMode)
	{
	case Information:
		MessageBox(Application::GetWindow()->GetId(), Text, Caption, MB_OK | MB_ICONINFORMATION); break;

	case Warning:
		MessageBox(Application::GetWindow()->GetId(), Text, Caption, MB_OK | MB_ICONWARNING); break;

	case Error:
		MessageBox(Application::GetWindow()->GetId(), Text, Caption, MB_OK | MB_ICONERROR); break;

	default:
		MessageBox(Application::GetWindow()->GetId(), Text, Caption, NULL); break;
	}
}
