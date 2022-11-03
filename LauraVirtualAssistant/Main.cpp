#include "PCH.h"
#include "Application.h"

int WINAPI WinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ PSTR lpCmdLine, _In_ INT nCmdShow)
{
	// Instantiate application
	Application Application;

	// Setup window
	Application.GetWindow()->SetTitle("Laura Virual Assistant");
	Application.GetWindow()->SetBackgroundColor(230, 53, 39);

	// Start application
	return Application.Start();;
}