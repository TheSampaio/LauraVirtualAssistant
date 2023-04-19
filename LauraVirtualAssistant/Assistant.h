#pragma once

#include "Voice.h"
#include "System.h"

class Assistant
{
public:
	Assistant();

	// Delete copy contructor and assigment operator
	Assistant(const Assistant&) = delete;
	Assistant& operator=(const Assistant&) = delete;

	// Main methods
	void Start();
	void Update();
	void End();

	// Get methods
	inline Voice* GetVoice()   { return s_Voice.get(); }
	inline System* GetSystem() { return s_System.get(); }

private:
	// Main methods
	void Initialize();

	// Static attributes
	static std::unique_ptr<System> s_System;
	static std::unique_ptr<Voice> s_Voice;
};
