#pragma once

#include "Assistant.h"
#include "Debug.h"
#include "Input.h"
#include "Window.h"

class Application
{
public:
	Application();

	// Delete copy contructor and assigment operator
	Application(const Application&) = delete;
	Application& operator=(const Application&) = delete;

	// Main methods
	int Start();

	// Get methods
	static inline Assistant* GetAssistant() { return s_Assistant.get(); }
	static inline Input* GetInput()         { return s_Input.get(); }
	static inline Window* GetWindow()       { return s_Window.get(); }

private:
	// Main methods
	int Run();

	// Static attributes
	static std::unique_ptr<Assistant> s_Assistant;
	static std::unique_ptr<Input> s_Input;
	static std::unique_ptr<Window> s_Window;
};
