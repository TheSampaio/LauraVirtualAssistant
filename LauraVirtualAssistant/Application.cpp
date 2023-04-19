#include "PCH.h"
#include "Application.h"

std::unique_ptr<Assistant> Application::s_Assistant = nullptr;
std::unique_ptr<Input>     Application::s_Input = nullptr;
std::unique_ptr<Window>    Application::s_Window = nullptr;

// Allocates memory dynamically
Application::Application()
{
	s_Assistant = std::make_unique<Assistant>();
	s_Input = std::make_unique<Input>();
	s_Window = std::make_unique<Window>();
}

// Starts the application
int Application::Start()
{
	// Create window and verify if was succeed
	if (!s_Window->Create())
	{
		Debug::Message(Error, L"Failed to create window.", L"Application");
		return EXIT_FAILURE;
	}

	// Runs the application
	return Run();
}

// Runs the application
int Application::Run()
{
	// Starts the assistant
	MSG Message{ NULL };
	s_Assistant->Start();

	// Main loop
	do
	{
		// Process all events
		if (PeekMessage(&Message, NULL, 0, 0, PM_REMOVE))
		{
			TranslateMessage(&Message);
			DispatchMessage(&Message);
		}

		else
		{
			s_Assistant->Update();
			Sleep(10);
		}

	} while (Message.message != WM_QUIT);

	// Finalizes the assistant and application
	s_Assistant->End();
	return static_cast<int>(Message.wParam);
}
