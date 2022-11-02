#ifndef LAURA_VOICE_H
#define LAURA_VOICE_H

class Voice
{
public:
	Voice();
	~Voice();

	void Speak(const std::wstring& Text);

private:
	ISpVoice* m_Voice;
	HRESULT m_Result;
};

#endif // !ASSISTANT_VOICE_H
