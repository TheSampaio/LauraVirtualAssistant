#include "PCH.h"
#include "Assistant.h"

#include "Application.h"

System* Assistant::s_System = nullptr;
Voice* Assistant::s_Voice = nullptr;

Input*& Assistant::s_Input = Application::s_Input;
Window*& Assistant::s_Window = Application::s_Window;

// Allocates memory dynamically
Assistant::Assistant()
{
	s_System = new System;
	s_Voice = new Voice;
}

// Deletes allocated memory
Assistant::~Assistant()
{
	delete s_System;
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

	Sleep(1000);
	s_Voice->Speak(L"Initializing completed");
}

// Updates the assistant
void Assistant::Update()
{
	if (s_Input->GetKeyTaped(VK_SPACE))
	{
		s_Voice->Speak(L"You taped spacebar");
	}
}

// Finalizes the assistant
void Assistant::End()
{
	s_Voice->Speak(L"Uninitializing systems");
}
