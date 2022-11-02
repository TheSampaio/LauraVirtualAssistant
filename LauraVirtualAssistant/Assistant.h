#ifndef LAURA_ASSISTANT_H
#define LAURA_ASSISTANT_H

#include "Voice.h"

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
	inline Voice*& GetVoice() { return s_Voice; }

private:	
	static Voice* s_Voice;
};

#endif // !ASSISTANT_ASSISTANT_H
