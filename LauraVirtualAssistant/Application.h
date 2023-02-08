#ifndef LAURA_APPLICATION_H
#define LAURA_APPLICATION_H

#include "Assistant.h"
#include "Input.h"
#include "Window.h"

class Application
{
public:
	Application();
	~Application();

	// Deleting copy constructors
	Application(const Application&) = delete;
	Application& operator=(const Application&) = delete;

	// Main methods
	int Start();

	// Get methods
	inline Assistant*& GetAssistant() { return s_Assistant; }
	inline Input*& GeInput()		  { return s_Input; }
	inline Window*& GetWindow()		  { return s_Window; }

	// Friends
	friend Assistant;

private:
	// Main methods
	int Run();

	// Static attributes
	static Assistant* s_Assistant;
	static Input* s_Input;
	static Window* s_Window;
};

#endif // !LAURA_APPLICATION_H
