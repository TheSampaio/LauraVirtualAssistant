#ifndef LAURA_ASSISTANT_H
#define LAURA_ASSISTANT_H

#include "Voice.h"
#include "Input.h"
#include "System.h"
#include "Window.h"

class Assistant
{
public:
	Assistant();
	~Assistant();

	// Deleting copy constructors
	Assistant(const Assistant&) = delete;
	Assistant& operator=(const Assistant&) = delete;

	// Main methods
	void Start();
	void Update();
	void End();

	// Get methods
	inline Voice*& GetVoice() { return s_Voice; }

private:
	// Static attributes
	static System* s_System;
	static Voice* s_Voice;

	static Input*& s_Input;
	static Window*& s_Window;
};

#endif // !LAURA_ASSISTANT_H
