#ifndef LAURA_APPLICATION_H
#define LAURA_APPLICATION_H

#include "Assistant.h"
#include "Window.h"

class Application
{
public:
	Application();
	~Application();

	// Main methods
	int Start();

	// Get methods
	inline Assistant*& GetAssistant() { return s_Assistant; }
	inline Window*& GetWindow()		  { return s_Window; }

private:
	// Main methods
	int Run();

	// Static attributes
	static Assistant* s_Assistant;
	static Window* s_Window;
};

#endif // !ASSISTANT_APPLICATION_H
