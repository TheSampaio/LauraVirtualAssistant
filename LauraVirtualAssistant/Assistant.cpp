#include "PCH.h"
#include "Assistant.h"

#include "Application.h"

std::unique_ptr<System> Assistant::s_System = nullptr;
std::unique_ptr<Voice>  Assistant::s_Voice = nullptr;

// Allocates memory dynamically
Assistant::Assistant()
{
	s_System = std::make_unique<System>();
	s_Voice = std::make_unique<Voice>();
}

// Starts the assistant
void Assistant::Start()
{
	Initialize();

	s_Voice->Speak(L"Hello " + s_System->GetCurrentUserName());
	s_Voice->Speak(L"My name is Laura, your personal virtual assistant");
	s_Voice->Speak(L"Today is " + s_System->GetCurrentDate());
}

// Updates the assistant
void Assistant::Update()
{
	if (Application::GetInput()->GetKeyTapped(VK_SPACE))
	{
		s_Voice->Speak(L"You tapped spacebar");
	}
}

// Finalizes the assistant
void Assistant::End()
{
	s_Voice->Speak(L"Uninitializing systems");
}

void Assistant::Initialize()
{
	s_Voice->Speak(L"Initializing systems");
	Sleep(500);

	// Registers assistant's hot keys
	s_Voice->Speak(L"Registering hot keys");
	RegisterHotKey(Application::GetWindow()->GetId(), 1, MOD_ALT, 'L');

	Sleep(1000);
	s_Voice->Speak(L"Initializing completed");
}
