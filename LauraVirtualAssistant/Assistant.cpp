#include "PCH.h"
#include "Assistant.h"

Voice* Assistant::s_Voice = nullptr;

Assistant::Assistant()
{
	s_Voice = new Voice;
}

Assistant::~Assistant()
{
	delete s_Voice;
}

void Assistant::Start()
{
	s_Voice->Speak(L"Hello sir");
	s_Voice->Speak(L"My name is Laura");
	s_Voice->Speak(L"I am your windows virtual assistant");
}

void Assistant::Update()
{
}

void Assistant::End()
{
	MessageBox(NULL, L"Test!!!", L"Window", MB_OK | MB_ICONWARNING);
}
