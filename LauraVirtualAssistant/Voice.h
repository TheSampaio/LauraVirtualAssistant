#pragma once

class Voice
{
public:
	Voice();
	~Voice();

	// Delete copy contructor and assigment operator
	Voice(const Voice&) = delete;
	Voice& operator=(const Voice&) = delete;

	// Main methods
	void Speak(const std::wstring& Text);

private:
	// Attributes
	ISpVoice* m_Voice;
	HRESULT m_Result;
};
