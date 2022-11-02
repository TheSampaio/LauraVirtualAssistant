#include "PCH.h"
#include "Application.h"

Assistant* Application::s_Assistant = nullptr;

Application::Application()
	: m_IsRunning(false)
{
	s_Assistant = new Assistant;
}

Application::~Application()
{
	delete s_Assistant;
}

void Application::Start()
{
	s_Assistant->Start();
	Run();
}

void Application::Run()
{
	m_IsRunning = true;

	while (m_IsRunning)
	{
		s_Assistant->Update();
	}

	s_Assistant->End();
}
