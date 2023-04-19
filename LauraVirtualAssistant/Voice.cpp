#include "PCH.h"
#include "Voice.h"

#include "Debug.h"

// Initializes voice
Voice::Voice()
    : m_Voice(nullptr)
{
    m_Result = 0;

    // Initializes Windows COM
    if (FAILED(CoInitialize(NULL)))
    {
        Debug::Message(Error, L"Failed to initialize COM.", L"Voice");
        exit(EXIT_FAILURE);
    }
}

// Uninitializes voice and COM
Voice::~Voice()
{
    m_Voice->Release();
    m_Voice = nullptr;
    CoUninitialize();
}

// Speak function
void Voice::Speak(const std::wstring& Text)
{
    m_Result = CoCreateInstance(CLSID_SpVoice, NULL, CLSCTX_ALL, IID_ISpVoice, (void**) &m_Voice);
    if (SUCCEEDED(m_Result)) { m_Result = m_Voice->Speak((L"<rate absspeed='1'><voice required='Gender=Female; Age=Adult; Language=809'>" + Text).c_str(), NULL, nullptr); }
}
