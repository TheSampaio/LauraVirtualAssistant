#ifndef LAURA_VOICE_H
#define LAURA_VOICE_H

class Voice
{
public:
	Voice();
	~Voice();

	// Deleting copy constructors
	Voice(const Voice&) = delete;
	Voice& operator=(const Voice&) = delete;

	// Main methods
	void Speak(const std::wstring& Text);

private:
	// Attributes
	ISpVoice* m_Voice;
	HRESULT m_Result;
};

#endif // !LAURA_VOICE_H
