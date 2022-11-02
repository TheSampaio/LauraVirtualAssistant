#ifndef LAURA_APPLICATION_H
#define LAURA_APPLICATION_H

#include "Assistant.h"

class Application
{
public:
	Application();
	~Application();

	// Main methods
	void Start();

	// Get methods
	inline Assistant*& GetAssistant() { return s_Assistant; }

private:
	bool m_IsRunning;

	void Run();

	static Assistant* s_Assistant;

};

#endif // !ASSISTANT_APPLICATION_H
