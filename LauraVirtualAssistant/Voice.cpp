#include "PCH.h"
#include "Voice.h"

Voice::Voice()
    : m_Voice(nullptr)
{
    m_Result = 0;

    if (FAILED(CoInitialize(NULL)))
    {
        MessageBox(NULL, L"Failed to initialize COM", L"Voice", MB_OK | MB_ICONERROR);
        exit(EXIT_FAILURE);
    }
}

Voice::~Voice()
{
    m_Voice->Release();
    m_Voice = nullptr;
    CoUninitialize();   
}

void Voice::Speak(const std::wstring& Text)
{
    m_Result = CoCreateInstance(CLSID_SpVoice, NULL, CLSCTX_ALL, IID_ISpVoice, (void**) &m_Voice);
    if (SUCCEEDED(m_Result)) { m_Result = m_Voice->Speak((L"<voice required='Gender = Female;'>" + Text).c_str(), NULL, nullptr); }
}
