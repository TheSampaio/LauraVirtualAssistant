#include "PCH.h"
#include "Application.h"

int WINAPI WinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ PSTR lpCmdLine, _In_ INT nCmdShow)
{
	// Instantiate application
	Application Engine;

	// Setup window
	Engine.GetWindow()->SetTitle("Laura Virual Assistant");
	Engine.GetWindow()->SetBackgroundColor(230, 50, 40);

	// Start application
	return Engine.Start();
}