#include "PCH.h"
#include "Application.h"

Assistant* Application::s_Assistant = nullptr;
Input*	   Application::s_Input = nullptr;
Window*	   Application::s_Window = nullptr;

// Allocates memory dynamically
Application::Application()
{
	s_Assistant = new Assistant;
	s_Input = new Input;
	s_Window = new Window;
}

// Deletes allocated memory
Application::~Application()
{
	delete s_Assistant;
	delete s_Input;
	delete s_Window;
}

// Starts the application
int Application::Start()
{
	// Create window and verify if was succeed
	if (!s_Window->Create())
	{
		MessageBox(NULL, L"Failed to create window.", L"Application", MB_OK | MB_ICONERROR);
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
