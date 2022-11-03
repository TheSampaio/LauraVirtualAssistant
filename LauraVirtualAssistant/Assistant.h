#ifndef LAURA_ASSISTANT_H
#define LAURA_ASSISTANT_H

#include "Voice.h"
#include "Window.h"

class Assistant
{
public:
	Assistant();
	~Assistant();

	// Main methods
	void Start();
	void Update();
	void End();

	// Get methods
	std::wstring GetUserProfile();
	inline Voice*& GetVoice() { return s_Voice; }

	// Set methods
	inline void SetWindow(Window*& Window) { s_Window = Window; }

private:
	// Static attributes
	static Voice* s_Voice;
	static Window* s_Window;
};

#endif // !ASSISTANT_ASSISTANT_H
