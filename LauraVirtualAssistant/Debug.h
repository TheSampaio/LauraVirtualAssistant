#pragma once

enum EDebugMode
{
	None = 0,
	Information,
	Warning,
	Error
};

class Debug
{
public:
	static inline void Message(EDebugMode DebugMode, LPCWSTR Text, LPCWSTR Caption) { Get().IMessage(DebugMode, Text, Caption); }

private:
	Debug() {};

	// Delete copy contructor and assigment operator
	Debug(const Debug&) = delete;
	Debug operator=(const Debug&) = delete;

	// Main methods
	void IMessage(EDebugMode DebugMode, LPCWSTR Text, LPCWSTR Caption);

	// Return the unique class's instance
	static Debug& Get() { static Debug m_Instance; return m_Instance; }
};

